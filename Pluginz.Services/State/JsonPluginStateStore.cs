namespace Pluginz.State;

/// <summary>Stores the state in one JSON file. Writes go to a temporary file first, so a crash cannot leave half a file.</summary>
public sealed class JsonPluginStateStore : IPluginStateStore
{
    private readonly string path;
    private readonly Lock gate = new();

    public JsonPluginStateStore(string path)
    {
        this.path = path;
    }

    public string FilePath => path;

    public PluginStateDocument Load()
    {
        lock (gate)
        {
            return Read();
        }
    }

    public void Update(Action<PluginStateDocument> change)
    {
        lock (gate)
        {
            var document = Read();
            change(document);
            Write(document);
        }
    }

    private PluginStateDocument Read()
    {
        if (!File.Exists(path))
        {
            return new PluginStateDocument();
        }

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<PluginStateDocument>(stream, PluginzJson.Options) ?? new PluginStateDocument();

            // JSON loses the comparer, so rebuild the dictionary with the case-insensitive one.
            document.Plugins = new Dictionary<string, PluginState>(document.Plugins, StringComparer.OrdinalIgnoreCase);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The plugin state file '{path}' is not valid JSON. Fix or delete it.", ex);
        }
    }

    private void Write(PluginStateDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, PluginzJson.Options));
        File.Move(temp, path, overwrite: true);
    }
}
