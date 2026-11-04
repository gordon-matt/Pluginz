using Heroicsoft.NetForge.Abstractions.Auth;

namespace Pluginz.Demo;

/// <summary>NetForge's shell asks for an auth provider even when the user menu is hidden.</summary>
internal sealed class DemoAuth : IAuthProviderService
{
    public AuthProvider Provider => AuthProvider.None;

    public string? KeycloakAuthority => null;

    public bool IsKeycloak => false;

    public bool IsIdentity => false;

    public bool IsNone => true;

    public string? KeycloakAccountUrl => null;
}