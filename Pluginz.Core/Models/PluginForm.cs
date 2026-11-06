namespace Pluginz.Models;

/// <summary>The settings an administrator may override without touching the plugin's files.</summary>
public sealed class PluginForm
{
    /// <summary>
    /// Gets or sets the system name.
    /// </summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>Leave empty to use the name from the manifest.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>Leave null to use the order from the manifest.</summary>
    public int? DisplayOrder { get; set; }

    /// <summary>Leave null to use the tenants from the manifest. An empty list means every tenant.</summary>
    public List<string>? Tenants { get; set; }

    /// <summary>Leave null to use the roles from the manifest. An empty list means every role.</summary>
    public List<string>? Roles { get; set; }
}

/// <summary>A NuGet package that can be installed as a plugin.</summary>
public sealed class PluginPackageInfo
{
    /// <summary>
    /// Gets or sets the package identifier.
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the authors.
    /// </summary>
    public string? Authors { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the project URL.
    /// </summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// Gets or sets the download count.
    /// </summary>
    public long? DownloadCount { get; set; }

    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is prerelease.
    /// </summary>
    public bool IsPrerelease { get; set; }

    /// <summary>The system name of the installed plugin that this package maps to, or null.</summary>
    public string? InstalledSystemName { get; set; }

    /// <summary>
    /// Gets or sets the installed version.
    /// </summary>
    public string? InstalledVersion { get; set; }
}

/// <summary>A newer version of an installed NuGet plugin.</summary>
public sealed class PluginUpdateInfo
{
    /// <summary>
    /// Gets or sets the system name.
    /// </summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the package identifier.
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current version.
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latest version.
    /// </summary>
    public string LatestVersion { get; set; } = string.Empty;
}