using Pluginz.Discovery;

namespace Pluginz.Management;

/// <summary>
/// Puts an unpacked plugin folder into the plugins folder. Archive upload and NuGet install both end here. The
/// plugin is discovered but not installed. Call <see cref="IPluginManager.InstallAsync"/> next.
/// </summary>
public interface IPluginFolderInstaller
{
    /// <summary>
    /// Validates the manifest in <paramref name="sourceDirectory"/>, then moves the folder into place. The source
    /// folder is consumed. When the plugin already exists, it is replaced. A plugin that is loaded from its own folder
    /// is staged instead and replaced at the next start.
    /// </summary>
    Task<Result<PluginInfo>> InstallAsync(string sourceDirectory, PluginOriginRecord origin, CancellationToken cancellationToken = default);
}