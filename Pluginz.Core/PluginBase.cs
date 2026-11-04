namespace Pluginz;

/// <summary>A convenient base class for <see cref="IPlugin"/> with overridable, empty members.</summary>
public abstract class PluginBase : IPlugin
{
    /// <inheritdoc />
    public virtual string? ConfigurationUrl => null;

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services, PluginContext context)
    {
    }

    /// <inheritdoc />
    public virtual Task InstallAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task UninstallAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task StartAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task StopAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}