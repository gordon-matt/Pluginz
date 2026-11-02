namespace Pluginz.Registry;

internal sealed class PluginFinder(PluginRegistry registry, IPluginScope scope) : IPluginFinder
{
    public IReadOnlyList<PluginInfo> GetPlugins(string? group = null, PluginAccess? access = null)
    {
        var effective = Resolve(access);

        return [.. registry.GetAll().Where(info =>
            info.Status == PluginStatus.Active
            && (group is null || string.Equals(info.Group, group, StringComparison.OrdinalIgnoreCase))
            && effective.Allows(info.Tenants, info.Roles))];
    }

    public IReadOnlyList<T> GetInstances<T>(PluginAccess? access = null)
        where T : class
    {
        var effective = Resolve(access);

        return [.. registry.ActiveEntries
            .Where(entry => entry.Instance is T && entry.IsLoaded && effective.Allows(entry.Tenants, entry.Roles))
            .Select(entry => (T)(object)entry.Instance!)];
    }

    public bool IsAvailable(string systemName, PluginAccess? access = null)
    {
        var info = registry.Find(systemName);
        return info is { Status: PluginStatus.Active } && Resolve(access).Allows(info.Tenants, info.Roles);
    }

    public bool IsAvailable(Type type, PluginAccess? access = null)
    {
        var info = registry.FindByAssembly(type.Assembly);
        return info is null || (info.Status == PluginStatus.Active && Resolve(access).Allows(info.Tenants, info.Roles));
    }

    private PluginAccess Resolve(PluginAccess? access) => access ?? new PluginAccess(scope.TenantId, scope.Roles);
}

/// <summary>The scope used when the host does not register one: the global tenant and no roles.</summary>
internal sealed class NullPluginScope : IPluginScope
{
    public string? TenantId => null;

    public IReadOnlyCollection<string> Roles => [];
}
