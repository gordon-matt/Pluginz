namespace Pluginz.Models;

/// <summary>
/// The contents of a plugin's <c>plugin.json</c>. Only <see cref="SystemName"/> is required. The MSBuild targets in
/// the Pluginz.Core package write this file for you from project properties.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>The unique key of the plugin. It is also the folder name. Letters, digits, dot, dash and underscore.</summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the friendly name.
    /// </summary>
    public string? FriendlyName { get; set; }

    /// <summary>A free-form category, for grouping in the UI and for filtering.</summary>
    public string? Group { get; set; }

    /// <summary>A SemVer version, for example <c>1.2.0</c> or <c>2.0.0-beta.1</c>.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>The file name of the main assembly. Defaults to <c>{SystemName}.dll</c>.</summary>
    public string? Assembly { get; set; }

    /// <summary>The full name of the <see cref="IPlugin"/> type. Needed only when the assembly has more than one.</summary>
    public string? EntryType { get; set; }

    /// <summary>A NuGet version range that the host version must satisfy, for example <c>[1.0,2.0)</c>.</summary>
    public string? HostVersion { get; set; }

    /// <summary>Other plugins that must be installed and enabled before this one can activate.</summary>
    public List<PluginDependency> Dependencies { get; set; } = [];

    /// <summary>
    /// Assembly names that the plugin must load from its own folder even when the host has them. Use this when the
    /// plugin needs a different version of a library than the host ships.
    /// </summary>
    public List<string> PrivateAssemblies { get; set; } = [];

    /// <summary>When not empty, the plugin is available only to these tenants. Empty means every tenant.</summary>
    public List<string> Tenants { get; set; } = [];

    /// <summary>When not empty, the plugin is available only to users in one of these roles.</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the project URL.
    /// </summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// Gets or sets the license.
    /// </summary>
    public string? License { get; set; }

    /// <summary>The main assembly file name with the default applied.</summary>
    [JsonIgnore]
    public string AssemblyFileName => string.IsNullOrWhiteSpace(Assembly) ? $"{SystemName}.dll" : Assembly;
}

/// <summary>A requirement on another plugin.</summary>
/// <param name="SystemName">The system name of the required plugin.</param>
/// <param name="Version">A NuGet version range, or null for any version.</param>
public sealed record PluginDependency(string SystemName, string? Version = null);