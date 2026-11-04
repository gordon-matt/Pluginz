namespace Pluginz.Web;

/// <summary>
/// A plugin that adds to the HTTP pipeline. The host calls <c>app.UsePluginz()</c> and <c>app.MapPluginz()</c>, which
/// call these members for each active plugin, in activation order. Controllers and Razor components need neither:
/// see <c>AddPluginzParts</c> and <c>AddPluginzAssemblies</c>.
/// </summary>
public interface IWebPlugin : IPlugin
{
    /// <summary>Adds middleware. It runs where the host calls <c>UsePluginz()</c>.</summary>
    void Configure(IApplicationBuilder app, PluginRuntimeContext context)
    {
    }

    /// <summary>Maps endpoints. It runs where the host calls <c>MapPluginz()</c>.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints, PluginRuntimeContext context)
    {
    }
}