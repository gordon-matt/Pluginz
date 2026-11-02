using NuGet.Versioning;
using Pluginz.Discovery;
using Pluginz.Hosting;
using Pluginz.Registry;

namespace Pluginz.Management;

internal sealed class PluginManager(PluginRuntime runtime, IServiceProvider services, ILogger<PluginManager> logger) : IPluginManager
{
    private const int MaxNameLength = 200;

    private PluginRegistry Registry => runtime.RegistryInternal;

    public Task<Result<PluginInfo>> InstallAsync(string systemName, CancellationToken cancellationToken = default) =>
        RunAsync(systemName, async entry =>
        {
            if (entry.Problems.Count > 0)
            {
                return Invalid(systemName, $"It cannot be installed. {string.Join(' ', entry.Problems)}");
            }

            if (entry.State.Installed)
            {
                return Result<PluginInfo>.Conflict($"'{entry.FriendlyName}' is already installed.");
            }

            var missing = FindUnmetDependencies(entry);
            if (missing is not null)
            {
                return Invalid(systemName, missing);
            }

            try
            {
                await runtime.RunHookAsync(entry, PluginHook.Install, services, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The install step of plugin {SystemName} failed", systemName);
                return Result<PluginInfo>.Error($"The install step of '{entry.FriendlyName}' failed: {ex.Message}");
            }

            runtime.StateStore.Update(document =>
            {
                var state = document.GetOrAdd(entry.SystemName);
                state.Installed = true;
                state.Enabled = true;
                state.InstalledVersion = entry.Manifest.Version;
                state.InstalledUtc = DateTime.UtcNow;
            });

            return Finish(entry, PluginChangeKind.Installed);
        },
        cancellationToken);

    public Task<Result<PluginInfo>> UninstallAsync(string systemName, CancellationToken cancellationToken = default) =>
        RunAsync(systemName, async entry =>
        {
            if (!entry.State.Installed)
            {
                return Result<PluginInfo>.Conflict($"'{entry.FriendlyName}' is not installed.");
            }

            var dependents = FindDependents(entry, installedOnly: true);
            if (dependents.Count > 0)
            {
                return Invalid(systemName, $"Uninstall {string.Join(", ", dependents)} first. {(dependents.Count == 1 ? "It depends" : "They depend")} on '{entry.FriendlyName}'.");
            }

            try
            {
                await runtime.RunHookAsync(entry, PluginHook.Uninstall, services, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (entry.Instance is not null)
                {
                    logger.LogError(ex, "The uninstall step of plugin {SystemName} failed", systemName);
                    return Result<PluginInfo>.Error($"The uninstall step of '{entry.FriendlyName}' failed: {ex.Message}");
                }

                // The plugin could not even be loaded. Do not trap the administrator with a plugin that cannot be removed.
                logger.LogWarning(ex, "Plugin {SystemName} could not be loaded to run its uninstall step. Removing it anyway", systemName);
            }

            runtime.StateStore.Update(document =>
            {
                var state = document.GetOrAdd(entry.SystemName);
                state.Installed = false;
                state.Enabled = false;
            });

            return Finish(entry, PluginChangeKind.Uninstalled);
        },
        cancellationToken);

    public Task<Result<PluginInfo>> EnableAsync(string systemName, CancellationToken cancellationToken = default) =>
        RunAsync(systemName, entry =>
        {
            if (!entry.State.Installed)
            {
                return Task.FromResult(Result<PluginInfo>.Conflict($"Install '{entry.FriendlyName}' first."));
            }

            if (entry.State.Enabled)
            {
                return Task.FromResult(Result<PluginInfo>.Conflict($"'{entry.FriendlyName}' is already enabled."));
            }

            var missing = FindUnmetDependencies(entry);
            if (missing is not null)
            {
                return Task.FromResult(Invalid(systemName, missing));
            }

            runtime.StateStore.Update(document => document.GetOrAdd(entry.SystemName).Enabled = true);
            return Task.FromResult(Finish(entry, PluginChangeKind.Enabled));
        },
        cancellationToken);

    public Task<Result<PluginInfo>> DisableAsync(string systemName, CancellationToken cancellationToken = default) =>
        RunAsync(systemName, entry =>
        {
            if (!entry.State.Installed || !entry.State.Enabled)
            {
                return Task.FromResult(Result<PluginInfo>.Conflict($"'{entry.FriendlyName}' is not enabled."));
            }

            var dependents = FindDependents(entry, installedOnly: false).Where(name => IsEnabled(name)).ToList();
            if (dependents.Count > 0)
            {
                return Task.FromResult(Invalid(systemName, $"Disable {string.Join(", ", dependents)} first. {(dependents.Count == 1 ? "It depends" : "They depend")} on '{entry.FriendlyName}'."));
            }

            runtime.StateStore.Update(document => document.GetOrAdd(entry.SystemName).Enabled = false);
            return Task.FromResult(Finish(entry, PluginChangeKind.Disabled));
        },
        cancellationToken);

    public Task<Result<PluginInfo>> UpdateAsync(PluginForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);

        return RunAsync(form.SystemName, entry =>
        {
            var name = form.FriendlyName?.Trim();
            if (name is { Length: > MaxNameLength })
            {
                return Task.FromResult(Result<PluginInfo>.Invalid(new ValidationError(nameof(form.FriendlyName), "That name is too long.")));
            }

            // Keep an override only when it differs from the manifest, so that a plugin update can still change the rest.
            var manifest = entry.Manifest;
            var tenants = Clean(form.Tenants);
            var roles = Clean(form.Roles);

            runtime.StateStore.Update(document =>
            {
                var state = document.GetOrAdd(entry.SystemName);
                state.FriendlyName = string.IsNullOrWhiteSpace(name) || name == manifest.FriendlyName ? null : name;
                state.DisplayOrder = form.DisplayOrder == manifest.DisplayOrder ? null : form.DisplayOrder;
                state.Tenants = tenants is not null && tenants.SequenceEqual(manifest.Tenants, StringComparer.OrdinalIgnoreCase) ? null : tenants;
                state.Roles = roles is not null && roles.SequenceEqual(manifest.Roles, StringComparer.OrdinalIgnoreCase) ? null : roles;
            });

            return Task.FromResult(Finish(entry, PluginChangeKind.Updated));
        },
        cancellationToken);
    }

    public async Task<Result> DeleteAsync(string systemName, CancellationToken cancellationToken = default)
    {
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            var entry = Registry.FindEntry(systemName);
            if (entry is null)
            {
                return Result.NotFound("That plugin is not in the list.");
            }

            if (entry.State.Installed)
            {
                return Result.Conflict($"Uninstall '{entry.FriendlyName}' before deleting it.");
            }

            if (entry.IsLoaded && !runtime.Options.ShadowCopy)
            {
                return Result.Conflict($"'{entry.FriendlyName}' is still loaded from its folder. Restart the host, then delete it.");
            }

            var dependents = FindDependents(entry, installedOnly: false);
            if (dependents.Count > 0)
            {
                return Result.Conflict($"{string.Join(", ", dependents)} depend{(dependents.Count == 1 ? "s" : string.Empty)} on '{entry.FriendlyName}'.");
            }

            try
            {
                DeleteDirectory(entry.Directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not delete the folder of plugin {SystemName}", systemName);
                return Result.Error($"The files are in use and could not be deleted. {ex.Message}");
            }

            runtime.StateStore.Update(document => document.Plugins.Remove(entry.SystemName));
            Registry.Remove(entry);
            Registry.Raise(entry.SystemName, PluginChangeKind.Deleted);
            return Result.Success();
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<PluginInfo>>> RescanAsync(CancellationToken cancellationToken = default)
    {
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            Registry.Refresh();
            return Result<IReadOnlyList<PluginInfo>>.Success(Registry.GetAll());
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    private async Task<Result<PluginInfo>> RunAsync(
        string systemName,
        Func<PluginEntry, Task<Result<PluginInfo>>> action,
        CancellationToken cancellationToken)
    {
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            var entry = Registry.FindEntry(systemName);
            return entry is null
                ? Result<PluginInfo>.NotFound("That plugin is not in the list.")
                : await action(entry);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    private Result<PluginInfo> Finish(PluginEntry entry, PluginChangeKind kind)
    {
        Registry.Mutate(() => PluginRegistry.ApplyState(entry, runtime.StateStore.Load()));
        Registry.Raise(entry.SystemName, kind);
        logger.LogInformation("Plugin {SystemName}: {Change}", entry.SystemName, kind);
        return Result<PluginInfo>.Success(Registry.Find(entry.SystemName)!);
    }

    private static Result<PluginInfo> Invalid(string systemName, string message) =>
        Result<PluginInfo>.Invalid(new ValidationError(systemName, message));

    /// <summary>Null when every dependency is installed and enabled and in range. Otherwise a message that says what is missing.</summary>
    private string? FindUnmetDependencies(PluginEntry entry)
    {
        var problems = new List<string>();

        foreach (var dependency in entry.Manifest.Dependencies)
        {
            var found = Registry.FindEntry(dependency.SystemName);
            if (found is null)
            {
                problems.Add($"'{dependency.SystemName}' is not available.");
            }
            else if (!found.State.Installed || !found.State.Enabled)
            {
                problems.Add($"'{found.FriendlyName}' must be installed and enabled first.");
            }
            else if (dependency.Version is not null
                && VersionRange.TryParse(dependency.Version, out var range)
                && NuGetVersion.TryParse(found.Manifest.Version, out var version)
                && !range.Satisfies(version))
            {
                problems.Add($"'{found.FriendlyName}' must be {dependency.Version}, but it is {found.Manifest.Version}.");
            }
        }

        return problems.Count == 0 ? null : $"{entry.FriendlyName} needs: {string.Join(' ', problems)}";
    }

    private List<string> FindDependents(PluginEntry entry, bool installedOnly) => Registry.Read(entries => entries
        .Where(other => !ReferenceEquals(other, entry)
            && (!installedOnly || other.State.Installed)
            && other.Manifest.Dependencies.Any(d => string.Equals(d.SystemName, entry.SystemName, StringComparison.OrdinalIgnoreCase)))
        .Select(other => other.SystemName)
        .ToList());

    private bool IsEnabled(string systemName) => Registry.FindEntry(systemName) is { State: { Installed: true, Enabled: true } };

    private static List<string>? Clean(List<string>? values) => values is null
        ? null
        : [.. values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }
}
