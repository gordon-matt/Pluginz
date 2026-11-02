namespace Pluginz.State;

/// <summary>
/// Persists <see cref="PluginStateDocument"/>. It is synchronous on purpose: the host reads it while it builds its
/// service container, before any async infrastructure or database exists.
/// </summary>
public interface IPluginStateStore
{
    /// <summary>Returns a copy of the stored state. An empty document when nothing is stored.</summary>
    PluginStateDocument Load();

    /// <summary>Reads the state, applies <paramref name="change"/> and writes it back as one atomic step.</summary>
    void Update(Action<PluginStateDocument> change);
}
