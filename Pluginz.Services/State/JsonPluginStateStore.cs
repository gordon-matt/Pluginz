namespace Pluginz.State;

/// <summary>Stores the state in one JSON file. Writes go to a temporary file first, so a crash cannot leave half a file.</summary>
public sealed class JsonPluginStateStore : IPluginStateStore
{
    private readonly Lock gate = new();

    /// <summary>
    /// Initializes a new instance of the JsonPluginStateStore class.
    /// </summary>
    /// <param name="path">The path.</param>
    public JsonPluginStateStore(string path)
    {
        FilePath = path;
    }

    /// <summary>
    /// Gets the file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Loads the value.
    /// </summary>
    /// <returns>The plugin state document.</returns>
    public PluginStateDocument Load()
    {
        lock (gate)
        {
            return Read();
        }
    }

    /// <summary>
    /// Updates the value.
    /// </summary>
    /// <param name="change">The change.</param>
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
        if (!File.Exists(FilePath))
        {
            return new PluginStateDocument();
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            var document = JsonSerializer.Deserialize<PluginStateDocument>(stream, PluginzJson.Options) ?? new PluginStateDocument();

            // JSON loses the comparer, so rebuild the dictionary with the case-insensitive one.
            document.Plugins = new Dictionary<string, PluginState>(document.Plugins, StringComparer.OrdinalIgnoreCase);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The plugin state file '{FilePath}' is not valid JSON. Fix or delete it.", ex);
        }
    }

    private void Write(PluginStateDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        string temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, PluginzJson.Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}