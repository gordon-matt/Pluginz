using Microsoft.Extensions.Configuration;
using Pluginz.Hosting;
using Pluginz.Packages;

namespace Pluginz;

/// <summary>
/// Provides extension methods for nu get service collections.
/// </summary>
public static class NuGetServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds <see cref="IPluginPackageService"/>, which installs plugins from NuGet packages. Call it after
        /// <c>AddPluginz</c>. Options bind from the <c>Pluginz:NuGet</c> configuration section when the host has one.
        /// </summary>
        public IServiceCollection AddPluginzNuGet(Action<PluginzNuGetOptions>? configure = null)
        {
            var builder = services.AddOptions<PluginzNuGetOptions>();
            // Binds from the configuration that AddPluginz was given, so this works whether or not the host registered IConfiguration yet.
            builder.Configure<PluginRuntime>((options, runtime) =>
                runtime.Configuration.GetSection(PluginzNuGetOptions.SectionName).Bind(options));

            if (configure is not null)
            {
                builder.Configure(configure);
            }

            services.AddSingleton<IPluginPackageService, PluginPackageService>();
            return services;
        }
    }
}