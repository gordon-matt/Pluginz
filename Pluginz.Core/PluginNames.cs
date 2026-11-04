using System.Text.RegularExpressions;

namespace Pluginz;

/// <summary>Constants and validation shared by every Pluginz project.</summary>
public static partial class PluginNames
{
    /// <summary>The file name of the manifest inside a plugin folder.</summary>
    public const string ManifestFileName = "plugin.json";

    /// <summary>A hidden folder inside a plugin folder where Pluginz keeps its own records.</summary>
    public const string MetadataFolderName = ".pluginz";

    /// <summary>Where a NuGet package carries its manifest, relative to the package root.</summary>
    public const string PackageManifestPath = "pluginz/plugin.json";

    /// <summary>Whether a value is safe to use as a plugin system name and folder name.</summary>
    public static bool IsValidSystemName(string? value) =>
        !string.IsNullOrEmpty(value)
        && !value.Contains("..", StringComparison.Ordinal)
        && !value.EndsWith('.')
        && SystemNameRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex SystemNameRegex();
}

/// <summary>JSON settings for manifests and the state file.</summary>
public static class PluginzJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>The routes of the Pluginz admin pages.</summary>
public static class PluginzRoutes
{
    public const string Plugins = "/pluginz";

    public const string Browse = "/pluginz/browse";
}