using System.Runtime.CompilerServices;
using NuGet.Versioning;
using Pluginz.Discovery;
using Pluginz.Loading;
using Pluginz.Registry;
using Pluginz.State;

namespace Pluginz.Hosting;

/// <summary>
/// The engine behind <c>AddPluginz</c>. It discovers plugins, loads the ones that are installed and enabled, and lets
/// them register services. It runs once, before the host's container is built, because that is the only moment
/// at which a plugin can still add services.
/// </summary>
public sealed class PluginRuntime
{
    private readonly IConfiguration configuration;
    private readonly string contentRootPath;
    private readonly string environmentName;
    private readonly List<(LogLevel Level, string Message, Exception? Error)> startupLog = [];

    private PluginRuntime(PluginzOptions options, IConfiguration configuration, string contentRootPath, string environmentName)
    {
        Options = options;
        this.configuration = configuration;
        this.contentRootPath = contentRootPath;
        this.environmentName = environmentName;

        Paths = PluginPaths.From(options, contentRootPath);
        Rules = new SharedAssemblyRules(options.Isolation);
        StateStore = new JsonPluginStateStore(Paths.StateFile);
        Discovery = new PluginDiscovery(Paths, options, Log);

        Paths.EnsureCreated();
        Discovery.ApplyPending();

        var entries = Discovery.Scan();
        var document = StateStore.Load();
        var pending = Discovery.ReadPendingVersions();

        foreach (var entry in entries)
        {
            PluginRegistry.ApplyState(entry, document);
            entry.PendingVersion = pending.GetValueOrDefault(entry.SystemName);
        }

        RegistryInternal = new PluginRegistry(Discovery, StateStore, entries);
        Activate();
    }

    public PluginzOptions Options { get; }

    public PluginPaths Paths { get; }

    internal IConfiguration Configuration => configuration;

    public IPluginStateStore StateStore { get; }

    public IPluginRegistry Registry => RegistryInternal;

    internal PluginRegistry RegistryInternal { get; }

    internal PluginDiscovery Discovery { get; }

    internal SharedAssemblyRules Rules { get; }

    /// <summary>Serializes everything that changes plugins: install, uninstall, upload, delete, update.</summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal IReadOnlyList<(LogLevel Level, string Message, Exception? Error)> StartupLog => startupLog;

    /// <summary>Discovers and loads plugins. <see cref="ConfigureServices"/> then lets them register services.</summary>
    public static PluginRuntime Create(
        PluginzOptions options,
        IConfiguration configuration,
        string contentRootPath,
        string environmentName) => new(options, configuration, contentRootPath, environmentName);

    /// <summary>Calls <see cref="IPlugin.ConfigureServices"/> on every loaded plugin, in dependency order.</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        foreach (var entry in RegistryInternal.ActiveEntries)
        {
            var before = services.Count;

            try
            {
                entry.Instance?.ConfigureServices(services, entry.Context!);

                if (entry.Instance is not null)
                {
                    services.AddSingleton(typeof(IPlugin), entry.Instance);
                    services.AddSingleton(entry.PluginType!, entry.Instance);
                }

                Log(LogLevel.Information, $"Plugin '{entry.SystemName}' {entry.LoadedVersion} is active.", null);
            }
            catch (Exception ex)
            {
                // Remove whatever the plugin managed to register before it failed.
                while (services.Count > before)
                {
                    services.RemoveAt(services.Count - 1);
                }

                Fail(entry, $"ConfigureServices failed: {ex.Message}", ex);
            }
        }

