namespace Pluginz.Discovery;

/// <summary>Every folder and file that Pluginz uses, resolved to absolute paths once.</summary>
public sealed class PluginPaths
{
    private PluginPaths(string contentRoot, string pluginsRoot, string dataRoot)
    {
        ContentRoot = contentRoot;
        PluginsRoot = pluginsRoot;
        DataRoot = dataRoot;
    }

    /// <summary>The host's content root, which relative paths in settings are resolved against.</summary>
    public string ContentRoot { get; }

    /// <summary>One sub-folder per plugin.</summary>
    public string PluginsRoot { get; }

    /// <summary>The root for everything Pluginz writes besides the plugins themselves.</summary>
    public string DataRoot { get; }

    /// <summary>
    /// Gets the state file.
    /// </summary>
    public string StateFile => Path.Combine(DataRoot, "plugins.json");

    /// <summary>
    /// Gets the shadow root.
    /// </summary>
    public string ShadowRoot => Path.Combine(DataRoot, "shadow");

    /// <summary>Updates that wait for the next start because the plugin is loaded from its own folder.</summary>
    public string PendingRoot => Path.Combine(DataRoot, "pending");

    /// <summary>Where uploads and downloads are unpacked before they are validated and moved into place.</summary>
    public string StagingRoot => Path.Combine(DataRoot, "staging");

    /// <summary>Replaced folders wait here until they can be deleted.</summary>
    public string TrashRoot => Path.Combine(DataRoot, "trash");

    /// <summary>
    /// Creates a value from the value.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="contentRootPath">The content root path.</param>
    /// <returns>The plugin paths.</returns>
    public static PluginPaths From(PluginzOptions options, string contentRootPath) => new(
        contentRootPath,
        Path.GetFullPath(options.PluginsPath, contentRootPath),
        Path.GetFullPath(options.DataPath, contentRootPath));

    /// <summary>
    /// Gets the plugin directory.
    /// </summary>
    /// <param name="systemName">The system name.</param>
    /// <returns>The plugin directory.</returns>
    public string PluginDirectory(string systemName) => Path.Combine(PluginsRoot, systemName);

    /// <summary>
    /// Ensures the created.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(PluginsRoot);
        Directory.CreateDirectory(DataRoot);
    }
}

/// <summary>Written to <c>.pluginz/origin.json</c> when Pluginz installs a plugin, so it can tell where it came from.</summary>
public sealed class PluginOriginRecord
{
    /// <summary>
    /// Gets or sets the origin.
    /// </summary>
    public PluginOrigin Origin { get; set; }

    /// <summary>
    /// Gets or sets the package identifier.
    /// </summary>
    public string? PackageId { get; set; }

    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the installed utc.
    /// </summary>
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Attempts to read.
    /// </summary>
    /// <param name="pluginDirectory">The plugin directory.</param>
    /// <returns>The read.</returns>
    public static PluginOriginRecord? TryRead(string pluginDirectory)
    {
        string path = Path.Combine(pluginDirectory, PluginNames.MetadataFolderName, "origin.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PluginOriginRecord>(File.ReadAllText(path), PluginzJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the value.
    /// </summary>
    /// <param name="pluginDirectory">The plugin directory.</param>
    public void Write(string pluginDirectory)
    {
        string folder = Path.Combine(pluginDirectory, PluginNames.MetadataFolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "origin.json"), JsonSerializer.Serialize(this, PluginzJson.Options));
    }
}