using System.Text.RegularExpressions;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Pluginz.Discovery;
using Pluginz.Hosting;
using Pluginz.Management;

namespace Pluginz.Packages;

internal sealed partial class PluginPackageService : IPluginPackageService
{
    private static readonly string[] metaPackagePrefixes = ["NETStandard.Library", "Microsoft.NETCore.App", "Microsoft.AspNetCore.App", "Microsoft.NETCore.Platforms"];

    private readonly PluginRuntime runtime;
    private readonly IPluginFolderInstaller folderInstaller;
    private readonly PluginzNuGetOptions options;
    private readonly Microsoft.Extensions.Logging.ILogger logger;
    private readonly NuGet.Common.ILogger nugetLogger;
    private readonly NuGetFramework target;
    private readonly List<SourceRepository> repositories;

    public PluginPackageService(
        PluginRuntime runtime,
        IPluginFolderInstaller folderInstaller,
        IOptions<PluginzNuGetOptions> options,
        ILogger<PluginPackageService> logger)
    {
        this.runtime = runtime;
        this.folderInstaller = folderInstaller;
        this.options = options.Value;
        this.logger = logger;

        nugetLogger = new NuGetLogAdapter(logger);
        target = NuGetFramework.Parse(this.options.TargetFramework);
        repositories = [.. this.options.EffectiveSources.Select(CreateRepository)];
    }

    public async Task<Result<IReadOnlyList<PluginPackageInfo>>> SearchAsync(
        string? query,
        bool includePrerelease = false,
        int skip = 0,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var tag = options.TagFilter?.Trim();
        var remoteTerm = string.Join(' ', new[]
        {
            string.IsNullOrEmpty(tag) ? null : $"tags:{tag}",
            query?.Trim(),
        }.Where(part => !string.IsNullOrEmpty(part)));

        var found = new Dictionary<string, PluginPackageInfo>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        async Task<IReadOnlyList<IPackageSearchMetadata>> SearchRepositoryAsync(SourceRepository repository)
        {
            var search = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            if (search is null)
            {
                return [];
            }

            // A server understands the "tags:" field. A folder feed only matches plain text, so it gets the bare query.
            var term = repository.PackageSource.IsLocal ? query?.Trim() ?? string.Empty : remoteTerm;
            var page = await search.SearchAsync(term, new SearchFilter(includePrerelease), skip, take, nugetLogger, cancellationToken);

            return [.. page.Where(metadata => HasTag(metadata, tag) && (includePrerelease || !metadata.Identity.Version.IsPrerelease))];
        }

        // The feeds are searched at the same time, so one slow feed does not hold up the others.
        var searches = repositories.Select(SearchRepositoryAsync).ToList();

        for (var index = 0; index < repositories.Count; index++)
        {
            var source = repositories[index].PackageSource.Name;

            try
            {
                foreach (var metadata in await searches[index])
                {
                    // A folder feed can return one entry per version. Keep the newest.
                    if (!found.TryGetValue(metadata.Identity.Id, out var existing)
                        || metadata.Identity.Version > NuGetVersion.Parse(existing.Version))
                    {
                        found[metadata.Identity.Id] = Map(metadata, source);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Searching {Source} failed", source);
                failures.Add($"{source}: {ex.GetBaseException().Message}");
            }
        }

        if (found.Count == 0 && failures.Count == repositories.Count)
        {
            return Result<IReadOnlyList<PluginPackageInfo>>.Error($"No feed could be searched. {string.Join(' ', failures)}");
        }

        var items = found.Values
            .OrderByDescending(package => package.DownloadCount ?? 0)
            .ThenBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in items)
        {
            var installed = FindInstalled(item.PackageId);
            item.InstalledSystemName = installed?.SystemName;
            item.InstalledVersion = installed?.Manifest.Version;
        }

        return Result<IReadOnlyList<PluginPackageInfo>>.Success(items);
    }

    public async Task<Result<IReadOnlyList<string>>> GetVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPackageId(packageId))
        {
            return Result<IReadOnlyList<string>>.Invalid(new ValidationError(nameof(packageId), "That is not a valid package id."));
        }

        using var cache = new SourceCacheContext();
        var versions = await FindVersionsAsync(packageId, cache, cancellationToken);

