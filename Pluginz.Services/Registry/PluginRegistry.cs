using Pluginz.Discovery;
using Pluginz.Loading;
using Pluginz.State;

namespace Pluginz.Registry;

internal sealed class PluginRegistry : IPluginRegistry
{
    private readonly Lock gate = new();
    private readonly PluginDiscovery discovery;
    private readonly IPluginStateStore store;
    private List<PluginEntry> entries;
    private List<PluginEntry> activationOrder = [];

    public PluginRegistry(PluginDiscovery discovery, IPluginStateStore store, List<PluginEntry> entries)
    {
        this.discovery = discovery;
        this.store = store;
        this.entries = entries;
    }

    public event EventHandler<PluginChangedEventArgs>? Changed;

    public bool RestartRequired
    {
        get
        {
            lock (gate)
            {
                return field || entries.Any(entry => entry.RestartRequired);
            }
        }

        private set;
    }

    public IReadOnlyList<Assembly> ActiveAssemblies
    {
        get
        {
            lock (gate)
            {
                return [.. activationOrder.Where(e => e.Assembly is not null).Select(e => e.Assembly!)];
            }
        }
    }

    public IReadOnlyList<IPlugin> ActivePlugins
    {
        get
        {
            lock (gate)
            {
                return [.. activationOrder.Where(e => e.Instance is not null).Select(e => e.Instance!)];
            }
        }
    }

    /// <summary>The loaded entries in the order they were activated. Stopping reverses it.</summary>
    public IReadOnlyList<PluginEntry> ActiveEntries
    {
        get
        {
            lock (gate)
            {
                return [.. activationOrder];
            }
        }
    }

    public IReadOnlyList<PluginEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
    }

    public IReadOnlyList<PluginInfo> GetAll()
    {
        lock (gate)
        {
            return [.. entries
                .Select(entry => entry.ToInfo())
                .OrderBy(info => info.DisplayOrder)
                .ThenBy(info => info.FriendlyName, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public PluginInfo? Find(string systemName)
    {
        lock (gate)
        {
            return FindUnsafe(systemName)?.ToInfo();
        }
    }

    public PluginEntry? FindEntry(string systemName)
    {
        lock (gate)
        {
            return FindUnsafe(systemName);
        }
    }

    public PluginInfo? FindByAssembly(Assembly assembly)
    {
        // An assembly that a plugin loaded for itself lives in that plugin's load context.
        if (AssemblyLoadContext.GetLoadContext(assembly) is not PluginLoadContext context)
        {
            return null;
        }

        lock (gate)
        {
            return entries.FirstOrDefault(entry => ReferenceEquals(entry.LoadContext, context))?.ToInfo();
        }
    }

    public bool IsActive(string systemName)
    {
        lock (gate)
        {
            return FindUnsafe(systemName) is { Status: PluginStatus.Active };
        }
    }

    public IReadOnlyList<Type> GetTypes<T>() => GetTypes(typeof(T));

    public IReadOnlyList<Type> GetTypes(Type contract)
    {
        var assemblies = ActiveAssemblies;
        var types = new List<Type>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && contract.IsAssignableFrom(type))
                {
                    types.Add(type);
                }
            }
        }

        return types;
    }

    public void Refresh()
    {
        var scanned = discovery.Scan();
        var pending = discovery.ReadPendingVersions();
        var changed = new List<string>();

        lock (gate)
        {
            var document = store.Load();
            var next = new List<PluginEntry>();

            foreach (var found in scanned)
            {
                var existing = FindUnsafe(found.SystemName);
                if (existing is null)
                {
                    ApplyState(found, document);
                    next.Add(found);
                    changed.Add(found.SystemName);
                    continue;
                }

                bool differs = !string.Equals(existing.Manifest.Version, found.Manifest.Version, StringComparison.OrdinalIgnoreCase)
                    || !existing.Problems.SequenceEqual(found.Problems);

                existing.Manifest = found.Manifest;
                existing.Directory = found.Directory;
                existing.Problems = found.Problems;
                existing.OriginRecord = found.OriginRecord;
                next.Add(existing);

                if (differs)
                {
                    changed.Add(existing.SystemName);
                }
            }

            // A loaded plugin stays until the host restarts, even if its folder is gone.
            next.AddRange(entries.Where(e => e.IsLoaded && next.All(n => !string.Equals(n.SystemName, e.SystemName, StringComparison.OrdinalIgnoreCase))));

            entries = next;

            foreach (var entry in entries)
            {
                entry.PendingVersion = pending.GetValueOrDefault(entry.SystemName);
            }
        }

        foreach (string name in changed)
        {
            Raise(name, PluginChangeKind.Discovered);
        }
    }

    /// <summary>Copies the stored decisions onto an entry. Call it under the lock or before the registry is shared.</summary>
    public static void ApplyState(PluginEntry entry, PluginStateDocument document) =>
        entry.State = document.Plugins.TryGetValue(entry.SystemName, out var state) ? state : new PluginState();

    public void SetActivationOrder(IEnumerable<PluginEntry> order)
    {
        lock (gate)
        {
            activationOrder = [.. order];
        }
    }

    public void Remove(PluginEntry entry)
    {
        lock (gate)
        {
            entries = [.. entries.Where(e => !ReferenceEquals(e, entry))];

            // The assemblies stay loaded until the host restarts, so the restart notice must outlive the entry.
            if (entry.IsLoaded)
            {
                RestartRequired = true;
            }
        }
    }

    public T Read<T>(Func<IReadOnlyList<PluginEntry>, T> read)
    {
        lock (gate)
        {
            return read(entries);
        }
    }

    public void Mutate(Action action)
    {
        lock (gate)
        {
            action();
        }
    }

    public void Raise(string systemName, PluginChangeKind kind) =>
        Changed?.Invoke(this, new PluginChangedEventArgs(systemName, kind));

    private PluginEntry? FindUnsafe(string systemName) =>
        entries.FirstOrDefault(entry => string.Equals(entry.SystemName, systemName, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}