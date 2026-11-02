using System.IO.Compression;
using Pluginz.Discovery;
using Pluginz.Hosting;

namespace Pluginz.Management;

internal sealed class PluginArchiveInstaller(
    PluginRuntime runtime,
    IPluginFolderInstaller folderInstaller,
    ILogger<PluginArchiveInstaller> logger) : IPluginArchiveInstaller
{
    private const int MaxEntries = 10_000;

    public async Task<Result<PluginInfo>> InstallAsync(Stream archive, string? fileName = null, CancellationToken cancellationToken = default)
    {
        if (!runtime.Options.AllowUpload)
        {
            return Result<PluginInfo>.Forbidden("Uploading plugins is turned off.");
        }

        var staging = Path.Combine(runtime.Paths.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var extracted = await ExtractAsync(archive, staging, cancellationToken);
            if (!extracted.IsSuccess)
            {
                return extracted.To<PluginInfo>();
            }

            var root = FindRoot(staging);
            if (root is null)
            {
                return Result<PluginInfo>.Invalid(new ValidationError(
                    "Archive", $"The zip file has no {PluginNames.ManifestFileName}, and no folder with a matching assembly."));
            }

            return await folderInstaller.InstallAsync(
                root,
                new PluginOriginRecord { Origin = PluginOrigin.Archive, Source = fileName },
                cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return Result<PluginInfo>.Invalid(new ValidationError("Archive", $"That is not a valid zip file. {ex.Message}"));
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private async Task<Result> ExtractAsync(Stream archive, string staging, CancellationToken cancellationToken)
    {
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);

        if (zip.Entries.Count > MaxEntries)
        {
            return Result.Invalid(new ValidationError("Archive", "The zip file has too many files."));
        }

        // Plugins are code, so the unpacked size is capped well above the upload size but far below a zip bomb.
        var maxBytes = (long)runtime.Options.MaxUploadMegabytes * 1024 * 1024 * 4;
        var written = 0L;
        var stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Rejected zip entry {Entry}: it points outside the staging folder", entry.FullName);
                return Result.Invalid(new ValidationError("Archive", $"The zip file contains an unsafe path: {entry.FullName}"));
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using var source = entry.Open();
            await using var target = File.Create(destination);

            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > maxBytes)
                {
                    return Result.Invalid(new ValidationError("Archive", "The zip file is too large when unpacked."));
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        return Result.Success();
    }

    /// <summary>The folder that holds the manifest: the staging folder itself, or its single child folder.</summary>
    private static string? FindRoot(string staging)
    {
        if (File.Exists(Path.Combine(staging, PluginNames.ManifestFileName)))
        {
            return staging;
        }

        var children = Directory.GetDirectories(staging);
        if (Directory.GetFiles(staging).Length == 0 && children.Length == 1)
        {
            var child = children[0];
            var name = Path.GetFileName(child);
            if (File.Exists(Path.Combine(child, PluginNames.ManifestFileName)) || File.Exists(Path.Combine(child, $"{name}.dll")))
            {
                return child;
            }
        }

        return null;
    }

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
