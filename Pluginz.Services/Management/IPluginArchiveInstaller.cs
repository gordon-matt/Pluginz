namespace Pluginz.Management;

/// <summary>Installs a plugin from a zip file. NuGet packages are handled by the Pluginz.NuGet package.</summary>
public interface IPluginArchiveInstaller
{
    /// <summary>
    /// Unpacks the zip into a staging folder, checks every entry path, finds the plugin's manifest and hands the folder
    /// to <see cref="IPluginFolderInstaller"/>. The plugin is added but not installed.
    /// </summary>
    Task<Result<PluginInfo>> InstallAsync(Stream archive, string? fileName = null, CancellationToken cancellationToken = default);
}
