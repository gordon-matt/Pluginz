using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Pluginz.Packages;

/// <summary>Turns the files of a NuGet package into the files of a plugin folder.</summary>
internal static class PackageLayout
{
    private static readonly FrameworkReducer reducer = new();

    /// <summary>
    /// Copies the package's assets for <paramref name="target"/> into <paramref name="root"/>: the closest
    /// <c>lib/{tfm}</c> folder flattened into the root, <c>runtimes/</c> as it is, and <c>staticwebassets/</c> as
    /// <c>wwwroot/</c>. Returns the file names that were copied from <c>lib</c>.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ExtractAssetsAsync(
        PackageArchiveReader reader,
        NuGetFramework target,
        string root,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var copied = new List<string>();
        var groups = reader.GetLibItems().ToList();
        var nearest = reducer.GetNearest(target, groups.Select(group => group.TargetFramework));

        if (nearest is not null)
        {
            var group = groups.First(g => g.TargetFramework.Equals(nearest));
            foreach (string item in group.Items)
            {
                // lib/{tfm}/Some.dll  ->  Some.dll, and lib/{tfm}/de/Some.resources.dll  ->  de/Some.resources.dll
                string[] parts = item.Split('/');
                if (parts.Length < 3 || parts[^1] == "_._" || item.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = string.Join('/', parts.Skip(2));
                if (await CopyAsync(reader, item, root, relative, overwrite, cancellationToken) && parts.Length == 3)
                {
                    copied.Add(parts[^1]);
                }
            }
        }

        foreach (string file in reader.GetFiles())
        {
            if (file.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                await CopyAsync(reader, file, root, file, overwrite, cancellationToken);
            }
            else if (file.StartsWith("staticwebassets/", StringComparison.OrdinalIgnoreCase))
            {
                await CopyAsync(reader, file, root, $"wwwroot/{file["staticwebassets/".Length..]}", overwrite, cancellationToken);
            }
        }

        return copied;
    }

    /// <summary>
    /// Builds the manifest from the package's <c>pluginz/plugin.json</c> when it has one, and from the nuspec for
    /// whatever it does not say. The package id and version always win for identity.
    /// </summary>
    public static async Task<PluginManifest> ReadManifestAsync(PackageArchiveReader reader, IReadOnlyList<string> primaryAssemblies)
    {
        var identity = reader.GetIdentity();
        var nuspec = reader.NuspecReader;
        var manifest = new PluginManifest();

        string? entry = reader.GetFiles().FirstOrDefault(file =>
            string.Equals(file, PluginNames.PackageManifestPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(file, PluginNames.ManifestFileName, StringComparison.OrdinalIgnoreCase));

        if (entry is not null)
        {
            await using var stream = reader.GetStream(entry);
            manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream, PluginzJson.Options) ?? new PluginManifest();
        }

        if (string.IsNullOrWhiteSpace(manifest.SystemName))
        {
            manifest.SystemName = identity.Id;
        }

        manifest.Version = identity.Version.ToNormalizedString();

        manifest.FriendlyName = Pick(manifest.FriendlyName, nuspec.GetTitle(), identity.Id);
        manifest.Description = Pick(manifest.Description, nuspec.GetDescription());
        manifest.Author = Pick(manifest.Author, nuspec.GetAuthors());
        manifest.ProjectUrl = Pick(manifest.ProjectUrl, nuspec.GetProjectUrl());
        manifest.License = Pick(manifest.License, nuspec.GetLicenseMetadata()?.License);

        if (manifest.Tags is not { Count: > 0 })
        {
            manifest.Tags = [.. (nuspec.GetTags() ?? string.Empty)
                .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Where(tag => !tag.Equals("pluginz", StringComparison.OrdinalIgnoreCase))];
        }

        manifest.Dependencies ??= [];
        manifest.PrivateAssemblies ??= [];

        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            manifest.Assembly = ChooseAssembly(manifest.SystemName, identity.Id, primaryAssemblies);
        }

        return manifest;
    }

    /// <summary>Dependency groups for the framework closest to <paramref name="target"/>.</summary>
    public static IReadOnlyList<PackageDependency> GetDependencies(PackageArchiveReader reader, NuGetFramework target)
    {
        var groups = reader.GetPackageDependencies().ToList();
        var nearest = reducer.GetNearest(target, groups.Select(group => group.TargetFramework));

        return nearest is null
            ? []
            : [.. groups.First(group => group.TargetFramework.Equals(nearest)).Packages];
    }

    private static string? ChooseAssembly(string systemName, string packageId, IReadOnlyList<string> assemblies)
    {
        var dlls = assemblies.Where(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();

        return dlls.FirstOrDefault(name => string.Equals(name, $"{systemName}.dll", StringComparison.OrdinalIgnoreCase))
            ?? dlls.FirstOrDefault(name => string.Equals(name, $"{packageId}.dll", StringComparison.OrdinalIgnoreCase))
            ?? (dlls.Count == 1 ? dlls[0] : null);
    }

    private static string? Pick(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private static async Task<bool> CopyAsync(
        PackageArchiveReader reader,
        string entry,
        string root,
        string relative,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        string rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string destination = Path.GetFullPath(Path.Combine(root, relative));

        // A package is untrusted input. Never write outside the plugin folder.
        if (!destination.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The package contains an unsafe path: {entry}");
        }

        if (!overwrite && File.Exists(destination))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var source = reader.GetStream(entry);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken);
        return true;
    }
}