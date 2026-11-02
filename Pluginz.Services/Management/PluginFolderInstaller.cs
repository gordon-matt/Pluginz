using Pluginz.Discovery;
using Pluginz.Hosting;
using Pluginz.Loading;
using Pluginz.Registry;

namespace Pluginz.Management;

internal sealed class PluginFolderInstaller(PluginRuntime runtime, ILogger<PluginFolderInstaller> logger) : IPluginFolderInstaller
{
    public async Task<Result<PluginInfo>> InstallAsync(string sourceDirectory, PluginOriginRecord origin, CancellationToken cancellationToken = default)
    {
        var (manifest, problems) = runtime.Discovery.Inspect(sourceDirectory, Path.GetFileName(sourceDirectory));
        if (manifest is null || problems.Count > 0)
        {
            return Result<PluginInfo>.Invalid(new ValidationError("Plugin", $"This is not a usable plugin. {string.Join(' ', problems)}"));
        }

        var prefixes = runtime.Options.AllowedPrefixes;
        if (prefixes.Count > 0 && !prefixes.Any(prefix => manifest.SystemName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<PluginInfo>.Forbidden($"Plugins named '{manifest.SystemName}' are not allowed. Allowed prefixes: {string.Join(", ", prefixes)}.");
        }

        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            var paths = runtime.Paths;
            var target = paths.PluginDirectory(manifest.SystemName);
            var existing = runtime.RegistryInternal.FindEntry(manifest.SystemName);

            origin.Write(sourceDirectory);

            try
            {
                if (existing is { IsLoaded: true } && !runtime.Options.ShadowCopy)
                {
                    // Its files are in use. Apply the update at the next start, before anything is loaded.
                    var pending = Path.Combine(paths.PendingRoot, manifest.SystemName);
                    Directory.CreateDirectory(paths.PendingRoot);
                    DeleteIfExists(pending);
                    Move(sourceDirectory, pending);
                }
                else if (Directory.Exists(target))
                {
                    var trash = Path.Combine(paths.TrashRoot, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(paths.TrashRoot);
                    Directory.Move(target, trash);

                    try
                    {
                        Move(sourceDirectory, target);
                    }
                    catch
                    {
                        // Put the old version back rather than leave the plugin missing.
                        DeleteIfExists(target);
                        Directory.Move(trash, target);
                        throw;
                    }

                    TryDelete(trash);
                }
                else
                {
                    Move(sourceDirectory, target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not put plugin {SystemName} in place", manifest.SystemName);
                return Result<PluginInfo>.Error($"The plugin folder is in use and could not be replaced. {ex.Message}");
            }

            runtime.RegistryInternal.Refresh();
            runtime.RegistryInternal.Raise(manifest.SystemName, PluginChangeKind.Discovered);
            logger.LogInformation("Plugin {SystemName} {Version} was added from {Origin}", manifest.SystemName, manifest.Version, origin.Origin);

            return Result<PluginInfo>.Success(runtime.RegistryInternal.Find(manifest.SystemName)!);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    private static void Move(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException) when (!Directory.Exists(destination))
        {
            // Moving across volumes is not supported by the OS. Copy and delete instead.
            ShadowCopier.CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            DeleteIfExists(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. The trash folder can be emptied by hand.
        }
    }
}
