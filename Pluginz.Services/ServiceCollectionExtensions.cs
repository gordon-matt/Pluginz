using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pluginz.Hosting;
using Pluginz.Management;
using Pluginz.Registry;
using Pluginz.State;

namespace Pluginz;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Finds the plugins, loads the installed and enabled ones, lets them register their services and adds the
        /// Pluginz services. Call it while building the host, before <c>Build()</c>. A plugin can add services only now.
        /// </summary>
        /// <param name="configuration">The host configuration. Options bind from the <c>Pluginz</c> section.</param>
        /// <param name="environment">Gives the content root, which relative plugin paths are resolved against.</param>
        /// <param name="configure">Changes the options after they are bound.</param>
        public IServiceCollection AddPluginz(
            IConfiguration configuration,
            IHostEnvironment environment,
            Action<PluginzOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);

            if (services.Any(descriptor => descriptor.ServiceType == typeof(PluginRuntime)))
            {
                throw new InvalidOperationException("AddPluginz was already called.");
            }

            var options = new PluginzOptions();
            configuration.GetSection(PluginzOptions.SectionName).Bind(options);
            configure?.Invoke(options);

            var runtime = PluginRuntime.Create(options, configuration, environment.ContentRootPath, environment.EnvironmentName);

            services.AddSingleton(Options.Create(options));
            services.AddSingleton(runtime);
            services.AddSingleton<IPluginRegistry>(runtime.Registry);
            services.AddSingleton(runtime.RegistryInternal);
            services.AddSingleton(runtime.StateStore);
            services.AddSingleton(runtime.Rules);

            services.AddSingleton<IPluginManager, PluginManager>();
            services.AddSingleton<IPluginFolderInstaller, PluginFolderInstaller>();
            services.AddSingleton<IPluginArchiveInstaller, PluginArchiveInstaller>();
            services.AddScoped<IPluginFinder, PluginFinder>();
            services.TryAddScoped<IPluginScope, NullPluginScope>();
            services.AddHostedService<PluginHostedService>();

            // Last, so a plugin can see (and replace) what the host and Pluginz registered.
            runtime.ConfigureServices(services);

            return services;
        }

        /// <summary>Same as <see cref="AddPluginz(IServiceCollection, IConfiguration, IHostEnvironment, Action{PluginzOptions})"/>, using <typeparamref name="TScope"/> as the current tenant and roles.</summary>
        public IServiceCollection AddPluginz<TScope>(
            IConfiguration configuration,
            IHostEnvironment environment,
            Action<PluginzOptions>? configure = null)
            where TScope : class, IPluginScope
        {
            services.AddScoped<IPluginScope, TScope>();
            return services.AddPluginz(configuration, environment, configure);
        }

        /// <summary>
        /// Registers every concrete type in the loaded plugins that implements <typeparamref name="TContract"/>, so a
        /// plugin only has to implement the contract. Call it after <c>AddPluginz</c>.
        /// </summary>
        public IServiceCollection AddPluginExports<TContract>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where TContract : class
        {
            var registry = services.GetPluginRegistry();

            foreach (var type in registry.GetTypes<TContract>())
            {
                services.Add(new ServiceDescriptor(typeof(TContract), type, lifetime));
            }

            return services;
        }

        /// <summary>The registry created by <c>AddPluginz</c>.</summary>
        public IPluginRegistry GetPluginRegistry() =>
            services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IPluginRegistry))?.ImplementationInstance as IPluginRegistry
            ?? throw new InvalidOperationException("Call AddPluginz first.");
    }

    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Same as <c>services.AddPluginz(...)</c>, using the builder's configuration and environment.</summary>
        public IHostApplicationBuilder AddPluginz(Action<PluginzOptions>? configure = null)
        {
            builder.Services.AddPluginz(builder.Configuration, builder.Environment, configure);
            return builder;
        }

        /// <summary>Same as <see cref="AddPluginz(IHostApplicationBuilder, Action{PluginzOptions})"/>, with a tenant and role scope.</summary>
        public IHostApplicationBuilder AddPluginz<TScope>(Action<PluginzOptions>? configure = null)
            where TScope : class, IPluginScope
        {
            builder.Services.AddPluginz<TScope>(builder.Configuration, builder.Environment, configure);
            return builder;
        }
    }
}
