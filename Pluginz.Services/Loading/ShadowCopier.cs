using System.Security.Cryptography;
using System.Text;

namespace Pluginz.Loading;

/// <summary>
/// Copies a plugin folder to a content-addressed shadow folder and loads from there. The original folder is never
/// locked, so an update can replace it while the old version keeps running.
/// </summary>
internal static class ShadowCopier
{
    private const string CompleteMarker = ".shadow-complete";

    /// <summary>Returns the shadow folder for the plugin's current files, creating it when it is missing.</summary>
    public static string Copy(string sourceDirectory, string shadowRoot, string systemName, ILogger? logger = null)
    {
        var parent = Path.Combine(shadowRoot, systemName);
        var target = Path.Combine(parent, Fingerprint(sourceDirectory));

        if (File.Exists(Path.Combine(target, CompleteMarker)))
        {
            return target;
        }

        Directory.CreateDirectory(parent);
        var temp = $"{target}.tmp-{Guid.NewGuid():N}";

        try
        {
            CopyDirectory(sourceDirectory, temp);
            File.WriteAllText(Path.Combine(temp, CompleteMarker), DateTime.UtcNow.ToString("O"));

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(temp, target);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        RemoveStale(parent, target, logger);
        return target;
    }

    private static string Fingerprint(string directory)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(directory, file);
            if (relative.StartsWith(PluginNames.MetadataFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(file);
            builder.Append(relative).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    internal static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void RemoveStale(string parent, string keep, ILogger? logger)
    {
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            if (string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another process may still run from it. It is removed on a later start.
                logger?.LogDebug(ex, "Could not remove old shadow copy {Directory}", directory);
            }
        }
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
