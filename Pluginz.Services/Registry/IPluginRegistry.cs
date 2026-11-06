namespace Pluginz.Registry;

/// <summary>What happened to a plugin.</summary>
public enum PluginChangeKind
{
    /// <summary>A plugin folder appeared, or its files changed.</summary>
    Discovered,

    /// <summary>
    /// The installed value.
    /// </summary>
    Installed,
    /// <summary>
    /// The uninstalled value.
    /// </summary>
    Uninstalled,
    /// <summary>
    /// The enabled value.
    /// </summary>
    Enabled,
    /// <summary>
    /// The disabled value.
    /// </summary>
    Disabled,

    /// <summary>The administrator changed the friendly name, order, tenants or roles.</summary>
    Updated,

    /// <summary>The plugin folder was deleted.</summary>
    Deleted,
}

/// <summary>
/// Represents the plugin changed event args.
/// </summary>
public sealed class PluginChangedEventArgs(string systemName, PluginChangeKind kind) : EventArgs
{
    /// <summary>
    /// Gets the system name.
    /// </summary>
    public string SystemName { get; } = systemName;

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public PluginChangeKind Kind { get; } = kind;
}

/// <summary>A read-only view of every plugin Pluginz knows about and of the ones that are loaded.</summary>
public interface IPluginRegistry
{
    /// <summary>Raised after any plugin is installed, removed, changed or discovered. It can fire on any thread.</summary>
    event EventHandler<PluginChangedEventArgs>? Changed;

    /// <summary>True when the host must restart for the current plugin state to take effect.</summary>
    bool RestartRequired { get; }

    /// <summary>All plugins ordered by display order, then friendly name.</summary>
    IReadOnlyList<PluginInfo> GetAll();

    /// <summary>
    /// Finds the value.
    /// </summary>
    /// <param name="systemName">The system name.</param>
    /// <returns>The plugin info.</returns>
    PluginInfo? Find(string systemName);

    /// <summary>Finds the plugin that an assembly belongs to, including assemblies the plugin loads for itself.</summary>
    PluginInfo? FindByAssembly(Assembly assembly);

    /// <summary>
    /// Finds by the type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The type.</returns>
    PluginInfo? FindByType(Type type) => FindByAssembly(type.Assembly);

    /// <summary>
    /// Determines whether this instance is active.
    /// </summary>
    /// <param name="systemName">The system name.</param>
    /// <returns>true when the condition is met; otherwise, false.</returns>
    bool IsActive(string systemName);

    /// <summary>The main assemblies of the plugins that are loaded, in activation order.</summary>
    IReadOnlyList<Assembly> ActiveAssemblies { get; }

    /// <summary>The instances of the <see cref="IPlugin"/> types of the loaded plugins, in activation order.</summary>
    IReadOnlyList<IPlugin> ActivePlugins { get; }

    /// <summary>The concrete types in loaded plugins that can be assigned to <typeparamref name="T"/>.</summary>
    IReadOnlyList<Type> GetTypes<T>();

    /// <summary>The concrete types in loaded plugins that can be assigned to <paramref name="contract"/>.</summary>
    IReadOnlyList<Type> GetTypes(Type contract);

    /// <summary>Reads the plugins folder again. New folders appear and changed manifests are re-read.</summary>
    void Refresh();
}