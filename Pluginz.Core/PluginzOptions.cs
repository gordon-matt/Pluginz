namespace Pluginz;

/// <summary>
/// Settings for Pluginz. Bind them from the <c>Pluginz</c> configuration section or set them in the
/// <c>AddPluginz</c> callback.
/// </summary>
public sealed class PluginzOptions
{
    public const string SectionName = "Pluginz";

    /// <summary>The folder that holds one sub-folder per plugin. A relative path is relative to the content root.</summary>
    public string PluginsPath { get; set; } = "Plugins";

    /// <summary>
    /// The folder for everything Pluginz writes: the state file, shadow copies, staged updates and each plugin's
    /// data folder. A relative path is relative to the content root.
    /// </summary>
    public string DataPath { get; set; } = "App_Data/Pluginz";

    /// <summary>
    /// Load each plugin from a copy under <see cref="DataPath"/> so that its files on disk stay unlocked and can be
    /// replaced while the host runs. Turn it off only if you have a reason to load from the plugin folder itself.
    /// </summary>
    public bool ShadowCopy { get; set; } = true;

    /// <summary>
    /// The version of the host, which plugin manifests are checked against. When empty, the version of the entry
    /// assembly is used.
    /// </summary>
    public string? HostVersion { get; set; }

    /// <summary>When true, a plugin that fails to load stops the host from starting. When false, it is marked as failed.</summary>
    public bool ThrowOnLoadFailure { get; set; }

    /// <summary>How plugin assemblies are separated from the host's.</summary>
    public PluginIsolationOptions Isolation { get; set; } = new();

    /// <summary>
    /// Allow-list of system name prefixes for plugins that arrive by upload or from NuGet. Empty allows any name.
    /// Plugins that are already in the plugins folder are not checked.
    /// </summary>
    public List<string> AllowedPrefixes { get; set; } = [];

    /// <summary>Show upload in the admin and accept uploaded archives. Uploaded code runs inside the host.</summary>
    public bool AllowUpload { get; set; } = true;

    /// <summary>The largest upload the admin accepts, in megabytes.</summary>
    public int MaxUploadMegabytes { get; set; } = 200;

    /// <summary>
    /// Show a Restart button in the admin. It stops the host process, so turn it on only when something starts the
    /// host again: IIS, systemd, Docker, a Windows service or a process supervisor.
    /// </summary>
    public bool AllowRestart { get; set; }

    /// <summary>When true, the admin pages and endpoints reject anonymous callers.</summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>An authorization policy that a caller needs to view plugins. Null means any authenticated caller.</summary>
    public string? ReadPolicy { get; set; }

    /// <summary>An authorization policy that a caller needs to change plugins. Null falls back to <see cref="ReadPolicy"/>.</summary>
    public string? ManagePolicy { get; set; }
}

/// <summary>Which assemblies a plugin shares with the host.</summary>
public sealed class PluginIsolationOptions
{
    /// <summary>How plugin assemblies relate to the host's. See <see cref="PluginIsolationMode"/>.</summary>
    public PluginIsolationMode Mode { get; set; } = PluginIsolationMode.PreferHost;

    /// <summary>
    /// Assembly name prefixes that are always taken from the host. The defaults keep framework types the same
    /// across the boundary. The Pluginz assemblies themselves are always shared, whatever this list says.
    /// </summary>
    public List<string> SharedPrefixes { get; set; } = ["System.", "Microsoft.", "netstandard", "mscorlib", "WindowsBase"];

    /// <summary>Exact assembly names that are always taken from the host.</summary>
    public List<string> SharedAssemblies { get; set; } = [];
}

/// <summary>How a plugin's dependencies are resolved against the host's.</summary>
public enum PluginIsolationMode
{
    /// <summary>
    /// Use the host's copy of any assembly the host already has, and the plugin's copy of the rest. This keeps types
    /// such as UI and data-access libraries identical on both sides. A plugin can opt out for individual assemblies with
    /// <see cref="Models.PluginManifest.PrivateAssemblies"/>.
    /// </summary>
    PreferHost,

    /// <summary>
    /// Share only the framework, the Pluginz contracts and what <see cref="PluginIsolationOptions.SharedPrefixes"/> and
    /// <see cref="PluginIsolationOptions.SharedAssemblies"/> name. Everything else comes from the plugin's own folder
    /// even when the host has it. Plugins can then use any version of a library, but types must cross the boundary
    /// through the shared contracts.
    /// </summary>
    Isolated,
}
