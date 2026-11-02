namespace Pluginz.Models;

/// <summary>The settings an administrator may override without touching the plugin's files.</summary>
public sealed class PluginForm
{
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
    public string PackageId { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Authors { get; set; }

    public string? Description { get; set; }

    public string? Tags { get; set; }

    public string? IconUrl { get; set; }

    public string? ProjectUrl { get; set; }

    public long? DownloadCount { get; set; }

    public string? Source { get; set; }

    public bool IsPrerelease { get; set; }

    /// <summary>The system name of the installed plugin that this package maps to, or null.</summary>
    public string? InstalledSystemName { get; set; }

    public string? InstalledVersion { get; set; }
}

/// <summary>A newer version of an installed NuGet plugin.</summary>
public sealed class PluginUpdateInfo
{
    public string SystemName { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string CurrentVersion { get; set; } = string.Empty;

    public string LatestVersion { get; set; } = string.Empty;
}
