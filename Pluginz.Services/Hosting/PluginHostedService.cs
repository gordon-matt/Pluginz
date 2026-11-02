using Microsoft.Extensions.Hosting;

namespace Pluginz.Hosting;

/// <summary>
/// Writes the messages collected during plugin discovery to the real logger, then starts and stops each active
/// plugin. A plugin that throws is logged and skipped. It does not stop the host.
/// </summary>
internal sealed class PluginHostedService(PluginRuntime runtime, IServiceProvider services, ILogger<PluginHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (level, message, error) in runtime.StartupLog)
        {
            logger.Log(level, error, "{Message}", message);
        }

        foreach (var entry in runtime.RegistryInternal.ActiveEntries)
        {
            if (entry is not { Instance: not null, Context: not null })
            {
                continue;
            }

            try
            {
                await entry.Instance.StartAsync(new PluginRuntimeContext(entry.Context, services), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Plugin {SystemName} failed to start", entry.SystemName);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in runtime.RegistryInternal.ActiveEntries.Reverse())
        {
            if (entry is not { Instance: not null, Context: not null })
            {
                continue;
            }

            try
            {
                await entry.Instance.StopAsync(new PluginRuntimeContext(entry.Context, services), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Plugin {SystemName} failed to stop", entry.SystemName);
            }
        }
    }
}
