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

    public string StateFile => Path.Combine(DataRoot, "plugins.json");

    public string ShadowRoot => Path.Combine(DataRoot, "shadow");

    /// <summary>Updates that wait for the next start because the plugin is loaded from its own folder.</summary>
    public string PendingRoot => Path.Combine(DataRoot, "pending");

    /// <summary>Where uploads and downloads are unpacked before they are validated and moved into place.</summary>
    public string StagingRoot => Path.Combine(DataRoot, "staging");

    /// <summary>Replaced folders wait here until they can be deleted.</summary>
    public string TrashRoot => Path.Combine(DataRoot, "trash");

    public static PluginPaths From(PluginzOptions options, string contentRootPath) => new(
        contentRootPath,
        Path.GetFullPath(options.PluginsPath, contentRootPath),
        Path.GetFullPath(options.DataPath, contentRootPath));

    public string PluginDirectory(string systemName) => Path.Combine(PluginsRoot, systemName);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(PluginsRoot);
        Directory.CreateDirectory(DataRoot);
    }
}

/// <summary>Written to <c>.pluginz/origin.json</c> when Pluginz installs a plugin, so it can tell where it came from.</summary>
public sealed class PluginOriginRecord
{
    public PluginOrigin Origin { get; set; }

    public string? PackageId { get; set; }

    public string? Source { get; set; }

    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;

    public static PluginOriginRecord? TryRead(string pluginDirectory)
    {
        var path = Path.Combine(pluginDirectory, PluginNames.MetadataFolderName, "origin.json");
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

    public void Write(string pluginDirectory)
    {
        var folder = Path.Combine(pluginDirectory, PluginNames.MetadataFolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "origin.json"), JsonSerializer.Serialize(this, PluginzJson.Options));
    }
}
