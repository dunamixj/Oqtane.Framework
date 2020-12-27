using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Oqtane.Infrastructure;
using Oqtane.Infrastructure.Startup;

namespace Oqtane.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder ConfigureOqtaneAssemblies(this IApplicationBuilder app, IWebHostEnvironment env)
        {
            var startUps = AppDomain.CurrentDomain
                .GetOqtaneAssemblies()
                .SelectMany(s => s.GetInstances<IServerStartup>())
                .OrderBy(s => s, new ServerStartupComparer());

            foreach (var startup in startUps)
            {
                startup.Configure(app, env);
            }

            return app;
        }

        public static IApplicationBuilder UseOqtaneLocalization(this IApplicationBuilder app)
        {
            var localizationManager = app.ApplicationServices.GetService<ILocalizationManager>();
            var defaultCulture = localizationManager.GetDefaultCulture();
            var supportedCultures = localizationManager.GetSupportedCultures();

            app.UseRequestLocalization(options => {
                options.SetDefaultCulture(defaultCulture)
                    .AddSupportedCultures(supportedCultures)
                    .AddSupportedUICultures(supportedCultures);
            });

            return app;
        }
    }
}
