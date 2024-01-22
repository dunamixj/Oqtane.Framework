using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Oqtane.Models;
using Oqtane.Shared;
using System.Linq;
using Oqtane.Enums;
using Oqtane.Infrastructure;
using Oqtane.Repository;
using System.Net;
using Oqtane.Security;
using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Oqtane.Extensions;
using System;
using Oqtane.UI;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Oqtane.Controllers
{
    [Route(ControllerRoutes.ApiRoute)]
    public class SiteController : Controller
    {
        private readonly ISiteRepository _sites;
        private readonly IPageRepository _pages;
        private readonly IThemeRepository _themes;
        private readonly IModuleRepository _modules;
        private readonly IPageModuleRepository _pageModules;
        private readonly IModuleDefinitionRepository _moduleDefinitions;
        private readonly ILanguageRepository _languages;
        private readonly IUserPermissions _userPermissions;
        private readonly ISettingRepository _settings;
        private readonly ISyncManager _syncManager;
        private readonly ILogManager _logger;
        private readonly IMemoryCache _cache;
        private readonly Alias _alias;

        public SiteController(ISiteRepository sites, IPageRepository pages, IThemeRepository themes, IModuleRepository modules, IPageModuleRepository pageModules, IModuleDefinitionRepository moduleDefinitions, ILanguageRepository languages, IUserPermissions userPermissions, ISettingRepository settings, ITenantManager tenantManager, ISyncManager syncManager, ILogManager logger, IMemoryCache cache)
        {
            _sites = sites;
            _pages = pages;
            _themes = themes;
            _modules = modules;
            _pageModules = pageModules;
            _moduleDefinitions = moduleDefinitions;
            _languages = languages;
            _userPermissions = userPermissions;
            _settings = settings;
            _syncManager = syncManager;
            _logger = logger;
            _cache = cache;
            _alias = tenantManager.GetAlias();
        }

        // GET: api/<controller>
        [HttpGet]
        [Authorize(Roles = RoleNames.Host)]
        public IEnumerable<Site> Get()
        {
            return _sites.GetSites();
        }

        // GET api/<controller>/5
        [HttpGet("{id}")]
        public Site Get(int id)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return _cache.GetOrCreate($"site:{HttpContext.GetAlias().SiteKey}", entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromMinutes(30);
                    return GetSite(id);
                });
            }
            else
            {
                return GetSite(id);
            }
        }

        private Site GetSite(int siteid)
        {
            var site = _sites.GetSite(siteid);
            if (site != null && site.SiteId == _alias.SiteId)
            {
                // site settings
                site.Settings = _settings.GetSettings(EntityNames.Site, site.SiteId)
                    .Where(item => !item.IsPrivate || User.IsInRole(RoleNames.Admin))
                    .ToDictionary(setting => setting.SettingName, setting => setting.SettingValue);

                // populate File Extensions 
                site.ImageFiles = site.Settings.ContainsKey("ImageFiles") && !string.IsNullOrEmpty(site.Settings["ImageFiles"])
                    ? site.Settings["ImageFiles"] : Constants.ImageFiles;
                site.UploadableFiles = site.Settings.ContainsKey("UploadableFiles") && !string.IsNullOrEmpty(site.Settings["UploadableFiles"])
                    ? site.ImageFiles + site.Settings["UploadableFiles"] : Constants.UploadableFiles;

                // pages
                List<Setting> settings = _settings.GetSettings(EntityNames.Page).ToList();
                site.Pages = new List<Page>();
                foreach (Page page in _pages.GetPages(site.SiteId))
                {
                    if (!page.IsDeleted && _userPermissions.IsAuthorized(User, PermissionNames.View, page.PermissionList) && (Utilities.IsPageModuleVisible(page.EffectiveDate, page.ExpiryDate) || _userPermissions.IsAuthorized(User, PermissionNames.Edit, page.PermissionList)))
                    {
                        page.Settings = settings.Where(item => item.EntityId == page.PageId)
                            .Where(item => !item.IsPrivate || _userPermissions.IsAuthorized(User, PermissionNames.Edit, page.PermissionList))
                            .ToDictionary(setting => setting.SettingName, setting => setting.SettingValue);
                        site.Pages.Add(page);
                    }
                }

                site.Pages = GetPagesHierarchy(site.Pages);

                // modules
                List<ModuleDefinition> moduledefinitions = _moduleDefinitions.GetModuleDefinitions(site.SiteId).ToList();
                settings = _settings.GetSettings(EntityNames.Module).ToList();
                site.Modules = new List<Module>();
                foreach (PageModule pagemodule in _pageModules.GetPageModules(site.SiteId).Where(pm => !pm.IsDeleted && _userPermissions.IsAuthorized(User, PermissionNames.View, pm.Module.PermissionList)))
                {
                    if(Utilities.IsPageModuleVisible(pagemodule.EffectiveDate, pagemodule.ExpiryDate) || _userPermissions.IsAuthorized(User, PermissionNames.Edit, pagemodule.Module.PermissionList))
                    {
                        Module module = new Module
                        {
                            SiteId = pagemodule.Module.SiteId,
                            ModuleDefinitionName = pagemodule.Module.ModuleDefinitionName,
                            AllPages = pagemodule.Module.AllPages,
                            PermissionList = pagemodule.Module.PermissionList,
                            CreatedBy = pagemodule.Module.CreatedBy,
                            CreatedOn = pagemodule.Module.CreatedOn,
                            ModifiedBy = pagemodule.Module.ModifiedBy,
                            ModifiedOn = pagemodule.Module.ModifiedOn,
                            DeletedBy = pagemodule.DeletedBy,
                            DeletedOn = pagemodule.DeletedOn,
                            IsDeleted = pagemodule.IsDeleted,

                            PageModuleId = pagemodule.PageModuleId,
                            ModuleId = pagemodule.ModuleId,
                            PageId = pagemodule.PageId,
                            Title = pagemodule.Title,
                            Pane = pagemodule.Pane,
                            Order = pagemodule.Order,
                            ContainerType = pagemodule.ContainerType,
                            EffectiveDate = pagemodule.EffectiveDate,
                            ExpiryDate = pagemodule.ExpiryDate,

                            ModuleDefinition = _moduleDefinitions.FilterModuleDefinition(moduledefinitions.Find(item => item.ModuleDefinitionName == pagemodule.Module.ModuleDefinitionName)),

                            Settings = settings
                            .Where(item => item.EntityId == pagemodule.ModuleId)
                            .Where(item => !item.IsPrivate || _userPermissions.IsAuthorized(User, PermissionNames.Edit, pagemodule.Module.PermissionList))
                            .ToDictionary(setting => setting.SettingName, setting => setting.SettingValue)
                        };

                        site.Modules.Add(module);
                    }
                }

                site.Modules = site.Modules.OrderBy(item => item.PageId).ThenBy(item => item.Pane).ThenBy(item => item.Order).ToList();

                // languages
                site.Languages = _languages.GetLanguages(site.SiteId).ToList();
                var defaultCulture = CultureInfo.GetCultureInfo(Constants.DefaultCulture);
                site.Languages.Add(new Language { Code = defaultCulture.Name, Name = defaultCulture.DisplayName, Version = Constants.Version, IsDefault = !site.Languages.Any(l => l.IsDefault) });

                // themes
                site.Themes = _themes.FilterThemes(_themes.GetThemes().ToList());

                return site;
            }
            else
            {
                if (site != null)
                {
                    _logger.Log(LogLevel.Error, this, LogFunction.Security, "Unauthorized Site Get Attempt {SiteId}", siteid);
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                }
                else
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
                return null;
            }
        }

        // POST api/<controller>
        [HttpPost]
        [Authorize(Roles = RoleNames.Host)]
        public Site Post([FromBody] Site site)
        {
            if (ModelState.IsValid)
            {
                site = _sites.AddSite(site);
                _syncManager.AddSyncEvent(_alias.TenantId, EntityNames.Site, site.SiteId, SyncEventActions.Create);
                _logger.Log(site.SiteId, LogLevel.Information, this, LogFunction.Create, "Site Added {Site}", site);
            }
            else
            {
                _logger.Log(LogLevel.Error, this, LogFunction.Security, "Unauthorized Site Post Attempt {Site}", site);
                HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                site = null;
            }
            return site;
        }

        // PUT api/<controller>/5
        [HttpPut("{id}")]
        [Authorize(Roles = RoleNames.Admin)]
        public Site Put(int id, [FromBody] Site site)
        {
            var current = _sites.GetSite(site.SiteId, false);
            if (ModelState.IsValid && site.SiteId == _alias.SiteId && site.TenantId == _alias.TenantId && site.SiteId == id && current != null)
            {
                site = _sites.UpdateSite(site);
                _syncManager.AddSyncEvent(_alias.TenantId, EntityNames.Site, site.SiteId, SyncEventActions.Update);
                string action = SyncEventActions.Refresh;
                if (current.Runtime != site.Runtime || current.RenderMode != site.RenderMode)
                {
                    action = SyncEventActions.Reload;
                }
                _syncManager.AddSyncEvent(_alias.TenantId, EntityNames.Site, site.SiteId, action);
                _logger.Log(site.SiteId, LogLevel.Information, this, LogFunction.Update, "Site Updated {Site}", site);
            }
            else
            {
                _logger.Log(LogLevel.Error, this, LogFunction.Security, "Unauthorized Site Put Attempt {Site}", site);
                HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                site = null;
            }
            return site;
        }

        // DELETE api/<controller>/5
        [HttpDelete("{id}")]
        [Authorize(Roles = RoleNames.Host)]
        public void Delete(int id)
        {
            var site = _sites.GetSite(id);
            if (site != null && site.SiteId == _alias.SiteId)
            {
                _sites.DeleteSite(id);
                _syncManager.AddSyncEvent(_alias.TenantId, EntityNames.Site, site.SiteId, SyncEventActions.Delete);
                _logger.Log(id, LogLevel.Information, this, LogFunction.Delete, "Site Deleted {SiteId}", id);
            }
            else
            {
                _logger.Log(LogLevel.Error, this, LogFunction.Security, "Unauthorized Site Delete Attempt {SiteId}", id);
                HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            }
        }

        private static List<Page> GetPagesHierarchy(List<Page> pages)
        {
            List<Page> hierarchy = new List<Page>();
            Action<List<Page>, Page> getPath = null;
            getPath = (pageList, page) =>
            {
                IEnumerable<Page> children;
                int level;
                if (page == null)
                {
                    level = -1;
                    children = pages.Where(item => item.ParentId == null);
                }
                else
                {
                    level = page.Level;
                    children = pages.Where(item => item.ParentId == page.PageId);
                }
                foreach (Page child in children)
                {
                    child.Level = level + 1;
                    child.HasChildren = pages.Any(item => item.ParentId == child.PageId && !item.IsDeleted && item.IsNavigation);
                    hierarchy.Add(child);
                    getPath(pageList, child);
                }
            };
            pages = pages.OrderBy(item => item.Order).ToList();
            getPath(pages, null);

            // add any non-hierarchical items to the end of the list
            foreach (Page page in pages)
            {
                if (hierarchy.Find(item => item.PageId == page.PageId) == null)
                {
                    hierarchy.Add(page);
                }
            }
            return hierarchy;
        }
    }
}
