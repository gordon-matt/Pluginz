namespace Pluginz.Models;

/// <summary>Where a plugin came from.</summary>
public enum PluginOrigin
{
    /// <summary>A folder that someone copied into the plugins folder.</summary>
    Folder,

    /// <summary>Extracted from a zip file that was uploaded.</summary>
    Archive,

    /// <summary>Installed from a NuGet package.</summary>
    NuGet,
}

/// <summary>The state of a plugin in this process.</summary>
public enum PluginStatus
{
    /// <summary>Found on disk but not installed. Nothing of it is loaded.</summary>
    NotInstalled,

    /// <summary>Installed, but turned off.</summary>
    Disabled,

    /// <summary>Installed, enabled, loaded and running.</summary>
    Active,

    /// <summary>Installed and enabled, but loaded only after the host restarts.</summary>
    PendingActivation,

    /// <summary>Still loaded, but it was disabled or uninstalled. It stops after the host restarts.</summary>
    PendingDeactivation,

    /// <summary>Installed and enabled, but it could not be loaded. See <see cref="PluginInfo.Problems"/>.</summary>
    Failed,

    /// <summary>The manifest is invalid or the plugin does not fit this host. See <see cref="PluginInfo.Problems"/>.</summary>
    Incompatible,
}

/// <summary>A read-only snapshot of a plugin. Safe to cache and to pass to UI code.</summary>
public sealed record PluginInfo
{
    /// <summary>
    /// Gets or sets the system name.
    /// </summary>
    public required string SystemName { get; init; }

    /// <summary>
    /// Gets or sets the friendly name.
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Gets or sets the group.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>The version that is loaded in this process, when it differs from <see cref="Version"/>.</summary>
    public string? LoadedVersion { get; init; }

    /// <summary>A newer version that is staged on disk and replaces <see cref="Version"/> at the next start.</summary>
    public string? PendingVersion { get; init; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int DisplayOrder { get; init; }

    /// <summary>
    /// Gets or sets the directory path.
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Gets or sets the assembly file name.
    /// </summary>
    public required string AssemblyFileName { get; init; }

    /// <summary>
    /// Gets or sets the origin.
    /// </summary>
    public PluginOrigin Origin { get; init; }

    /// <summary>The NuGet package id, for plugins installed from a package.</summary>
    public string? PackageId { get; init; }

    /// <summary>The feed or file that the plugin came from.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is installed.
    /// </summary>
    public bool IsInstalled { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public PluginStatus Status { get; init; }

    /// <summary>True when the host must restart for the plugin's current state to take effect.</summary>
    public bool RestartRequired { get; init; }

    /// <summary>
    /// Gets or sets the problems.
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>
    /// Gets or sets the dependencies.
    /// </summary>
    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];

    /// <summary>
    /// Gets or sets the tenants.
    /// </summary>
    public IReadOnlyList<string> Tenants { get; init; } = [];

    /// <summary>
    /// Gets or sets the roles.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string? IconUrl { get; init; }

    /// <summary>
    /// Gets or sets the project URL.
    /// </summary>
    public string? ProjectUrl { get; init; }

    /// <summary>
    /// Gets or sets the license.
    /// </summary>
    public string? License { get; init; }

    /// <summary>The settings page URL reported by the plugin, when it is loaded and has one.</summary>
    public string? ConfigurationUrl { get; init; }

    /// <summary>True while the plugin is loaded and running.</summary>
    public bool IsActive => Status is PluginStatus.Active or PluginStatus.PendingDeactivation;
}