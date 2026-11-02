using System.Diagnostics;
using NuGet.Versioning;

namespace Pluginz.Discovery;

/// <summary>Finds plugin folders and reads their manifests. It never loads a plugin assembly.</summary>
internal sealed class PluginDiscovery
{
    private readonly PluginPaths paths;
    private readonly PluginzOptions options;
    private readonly Action<LogLevel, string, Exception?> log;

    public PluginDiscovery(PluginPaths paths, PluginzOptions options, Action<LogLevel, string, Exception?> log)
    {
        this.paths = paths;
        this.options = options;
        this.log = log;
    }

    /// <summary>Moves staged updates into place. Call it before <see cref="Scan"/> and before anything is loaded.</summary>
    public void ApplyPending()
    {
        if (!Directory.Exists(paths.PendingRoot))
        {
            return;
        }

        foreach (var staged in Directory.EnumerateDirectories(paths.PendingRoot))
        {
            var name = Path.GetFileName(staged);
            var target = paths.PluginDirectory(name);

            try
            {
                if (Directory.Exists(target))
                {
                    var trash = Path.Combine(paths.TrashRoot, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(paths.TrashRoot);
                    Directory.Move(target, trash);
                    TryDelete(trash);
                }

                Directory.Move(staged, target);
                log(LogLevel.Information, $"Applied the staged update of plugin '{name}'.", null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log(LogLevel.Error, $"Could not apply the staged update of plugin '{name}'. It stays staged.", ex);
            }
        }
    }

    /// <summary>Reads every plugin folder. A folder with a bad manifest still yields an entry, flagged with a problem.</summary>
    public List<PluginEntry> Scan()
    {
        var entries = new List<PluginEntry>();
        if (!Directory.Exists(paths.PluginsRoot))
        {
            return entries;
        }

        var hostVersion = ResolveHostVersion();

        foreach (var directory in Directory.EnumerateDirectories(paths.PluginsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(directory);
            if (folderName.StartsWith('.'))
            {
                continue;
            }

            entries.Add(ReadEntry(directory, folderName, hostVersion));
        }

        return entries;
    }

    /// <summary>The versions of updates that are staged and apply at the next start, by system name.</summary>
    public Dictionary<string, string> ReadPendingVersions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(paths.PendingRoot))
        {
            return result;
        }

        foreach (var staged in Directory.EnumerateDirectories(paths.PendingRoot))
        {
            var manifest = ReadManifest(staged, Path.GetFileName(staged), out _);
            result[Path.GetFileName(staged)] = manifest?.Version ?? "?";
        }

        return result;
    }

    /// <summary>Reads and checks a manifest outside the plugins folder, for example in an unpacked upload.</summary>
    public (PluginManifest? Manifest, List<string> Problems) Inspect(string directory, string expectedName)
    {
        var manifest = ReadManifest(directory, expectedName, out var error);
        var problems = new List<string>();
        if (error is not null)
        {
            problems.Add(error);
        }

        if (manifest is not null)
        {
            problems.AddRange(Validate(manifest, directory, ResolveHostVersion(), requireFolderMatch: false));
        }

        return (manifest, problems);
    }

    private PluginEntry ReadEntry(string directory, string folderName, NuGetVersion? hostVersion)
    {
        var manifest = ReadManifest(directory, folderName, out var error);
        var problems = new List<string>();

        if (error is not null)
        {
            problems.Add(error);
        }

        // Keep the folder visible in the admin even when it cannot be used.
        manifest ??= new PluginManifest { SystemName = folderName, FriendlyName = folderName };

        if (error is null)
        {
            problems.AddRange(Validate(manifest, directory, hostVersion, requireFolderMatch: true));
        }

        return new PluginEntry
        {
            Manifest = manifest,
            Directory = directory,
            Problems = problems,
            OriginRecord = PluginOriginRecord.TryRead(directory),
        };
    }

    private static PluginManifest? ReadManifest(string directory, string folderName, out string? error)
    {
        error = null;
        var manifestPath = Path.Combine(directory, PluginNames.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            // A folder with a single {folder}.dll is the simplest possible plugin. Synthesize a manifest.
            var dll = Path.Combine(directory, $"{folderName}.dll");
            if (PluginNames.IsValidSystemName(folderName) && File.Exists(dll))
            {
                var version = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "1.0.0";
                return new PluginManifest { SystemName = folderName, FriendlyName = folderName, Version = version };
            }

            error = $"The folder has no {PluginNames.ManifestFileName}.";
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), PluginzJson.Options);
            if (manifest is null)
            {
                error = $"{PluginNames.ManifestFileName} is empty.";
                return null;
            }

            Normalize(manifest);
            return manifest;
        }
        catch (JsonException ex)
        {
            error = $"{PluginNames.ManifestFileName} is not valid: {ex.Message}";
            return null;
        }
        catch (IOException ex)
        {
            error = $"{PluginNames.ManifestFileName} cannot be read: {ex.Message}";
            return null;
        }
    }

    // MSBuild writes empty strings for unset properties, and hand-written files may have blanks.
    private static void Normalize(PluginManifest manifest)
    {
        manifest.SystemName = manifest.SystemName?.Trim() ?? string.Empty;
        manifest.FriendlyName = NullIfBlank(manifest.FriendlyName);
        manifest.Group = NullIfBlank(manifest.Group);
        manifest.Author = NullIfBlank(manifest.Author);
        manifest.Description = NullIfBlank(manifest.Description);
        manifest.Assembly = NullIfBlank(manifest.Assembly);
        manifest.EntryType = NullIfBlank(manifest.EntryType);
        manifest.HostVersion = NullIfBlank(manifest.HostVersion);
        manifest.IconUrl = NullIfBlank(manifest.IconUrl);
        manifest.ProjectUrl = NullIfBlank(manifest.ProjectUrl);
        manifest.License = NullIfBlank(manifest.License);
        manifest.Version = NullIfBlank(manifest.Version) ?? "1.0.0";
        manifest.Dependencies ??= [];
        manifest.PrivateAssemblies ??= [];
        manifest.Tenants ??= [];
        manifest.Roles ??= [];
        manifest.Tags ??= [];
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private List<string> Validate(PluginManifest manifest, string directory, NuGetVersion? hostVersion, bool requireFolderMatch)
    {
        var problems = new List<string>();

        if (!PluginNames.IsValidSystemName(manifest.SystemName))
        {
            problems.Add("systemName must start with a letter or digit and use only letters, digits, '.', '-' and '_'.");
        }
        else if (requireFolderMatch && !string.Equals(manifest.SystemName, Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"The folder name '{Path.GetFileName(directory)}' must match systemName '{manifest.SystemName}'.");
        }

        if (!NuGetVersion.TryParse(manifest.Version, out _))
        {
            problems.Add($"version '{manifest.Version}' is not a valid SemVer version.");
        }

        var assembly = manifest.AssemblyFileName;
        if (assembly.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(assembly) || assembly.Contains('/') || assembly.Contains('\\'))
        {
            problems.Add("assembly must be a plain file name in the plugin folder.");
        }
        else if (!File.Exists(Path.Combine(directory, assembly)))
        {
            problems.Add($"The assembly '{assembly}' is missing.");
        }

        if (manifest.HostVersion is not null)
        {
            if (!VersionRange.TryParse(manifest.HostVersion, out var range))
            {
                problems.Add($"hostVersion '{manifest.HostVersion}' is not a valid version range.");
            }
            else if (hostVersion is not null && !range.Satisfies(hostVersion))
            {
                problems.Add($"The plugin needs host version {manifest.HostVersion}, but this host is {hostVersion}.");
            }
        }

        foreach (var dependency in manifest.Dependencies)
        {
            if (!PluginNames.IsValidSystemName(dependency.SystemName))
            {
                problems.Add($"Dependency '{dependency.SystemName}' is not a valid system name.");
            }
            else if (dependency.Version is not null && !VersionRange.TryParse(dependency.Version, out _))
            {
                problems.Add($"Dependency '{dependency.SystemName}' has an invalid version range '{dependency.Version}'.");
            }
        }

        return problems;
    }

    private NuGetVersion? ResolveHostVersion()
    {
        var text = options.HostVersion;
        if (string.IsNullOrWhiteSpace(text))
        {
            var entry = Assembly.GetEntryAssembly();
            text = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? entry?.GetName().Version?.ToString();
        }

        return NuGetVersion.TryParse(text, out var version) ? version : null;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. The trash folder can be emptied by hand.
        }
    }
}
