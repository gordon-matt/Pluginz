namespace Pluginz;

/// <summary>
/// The entry point of a plugin. A plugin assembly contains one public, concrete type that implements this
/// interface and has a parameterless constructor. Every member has a default, so implement only what you need.
/// Most plugins derive from <see cref="PluginBase"/> instead.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConfigureServices"/> runs once, while the host builds its service container, and only when the
/// plugin is installed and enabled. A plugin that is not installed contributes nothing to the host.
/// </para>
/// <para>
/// <see cref="InstallAsync"/> and <see cref="UninstallAsync"/> run when an administrator installs or removes the
/// plugin. They run even when the plugin is not active in this process, so do not assume that the services
/// registered in <see cref="ConfigureServices"/> exist. <see cref="PluginRuntimeContext.Services"/> holds the
/// host's services, not the plugin's.
/// </para>
/// </remarks>
public interface IPlugin
{
    /// <summary>
    /// A host-relative URL of the plugin's settings page, or null when there is none. The admin UI shows a
    /// Configure link for installed plugins that return a value.
    /// </summary>
    string? ConfigurationUrl => null;

    /// <summary>Registers the plugin's services into the host container.</summary>
    void ConfigureServices(IServiceCollection services, PluginContext context)
    {
    }

    /// <summary>Runs once when the plugin is installed. Create tables, seed data, write defaults.</summary>
    Task InstallAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Runs once when the plugin is uninstalled. Remove what <see cref="InstallAsync"/> created.</summary>
    Task UninstallAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Runs when the host starts, after the container is built, for each active plugin.</summary>
    Task StartAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Runs when the host stops, for each active plugin, in reverse start order.</summary>
    Task StopAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}