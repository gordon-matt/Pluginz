using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Pluginz.Components;

/// <summary>
/// Plugins are code that runs inside the host, so the admin fails closed: when authentication or a policy is
/// required and the host has no auth services, nobody gets in.
/// </summary>
public static class PluginzAccess
{
    /// <summary>Whether the current circuit's user may view plugins.</summary>
    public static Task<bool> CanReadAsync(PluginzOptions options, IServiceProvider services) =>
        AllowAsync(options, services, options.ReadPolicy, user: null);

    /// <summary>Whether the current circuit's user may install, remove, enable, disable and upload plugins.</summary>
    public static Task<bool> CanManageAsync(PluginzOptions options, IServiceProvider services) =>
        AllowAsync(options, services, options.ManagePolicy ?? options.ReadPolicy, user: null);

    /// <summary>
    /// Pass <paramref name="user"/> from an HTTP endpoint. The Blazor authentication-state provider throws when it is
    /// resolved outside a component.
    /// </summary>
    public static Task<bool> CanManageAsync(PluginzOptions options, IServiceProvider services, ClaimsPrincipal? user) =>
        AllowAsync(options, services, options.ManagePolicy ?? options.ReadPolicy, user);

    private static async Task<bool> AllowAsync(PluginzOptions options, IServiceProvider services, string? policy, ClaimsPrincipal? user)
    {
        if (!options.RequireAuthentication && string.IsNullOrWhiteSpace(policy))
        {
            return true;
        }

        user ??= await UserFromCircuitAsync(services);
        if (user is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(policy))
        {
            var authorization = services.GetService<IAuthorizationService>();
            if (authorization is null)
            {
                return false;
            }

            return (await authorization.AuthorizeAsync(user, policy)).Succeeded;
        }

        return user.Identity?.IsAuthenticated == true;
    }

    private static async Task<ClaimsPrincipal?> UserFromCircuitAsync(IServiceProvider services)
    {
        var authState = services.GetService<AuthenticationStateProvider>();
        if (authState is null)
        {
            return null;
        }

        var state = await authState.GetAuthenticationStateAsync();
        return state.User;
    }
}
