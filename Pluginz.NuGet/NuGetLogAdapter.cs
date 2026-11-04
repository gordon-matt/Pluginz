using NuGet.Common;

namespace Pluginz.Packages;

/// <summary>Sends NuGet's log messages to <see cref="ILogger"/>.</summary>
internal sealed class NuGetLogAdapter(Microsoft.Extensions.Logging.ILogger logger) : LoggerBase
{
    public override void Log(ILogMessage message) =>
        logger.Log(Map(message.Level), "NuGet: {Message}", message.Message);

    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }

    private static Microsoft.Extensions.Logging.LogLevel Map(NuGet.Common.LogLevel level) => level switch
    {
        NuGet.Common.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        NuGet.Common.LogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Debug,
        NuGet.Common.LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
        NuGet.Common.LogLevel.Minimal => Microsoft.Extensions.Logging.LogLevel.Information,
        NuGet.Common.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        NuGet.Common.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        _ => Microsoft.Extensions.Logging.LogLevel.Information,
    };
}