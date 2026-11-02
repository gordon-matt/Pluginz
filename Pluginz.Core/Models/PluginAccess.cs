namespace Pluginz.Models;

/// <summary>
/// Tells Pluginz who is asking, so that tenant and role restrictions can be applied. Register one in the host
/// per request. The default scope has no tenant and no roles.
/// </summary>
public interface IPluginScope
{
    /// <summary>The current tenant. Null or empty means the global scope, which sees every plugin.</summary>
    string? TenantId { get; }

    /// <summary>The roles of the current user.</summary>
    IReadOnlyCollection<string> Roles { get; }
}

/// <summary>An explicit tenant and role filter. Pass it to the finder instead of using <see cref="IPluginScope"/>.</summary>
public sealed record PluginAccess(string? TenantId = null, IReadOnlyCollection<string>? Roles = null, bool Unrestricted = false)
{
    /// <summary>Skips tenant and role checks. Use it for background work that has no user.</summary>
    public static PluginAccess Everyone { get; } = new(Unrestricted: true);

    /// <summary>Whether a plugin with these restrictions is available to this caller.</summary>
    public bool Allows(IReadOnlyCollection<string> tenants, IReadOnlyCollection<string> roles)
    {
        if (Unrestricted)
        {
            return true;
        }

        if (tenants.Count > 0
            && !string.IsNullOrWhiteSpace(TenantId)
            && !tenants.Contains(TenantId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (roles.Count > 0)
        {
            return Roles is { Count: > 0 } && Roles.Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase));
        }

        return true;
    }
}
