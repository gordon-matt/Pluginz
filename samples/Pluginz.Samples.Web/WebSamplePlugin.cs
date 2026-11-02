using Heroicsoft.NetForge.Components;
using Heroicsoft.NetForge.Components.Navigation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pluginz.Web;

namespace Pluginz.Samples.Web;

public sealed class WebSamplePlugin : PluginBase, IWebPlugin
{
    public const string PageUrl = "/samples/web";

    public override string? ConfigurationUrl => PageUrl;

    public override void ConfigureServices(IServiceCollection services, PluginContext context)
    {
        services.AddSingleton<SampleClock>();
        services.AddNetForgeMenuContributor<WebSampleMenuContributor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, PluginRuntimeContext context) =>
        endpoints.MapGet($"{PageUrl}/ping", (SampleClock clock) => Results.Ok(new { plugin = context.SystemName, utc = clock.UtcNow }));
}

/// <summary>A service the plugin registers. Registering it is all it takes for the host's container to supply it.</summary>
public sealed class SampleClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class WebSampleMenuContributor : INetForgeMenuContributor
{
    public string Name => "Pluginz.Samples.Web";

    public string DefaultMenu => "Settings";

    public void Contribute(INetForgeMenuGroupBuilder group, Action<NetForgeMenuItemOptions>? configure) =>
        group.AddCustom(item =>
        {
            item.Title = "Web sample";
            item.Url = WebSamplePlugin.PageUrl;
            item.Icon = "star";
            item.MatchAll = true;
            configure?.Invoke(item);
        });
}
