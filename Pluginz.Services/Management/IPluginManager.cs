namespace Pluginz.Management;

/// <summary>
/// Changes what is installed, enabled and configured. Reading is the job of <see cref="Registry.IPluginRegistry"/>.
/// </summary>
/// <remarks>
/// A plugin registers its services while the host starts, so a change takes effect after the next restart. Each
/// result carries the new <see cref="PluginInfo"/>, whose <see cref="PluginInfo.RestartRequired"/> says so.
/// </remarks>
public interface IPluginManager
{
    /// <summary>Runs the plugin's install code, then records it as installed and enabled.</summary>
    Task<Result<PluginInfo>> InstallAsync(string systemName, CancellationToken cancellationToken = default);

    /// <summary>Runs the plugin's uninstall code, then records it as not installed. Its settings and data folder stay.</summary>
    Task<Result<PluginInfo>> UninstallAsync(string systemName, CancellationToken cancellationToken = default);

    /// <summary>Turns an installed plugin on.</summary>
    Task<Result<PluginInfo>> EnableAsync(string systemName, CancellationToken cancellationToken = default);

    /// <summary>Turns an installed plugin off without uninstalling it.</summary>
    Task<Result<PluginInfo>> DisableAsync(string systemName, CancellationToken cancellationToken = default);

    /// <summary>Saves the administrator's overrides: friendly name, display order, tenants and roles.</summary>
    Task<Result<PluginInfo>> UpdateAsync(PluginForm form, CancellationToken cancellationToken = default);

    /// <summary>Deletes the files of a plugin that is not installed.</summary>
    Task<Result> DeleteAsync(string systemName, CancellationToken cancellationToken = default);

    /// <summary>Reads the plugins folder again.</summary>
    Task<Result<IReadOnlyList<PluginInfo>>> RescanAsync(CancellationToken cancellationToken = default);
}
