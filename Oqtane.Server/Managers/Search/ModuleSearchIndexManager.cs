using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oqtane.Interfaces;
using Oqtane.Models;
using Oqtane.Repository;
using Oqtane.Shared;

namespace Oqtane.Managers.Search
{
    public class ModuleSearchIndexManager : SearchIndexManagerBase
    {
        public const int ModuleSearchIndexManagerPriority = 200;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ModuleSearchIndexManager> _logger;
        private readonly IPageModuleRepository _pageModuleRepostory;
        private readonly IPageRepository _pageRepository;
        private readonly ISettingRepository _settingRepository;

        public ModuleSearchIndexManager(
            IServiceProvider serviceProvider,
            IPageModuleRepository pageModuleRepostory,
            ILogger<ModuleSearchIndexManager> logger,
            IPageRepository pageRepository,
            ISettingRepository settingRepository)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _pageModuleRepostory = pageModuleRepostory;
            _pageRepository = pageRepository;
            _settingRepository = settingRepository;
        }

        public override string Name => EntityNames.Module;

        public override int Priority => ModuleSearchIndexManagerPriority;

        public override async Task<int> IndexContent(int siteId, DateTime? startTime, Func<List<SearchContent>, Task> processSearchContent, Func<string, Task> handleError)
        {
            var pageModules = _pageModuleRepostory.GetPageModules(siteId).DistinctBy(i => i.ModuleId);
            var searchContentList = new List<SearchContent>();

            foreach(var pageModule in pageModules)
            {
                var page = _pageRepository.GetPage(pageModule.PageId);
                if(page == null)
                {
                    continue;
                }

                var module = pageModule.Module;
                var allowIndex = AllowIndex(page);
                if (module.ModuleDefinition.ServerManagerType != "")
                {
                    _logger.LogDebug($"Search: Begin index module {module.ModuleId}.");
                    var type = Type.GetType(module.ModuleDefinition.ServerManagerType);
                    if (type?.GetInterface(nameof(ISearchable)) != null)
                    {
                        try
                        {
                            var moduleSearch = (ISearchable)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                            var contentList = moduleSearch.GetSearchContents(pageModule, startTime.GetValueOrDefault(DateTime.MinValue));
                            if(contentList != null)
                            {
                                foreach(var searchContent in contentList)
                                {
                                    SaveModuleMetaData(searchContent, pageModule);

                                    if(searchContent.IsActive)
                                    {
                                        searchContent.IsActive = allowIndex;
                                    }

                                    searchContentList.Add(searchContent);
                                }
                            }
                            
                        }
                        catch(Exception ex)
                        {
                            _logger.LogError(ex, $"Search: Index module {module.ModuleId} failed.");
                            await handleError($"Search: Index module {module.ModuleId} failed: {ex.Message}");
                        }
                    }
                    _logger.LogDebug($"Search: End index module {module.ModuleId}.");
                }
            }

            await processSearchContent(searchContentList);

            return searchContentList.Count;
        }

        private void SaveModuleMetaData(SearchContent searchContent, PageModule pageModule)
        {
            searchContent.SiteId = pageModule.Module.SiteId;

            if(string.IsNullOrEmpty(searchContent.EntityName))
            {
                searchContent.EntityName = EntityNames.Module;
            }

            if(searchContent.EntityId == 0)
            {
                searchContent.EntityId = pageModule.ModuleId;
            }

            if (searchContent.IsActive)
            {
                searchContent.IsActive = !pageModule.Module.IsDeleted;
            }

            if (searchContent.ContentAuthoredOn == DateTime.MinValue)
            {
                searchContent.ContentAuthoredOn = pageModule.ModifiedOn;
            }

            if (string.IsNullOrEmpty(searchContent.AdditionalContent))
            {
                searchContent.AdditionalContent = string.Empty;
            }

            var page = _pageRepository.GetPage(pageModule.PageId);

            if (string.IsNullOrEmpty(searchContent.Url) && page != null)
            {
                searchContent.Url = $"{(!string.IsNullOrEmpty(page.Path) && !page.Path.StartsWith("/") ? "/" : "")}{page.Path}";
            }

            if (string.IsNullOrEmpty(searchContent.Title) && page != null)
            {
                searchContent.Title = !string.IsNullOrEmpty(page.Title) ? page.Title : page.Name;
            }

            if (searchContent.SearchContentProperties == null)
            {
                searchContent.SearchContentProperties = new List<SearchContentProperty>();
            }

            if(!searchContent.SearchContentProperties.Any(i => i.Name == Constants.SearchPageIdPropertyName))
            {
                searchContent.SearchContentProperties.Add(new SearchContentProperty { Name = Constants.SearchPageIdPropertyName, Value = pageModule.PageId.ToString() });
            }

            if (!searchContent.SearchContentProperties.Any(i => i.Name == Constants.SearchModuleIdPropertyName))
            {
                searchContent.SearchContentProperties.Add(new SearchContentProperty { Name = Constants.SearchModuleIdPropertyName, Value = pageModule.ModuleId.ToString() });
            }
        }

        private bool AllowIndex(Page page)
        {
            var setting = _settingRepository.GetSetting(EntityNames.Page, page.PageId, "AllowIndex");
            return setting == null || !bool.TryParse(setting.SettingValue, out bool allowed) || allowed;
        }
    }
}
