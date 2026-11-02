namespace Pluginz.State;

/// <summary>The content of the state file. It records what administrators decided, not what a plugin declares.</summary>
public sealed class PluginStateDocument
{
    public Dictionary<string, PluginState> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PluginState GetOrAdd(string systemName)
    {
        if (!Plugins.TryGetValue(systemName, out var state))
        {
            state = new PluginState();
            Plugins[systemName] = state;
        }

        return state;
    }
}

/// <summary>What is recorded for one plugin.</summary>
public sealed class PluginState
{
    public bool Installed { get; set; }

    public bool Enabled { get; set; }

    public string? InstalledVersion { get; set; }

    public DateTime? InstalledUtc { get; set; }

    /// <summary>An override for the manifest's friendly name.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>An override for the manifest's display order.</summary>
    public int? DisplayOrder { get; set; }

    /// <summary>An override for the manifest's tenants. Null means use the manifest. Empty means every tenant.</summary>
    public List<string>? Tenants { get; set; }

    /// <summary>An override for the manifest's roles. Null means use the manifest. Empty means every role.</summary>
    public List<string>? Roles { get; set; }
}
