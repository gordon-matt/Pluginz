namespace Pluginz.Packages;

/// <summary>
/// Finds plugins on NuGet feeds and installs them. An installed package is added to the plugins folder together with
/// the dependencies the host does not already provide. It is discovered, not installed.
/// Call <see cref="Management.IPluginManager.InstallAsync"/> next.
/// </summary>
/// <remarks>
/// Pluginz does not verify NuGet package signatures. Install only from feeds you trust. Plugin code runs inside the host.
/// </remarks>
public interface IPluginPackageService
{
    /// <summary>Searches every configured feed.</summary>
    Task<Result<IReadOnlyList<PluginPackageInfo>>> SearchAsync(
        string? query,
        bool includePrerelease = false,
        int skip = 0,
        int take = 30,
        CancellationToken cancellationToken = default);

    /// <summary>The versions a package has on the configured feeds, newest first.</summary>
    Task<Result<IReadOnlyList<string>>> GetVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads a package and its dependencies from the feeds and adds the plugin. A null version means the latest.</summary>
    Task<Result<PluginInfo>> InstallAsync(
        string packageId,
        string? version = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a plugin from a .nupkg file. Dependencies that the package needs are fetched from the feeds.</summary>
    Task<Result<PluginInfo>> InstallFromFileAsync(
        Stream package,
        string? fileName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Compares every plugin that came from NuGet with the latest version on its feeds.</summary>
    Task<Result<IReadOnlyList<PluginUpdateInfo>>> GetUpdatesAsync(
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);
}