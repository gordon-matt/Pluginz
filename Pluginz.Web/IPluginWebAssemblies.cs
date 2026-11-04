using Pluginz.Registry;

namespace Pluginz.Web;

/// <summary>The assemblies of the active plugins, for Blazor's router. Inject it into the host's <c>Routes</c> component.</summary>
public interface IPluginWebAssemblies
{
    /// <summary>
    /// The main assemblies of the plugins that are loaded. Pass them to
    /// <c>&lt;Router AdditionalAssemblies="..."&gt;</c> so that the plugins' <c>@page</c> components are routable.
    /// </summary>
    IReadOnlyList<Assembly> Assemblies { get; }
}

internal sealed class PluginWebAssemblies(IPluginRegistry registry) : IPluginWebAssemblies
{
    public IReadOnlyList<Assembly> Assemblies => registry.ActiveAssemblies;
}