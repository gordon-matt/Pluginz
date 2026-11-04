namespace Pluginz.Registry;

/// <summary>
/// Asks which loaded plugins the current caller may use. <see cref="IPluginRegistry"/> lists everything. The finder
/// applies tenant and role restrictions, using <see cref="IPluginScope"/> unless you pass a <see cref="PluginAccess"/>.
/// </summary>
public interface IPluginFinder
{
    /// <summary>The active plugins that the caller may use, optionally limited to one group.</summary>
    IReadOnlyList<PluginInfo> GetPlugins(string? group = null, PluginAccess? access = null);

    /// <summary>The <see cref="IPlugin"/> instances of the available plugins that are assignable to <typeparamref name="T"/>.</summary>
    IReadOnlyList<T> GetInstances<T>(PluginAccess? access = null)
        where T : class;

    /// <summary>Whether the plugin is active and the caller may use it.</summary>
    bool IsAvailable(string systemName, PluginAccess? access = null);

    /// <summary>
    /// Whether the type comes from an active plugin that the caller may use. A type that is not from a plugin at all
    /// counts as available, so host code can use one check for both. Use it to gate behavior, for example in an
    /// event subscriber that a plugin ships.
    /// </summary>
    bool IsAvailable(Type type, PluginAccess? access = null);
}