        return Result<IReadOnlyList<string>>.Success([.. versions
            .Where(version => includePrerelease || !version.IsPrerelease)
            .OrderDescending()
            .Select(version => version.ToNormalizedString())]);
    }

    public async Task<Result<PluginInfo>> InstallAsync(
        string packageId,
        string? version = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPackageId(packageId))
        {
            return Result<PluginInfo>.Invalid(new ValidationError(nameof(packageId), "That is not a valid package id."));
        }

        NuGetVersion? wanted = null;
        if (!string.IsNullOrWhiteSpace(version) && !NuGetVersion.TryParse(version, out wanted))
        {
            return Result<PluginInfo>.Invalid(new ValidationError(nameof(version), "That is not a valid version."));
        }

        using var cache = new SourceCacheContext();
        var staging = Path.Combine(runtime.Paths.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            if (wanted is null)
            {
                wanted = (await FindVersionsAsync(packageId, cache, cancellationToken))
                    .Where(v => includePrerelease || options.IncludePrerelease || !v.IsPrerelease)
                    .OrderDescending()
                    .FirstOrDefault();

                if (wanted is null)
                {
                    return Result<PluginInfo>.NotFound($"Package '{packageId}' was not found on the configured feeds.");
                }
            }

            var downloaded = await DownloadAsync(packageId, wanted, Path.Combine(staging, "package.nupkg"), cache, cancellationToken);
            if (downloaded is null)
            {
                return Result<PluginInfo>.NotFound($"Package '{packageId}' {wanted.ToNormalizedString()} was not found on the configured feeds.");
            }

            return await InstallPackageAsync(Path.Combine(staging, "package.nupkg"), downloaded, staging, cache, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex, packageId);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    public async Task<Result<PluginInfo>> InstallFromFileAsync(Stream package, string? fileName = null, CancellationToken cancellationToken = default)
    {
        if (!runtime.Options.AllowUpload)
        {
            return Result<PluginInfo>.Forbidden("Uploading plugins is turned off.");
        }

        using var cache = new SourceCacheContext();
        var staging = Path.Combine(runtime.Paths.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var path = Path.Combine(staging, "package.nupkg");
            await using (var file = File.Create(path))
            {
                await package.CopyToAsync(file, cancellationToken);
            }

            return await InstallPackageAsync(path, fileName ?? "uploaded package", staging, cache, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex, fileName ?? "the package");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    public async Task<Result<IReadOnlyList<PluginUpdateInfo>>> GetUpdatesAsync(bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        var updates = new List<PluginUpdateInfo>();
        using var cache = new SourceCacheContext();

        var packaged = runtime.RegistryInternal.Read(entries => entries
            .Where(entry => entry.OriginRecord is { Origin: PluginOrigin.NuGet, PackageId: not null })
            .Select(entry => (entry.SystemName, PackageId: entry.OriginRecord!.PackageId!, entry.Manifest.Version))
            .ToList());

        foreach (var (systemName, packageId, current) in packaged)
        {
            if (!NuGetVersion.TryParse(current, out var installed))
            {
                continue;
            }

            var latest = (await FindVersionsAsync(packageId, cache, cancellationToken))
                .Where(version => includePrerelease || options.IncludePrerelease || installed.IsPrerelease || !version.IsPrerelease)
                .OrderDescending()
                .FirstOrDefault();

            if (latest is not null && latest > installed)
            {
                updates.Add(new PluginUpdateInfo
                {
                    SystemName = systemName,
                    PackageId = packageId,
                    CurrentVersion = installed.ToNormalizedString(),
                    LatestVersion = latest.ToNormalizedString(),
                });
            }
        }

        return Result<IReadOnlyList<PluginUpdateInfo>>.Success(updates);
    }

    private async Task<Result<PluginInfo>> InstallPackageAsync(
        string packagePath,
        string sourceName,
        string staging,
        SourceCacheContext cache,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(packagePath).Length > (long)options.MaxPackageMegabytes * 1024 * 1024)
        {
            return Result<PluginInfo>.Invalid(new ValidationError("Package", $"The package is larger than {options.MaxPackageMegabytes} MB."));
        }

        var pluginRoot = Path.Combine(staging, "plugin");
        Directory.CreateDirectory(pluginRoot);

        await using var stream = File.OpenRead(packagePath);
        using var reader = new PackageArchiveReader(stream);

        PackageIdentity identity;
        try
        {
            identity = reader.GetIdentity();
        }
        catch (Exception ex) when (ex is PackagingException or InvalidDataException)
        {
            return Result<PluginInfo>.Invalid(new ValidationError("Package", $"That is not a valid NuGet package. {ex.Message}"));
        }

        var assemblies = await PackageLayout.ExtractAssetsAsync(reader, target, pluginRoot, overwrite: true, cancellationToken);
        var manifest = await PackageLayout.ReadManifestAsync(reader, assemblies);

        if (string.IsNullOrWhiteSpace(manifest.Assembly) || !File.Exists(Path.Combine(pluginRoot, manifest.Assembly)))
        {
            return Result<PluginInfo>.Invalid(new ValidationError(
                "Package",
                assemblies.Count == 0
                    ? $"'{identity.Id}' has no assemblies for {options.TargetFramework}, so it is not a plugin for this host."
                    : $"The package has {assemblies.Count} assemblies. Add pluginz/plugin.json to the package and set \"assembly\"."));
        }

        var resolved = await AddDependenciesAsync(reader, manifest, pluginRoot, cache, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<PluginInfo>();
        }

        await File.WriteAllTextAsync(
            Path.Combine(pluginRoot, PluginNames.ManifestFileName),
            JsonSerializer.Serialize(manifest, PluginzJson.Options),
            cancellationToken);

        return await folderInstaller.InstallAsync(
            pluginRoot,
            new PluginOriginRecord { Origin = PluginOrigin.NuGet, PackageId = identity.Id, Source = sourceName },
            cancellationToken);
    }

    /// <summary>
    /// Copies the packages the plugin needs and the host does not provide into the plugin folder. A plugin that came
    /// from NuGet has no <c>.deps.json</c>, so the loader finds these by probing the folder.
    /// </summary>
    private async Task<Result> AddDependenciesAsync(
        PackageArchiveReader root,
        PluginManifest manifest,
        string pluginRoot,
        SourceCacheContext cache,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.GetIdentity().Id };
        var queue = new Queue<PackageDependency>(PackageLayout.GetDependencies(root, target));
        var downloaded = 0;

        while (queue.TryDequeue(out var dependency))
        {
            if (!seen.Add(dependency.Id) || metaPackagePrefixes.Any(prefix => dependency.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // A dependency on another plugin becomes a plugin dependency, not a copied library.
            var plugin = FindInstalled(dependency.Id);
            if (plugin is not null)
            {
                if (!manifest.Dependencies.Any(d => string.Equals(d.SystemName, plugin.SystemName, StringComparison.OrdinalIgnoreCase)))
                {
                    manifest.Dependencies.Add(new PluginDependency(plugin.SystemName, dependency.VersionRange?.OriginalString));
                }

                continue;
            }

            if (runtime.Rules.IsProvidedByHost(dependency.Id, manifest.PrivateAssemblies))
            {
                logger.LogDebug("Dependency {Package} is provided by the host", dependency.Id);
                continue;
            }

            if (++downloaded > options.MaxDependencyPackages)
            {
                return Result.Error($"The plugin needs more than {options.MaxDependencyPackages} dependency packages. Stopped.");
            }

            var range = dependency.VersionRange ?? VersionRange.All;
            var versions = await FindVersionsAsync(dependency.Id, cache, cancellationToken);
            var best = range.FindBestMatch(versions);

            if (best is null)
            {
                return Result.Error($"The dependency '{dependency.Id}' {range.OriginalString} was not found on the configured feeds.");
            }

            var path = Path.Combine(Path.GetDirectoryName(pluginRoot)!, $"dep-{Guid.NewGuid():N}.nupkg");
            if (await DownloadAsync(dependency.Id, best, path, cache, cancellationToken) is null)
            {
                return Result.Error($"The dependency '{dependency.Id}' {best.ToNormalizedString()} could not be downloaded.");
            }

            await using var stream = File.OpenRead(path);
            using var reader = new PackageArchiveReader(stream);

            // The first copy of a file wins, which matches the nearest-wins rule of NuGet restore.
            await PackageLayout.ExtractAssetsAsync(reader, target, pluginRoot, overwrite: false, cancellationToken);

            foreach (var next in PackageLayout.GetDependencies(reader, target))
            {
                queue.Enqueue(next);
            }
        }

        return Result.Success();
    }

    private async Task<IReadOnlyList<NuGetVersion>> FindVersionsAsync(string id, SourceCacheContext cache, CancellationToken cancellationToken)
    {
        var versions = new HashSet<NuGetVersion>();

        foreach (var repository in repositories)
        {
            try
            {
                var find = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                if (find is not null)
                {
                    versions.UnionWith(await find.GetAllVersionsAsync(id, cache, nugetLogger, cancellationToken));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Listing versions of {Package} on {Source} failed", id, repository.PackageSource.Name);
            }
        }

        return [.. versions];
    }

    /// <summary>Downloads a package from the first feed that has it. Returns the feed's name, or null.</summary>
    private async Task<string?> DownloadAsync(string id, NuGetVersion version, string path, SourceCacheContext cache, CancellationToken cancellationToken)
    {
        foreach (var repository in repositories)
        {
            try
            {
                var find = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                if (find is null)
                {
                    continue;
                }

                await using var file = File.Create(path);
                if (await find.CopyNupkgToStreamAsync(id, version, file, cache, nugetLogger, cancellationToken))
                {
                    return repository.PackageSource.Name;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Downloading {Package} {Version} from {Source} failed", id, version, repository.PackageSource.Name);
            }
        }

        return null;
    }

    // A plugin that came from a package remembers the package id. One that was added some other way is matched by the
    // convention that a plugin's package id is its system name.
    private PluginEntry? FindInstalled(string packageId) => runtime.RegistryInternal.Read(entries =>
        entries.FirstOrDefault(entry => string.Equals(entry.OriginRecord?.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
        ?? entries.FirstOrDefault(entry => string.Equals(entry.SystemName, packageId, StringComparison.OrdinalIgnoreCase)));

    private SourceRepository CreateRepository(PluginPackageSource source)
    {
        // A feed is a URL or a folder. A relative folder is relative to the content root, not to wherever the process started.
        var location = Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file"
            ? source.Url
            : Path.GetFullPath(source.Url, runtime.Paths.ContentRoot);

        var packageSource = new PackageSource(location, string.IsNullOrWhiteSpace(source.Name) ? source.Url : source.Name);

        if (!string.IsNullOrWhiteSpace(source.Username))
        {
            packageSource.Credentials = new PackageSourceCredential(
                packageSource.Name,
                source.Username,
                source.Password ?? string.Empty,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);
        }

        return Repository.Factory.GetCoreV3(packageSource);
    }

    private static bool HasTag(IPackageSearchMetadata metadata, string? tag) =>
        string.IsNullOrEmpty(tag)
        || (metadata.Tags ?? string.Empty)
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Contains(tag, StringComparer.OrdinalIgnoreCase);

    private static PluginPackageInfo Map(IPackageSearchMetadata metadata, string source) => new()
    {
        PackageId = metadata.Identity.Id,
        Version = metadata.Identity.Version.ToNormalizedString(),
        Title = metadata.Title,
        Authors = metadata.Authors,
        Description = metadata.Description,
        Tags = metadata.Tags,
        IconUrl = metadata.IconUrl?.ToString(),
        ProjectUrl = metadata.ProjectUrl?.ToString(),
        DownloadCount = metadata.DownloadCount,
        IsPrerelease = metadata.Identity.Version.IsPrerelease,
        Source = source,
    };

    private Result<PluginInfo> Failed(Exception ex, string what)
    {
        logger.LogError(ex, "Installing {Package} failed", what);

        return ex switch
        {
            InvalidDataException => Result<PluginInfo>.Invalid(new ValidationError("Package", ex.Message)),
            _ => Result<PluginInfo>.Error($"Installing '{what}' failed. {ex.GetBaseException().Message}"),
        };
    }

    private static bool IsValidPackageId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 100 && PackageIdRegex().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex PackageIdRegex();

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