        RegistryInternal.SetActivationOrder(RegistryInternal.ActiveEntries.Where(e => e.IsLoaded));
    }

    /// <summary>Runs a plugin's install or uninstall code, loading it into a throw-away context if it is not active.</summary>
    internal async Task RunHookAsync(PluginEntry entry, PluginHook hook, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (entry is { Instance: not null, Context: not null })
        {
            await InvokeAsync(entry.Instance, hook, new PluginRuntimeContext(entry.Context, services), cancellationToken);
            return;
        }

        // Not loaded: use a collectible context so the plugin's files and types are released afterwards.
        var directory = Options.ShadowCopy
            ? ShadowCopier.Copy(entry.Directory, Paths.ShadowRoot, entry.SystemName)
            : entry.Directory;

        var context = new PluginLoadContext(
            entry.SystemName,
            Path.Combine(directory, entry.Manifest.AssemblyFileName),
            Rules,
            entry.Manifest.PrivateAssemblies,
            isCollectible: true);

        var reference = new WeakReference(context);

        try
        {
            await RunHookInContextAsync(context, entry.Manifest, entry.ToInfo(), hook, services, cancellationToken);
        }
        finally
        {
            context.Unload();
        }

        for (var attempt = 0; attempt < 8 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task RunHookInContextAsync(
        PluginLoadContext loadContext,
        PluginManifest manifest,
        PluginInfo info,
        PluginHook hook,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var assembly = loadContext.LoadMainAssembly();
        var type = FindPluginType(assembly, manifest);
        if (type is null)
        {
            return;
        }

        var plugin = (IPlugin)CreateInstance(type);
        var context = new PluginRuntimeContext(CreateContext(info), services);
        await InvokeAsync(plugin, hook, context, cancellationToken);
    }

    private static Task InvokeAsync(IPlugin plugin, PluginHook hook, PluginRuntimeContext context, CancellationToken cancellationToken) =>
        hook == PluginHook.Install
            ? plugin.InstallAsync(context, cancellationToken)
            : plugin.UninstallAsync(context, cancellationToken);

    internal PluginContext CreateContext(PluginInfo info) =>
        new(info, configuration, contentRootPath, environmentName, Paths.DataRoot);

    private void Activate()
    {
        var candidates = RegistryInternal.Entries
            .Where(e => e is { State: { Installed: true, Enabled: true }, Problems.Count: 0 })
            .ToList();

        var active = new List<PluginEntry>();

        foreach (var entry in Order(candidates))
        {
            var problems = CheckDependencies(entry, active);
            if (problems.Count > 0)
            {
                entry.ActivationProblems.AddRange(problems);
                Log(LogLevel.Warning, $"Plugin '{entry.SystemName}' was not loaded. {string.Join(' ', problems)}", null);
                continue;
            }

            try
            {
                Load(entry);
                active.Add(entry);
            }
            catch (Exception ex)
            {
                Fail(entry, $"Loading failed: {ex.Message}", ex);

                if (Options.ThrowOnLoadFailure)
                {
                    throw new InvalidOperationException($"Plugin '{entry.SystemName}' could not be loaded.", ex);
                }
            }
        }

        RegistryInternal.SetActivationOrder(active);
    }

    private void Load(PluginEntry entry)
    {
        var directory = Options.ShadowCopy
            ? ShadowCopier.Copy(entry.Directory, Paths.ShadowRoot, entry.SystemName)
            : entry.Directory;

        var context = new PluginLoadContext(
            entry.SystemName,
            Path.Combine(directory, entry.Manifest.AssemblyFileName),
            Rules,
            entry.Manifest.PrivateAssemblies);

        var assembly = context.LoadMainAssembly();
        var type = FindPluginType(assembly, entry.Manifest);

        entry.LoadContext = context;
        entry.Assembly = assembly;
        entry.PluginType = type;
        entry.Instance = type is null ? null : (IPlugin)CreateInstance(type);
        entry.LoadedVersion = entry.Manifest.Version;
        entry.LoadedDirectory = directory;
        entry.Context = CreateContext(entry.ToInfo());
    }

    private static List<string> CheckDependencies(PluginEntry entry, List<PluginEntry> active)
    {
        var problems = new List<string>();

        foreach (var dependency in entry.Manifest.Dependencies)
        {
            var found = active.FirstOrDefault(e => string.Equals(e.SystemName, dependency.SystemName, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                problems.Add($"It requires plugin '{dependency.SystemName}', which is not installed and active.");
            }
            else if (dependency.Version is not null
                && VersionRange.TryParse(dependency.Version, out var range)
                && NuGetVersion.TryParse(found.Manifest.Version, out var version)
                && !range.Satisfies(version))
            {
                problems.Add($"It requires plugin '{dependency.SystemName}' {dependency.Version}, but {found.Manifest.Version} is installed.");
            }
        }

        return problems;
    }

    /// <summary>Orders plugins so that each one comes after the plugins it depends on.</summary>
    private static List<PluginEntry> Order(List<PluginEntry> candidates)
    {
        var byName = candidates.ToDictionary(e => e.SystemName, StringComparer.OrdinalIgnoreCase);
        var result = new List<PluginEntry>();
        var visited = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        void Visit(PluginEntry entry)
        {
            if (visited.TryGetValue(entry.SystemName, out var done))
            {
                if (!done)
                {
                    entry.ActivationProblems.Add("It is part of a circular plugin dependency.");
                }

                return;
            }

            visited[entry.SystemName] = false;

            foreach (var dependency in entry.Manifest.Dependencies)
            {
                if (byName.TryGetValue(dependency.SystemName, out var required))
                {
                    Visit(required);
                }
            }

            visited[entry.SystemName] = true;
            result.Add(entry);
        }

        foreach (var entry in candidates.OrderBy(e => e.DisplayOrder).ThenBy(e => e.SystemName, StringComparer.OrdinalIgnoreCase))
        {
            Visit(entry);
        }

        return result;
    }

    internal static Type? FindPluginType(Assembly assembly, PluginManifest manifest)
    {
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>();
        }

        var candidates = types
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IPlugin).IsAssignableFrom(type))
            .ToList();

        if (manifest.EntryType is not null)
        {
            return candidates.FirstOrDefault(type => type.FullName == manifest.EntryType)
                ?? throw new InvalidOperationException($"The entry type '{manifest.EntryType}' was not found in {manifest.AssemblyFileName}.");
        }

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                $"{manifest.AssemblyFileName} has {candidates.Count} IPlugin types. Set entryType in {PluginNames.ManifestFileName} to pick one."),
        };
    }

    private static object CreateInstance(Type type) =>
        Activator.CreateInstance(type, nonPublic: true)
        ?? throw new InvalidOperationException($"Could not create {type.FullName}. It needs a parameterless constructor.");

    private void Fail(PluginEntry entry, string message, Exception ex)
    {
        entry.ActivationFailed = true;
        entry.ActivationProblems.Add(message);
        Log(LogLevel.Error, $"Plugin '{entry.SystemName}': {message}", ex);
    }

    private void Log(LogLevel level, string message, Exception? error) => startupLog.Add((level, message, error));
}

internal enum PluginHook
{
    Install,
    Uninstall,
}
