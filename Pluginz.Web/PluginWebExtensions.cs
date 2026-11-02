using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Pluginz.Discovery;
using Pluginz.Hosting;
using Pluginz.Registry;
using Pluginz.Web;

namespace Pluginz;

public static class PluginWebExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds <see cref="IPluginWebAssemblies"/>. Call it after <c>AddPluginz</c>.</summary>
        public IServiceCollection AddPluginzWeb()
        {
            services.GetPluginRegistry();
            services.TryAddSingleton<IPluginWebAssemblies, PluginWebAssemblies>();
            return services;
        }
    }

    extension(IMvcBuilder builder)
    {
        /// <summary>
        /// Makes the controllers, view components and compiled views of the active plugins part of the MVC
        /// application. Call it after <c>AddPluginz</c>, on the result of <c>AddControllers()</c> or similar.
        /// </summary>
        public IMvcBuilder AddPluginzParts()
        {
            var registry = builder.Services.GetPluginRegistry();
            var parts = builder.PartManager.ApplicationParts;

            foreach (var assembly in registry.ActiveAssemblies)
            {
                foreach (var candidate in GetAssemblyWithRelated(assembly))
                {
                    foreach (var part in ApplicationPartFactory.GetApplicationPartFactory(candidate).GetApplicationParts(candidate))
                    {
                        if (!parts.Any(existing => existing.Name == part.Name))
                        {
                            parts.Add(part);
                        }
                    }
                }
            }

            return builder;
        }
    }

    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Serves each active plugin's <c>wwwroot</c> and lets <see cref="IWebPlugin"/> plugins add middleware. Call it after
        /// <c>UseStaticFiles()</c> and before <c>UseRouting()</c>.
        /// </summary>
        /// <remarks>
        /// Files are served at <c>/_content/{AssemblyName}</c>, the same address a Razor class library uses, and at
        /// <c>/plugins/{SystemName}</c>.
        /// </remarks>
        public IApplicationBuilder UsePluginz()
        {
            var runtime = app.ApplicationServices.GetRequiredService<PluginRuntime>();
            var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Pluginz");

            foreach (var entry in runtime.RegistryInternal.ActiveEntries)
            {
                var wwwroot = entry.LoadedDirectory is null ? null : Path.Combine(entry.LoadedDirectory, "wwwroot");
                if (wwwroot is not null && Directory.Exists(wwwroot))
                {
                    var assemblyName = entry.Assembly!.GetName().Name!;
                    var provider = new PhysicalFileProvider(wwwroot);

                    app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, RequestPath = $"/_content/{assemblyName}" });

                    if (!string.Equals(assemblyName, entry.SystemName, StringComparison.OrdinalIgnoreCase))
                    {
                        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, RequestPath = $"/_content/{entry.SystemName}" });
                    }

                    app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, RequestPath = $"/plugins/{entry.SystemName}" });
                    logger.LogDebug("Serving static files of plugin {SystemName} from {Path}", entry.SystemName, wwwroot);
                }

                if (entry is { Instance: IWebPlugin web, Context: not null })
                {
                    web.Configure(app, new PluginRuntimeContext(entry.Context, app.ApplicationServices));
                }
            }

            return app;
        }
    }

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Calls <see cref="IWebPlugin.MapEndpoints"/> on every active web plugin.</summary>
        public IEndpointRouteBuilder MapPluginz()
        {
            var runtime = endpoints.ServiceProvider.GetRequiredService<PluginRuntime>();

            foreach (var entry in runtime.RegistryInternal.ActiveEntries)
            {
                if (entry is { Instance: IWebPlugin web, Context: not null })
                {
                    web.MapEndpoints(endpoints, new PluginRuntimeContext(entry.Context, endpoints.ServiceProvider));
                }
            }

            return endpoints;
        }
    }

    extension(RazorComponentsEndpointConventionBuilder builder)
    {
        /// <summary>
        /// Adds the active plugins' assemblies to <c>MapRazorComponents</c>, so that their <c>@page</c> components get
        /// endpoints. The router needs the same assemblies. See <see cref="IPluginWebAssemblies"/>.
        /// </summary>
        public RazorComponentsEndpointConventionBuilder AddPluginzAssemblies(IServiceProvider services)
        {
            var assemblies = services.GetRequiredService<IPluginRegistry>().ActiveAssemblies;
            return assemblies.Count == 0 ? builder : builder.AddAdditionalAssemblies([.. assemblies]);
        }
    }

    private static IEnumerable<Assembly> GetAssemblyWithRelated(Assembly assembly)
    {
        yield return assembly;

        // Compiled Razor views live in a separate assembly that the main one points to. The framework would load it
        // into the default context, so load it into the plugin's own context instead.
        var context = AssemblyLoadContext.GetLoadContext(assembly);
        var directory = Path.GetDirectoryName(assembly.Location);
        if (context is null || directory is null)
        {
            yield break;
        }

        foreach (var related in assembly.GetCustomAttributes<RelatedAssemblyAttribute>())
        {
            var path = Path.Combine(directory, $"{related.AssemblyFileName}.dll");
            if (File.Exists(path))
            {
                yield return context.LoadFromAssemblyPath(path);
            }
        }
    }
}
