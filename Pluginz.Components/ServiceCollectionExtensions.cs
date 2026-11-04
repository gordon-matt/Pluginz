using Heroicsoft.NetForge.Components;

namespace Pluginz.Components;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Blazor admin and its menu contributor. Call <c>AddPluginz</c> first. Add
        /// <c>AddPluginzNuGet</c> to get the NuGet browser, and add this assembly to
        /// <c>ForgeRouter.AdditionalAssemblies</c>.
        /// </summary>
        public IServiceCollection AddPluginzComponents()
        {
            // Throws a clear message when AddPluginz has not run.
            services.GetPluginRegistry();

            if (!services.Any(descriptor => descriptor.ServiceType == typeof(ToastService)))
            {
                services.AddNetForgeComponents();
            }

            services.AddNetForgeMenuContributor<PluginzMenuContributor>();
            return services;
        }
    }
}