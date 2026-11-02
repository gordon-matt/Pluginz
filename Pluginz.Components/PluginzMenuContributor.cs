using Heroicsoft.NetForge.Components.Navigation;

namespace Pluginz.Components;

public sealed class PluginzMenuContributor : INetForgeMenuContributor
{
    public const string ContributorName = "Pluginz";

    public string Name => ContributorName;

    public string DefaultMenu => "Settings";

    public void Contribute(INetForgeMenuGroupBuilder group, Action<NetForgeMenuItemOptions>? configure)
    {
        group.AddCustom(item =>
        {
            item.Title = "Plugins";
            item.Url = PluginzRoutes.Plugins;
            item.Icon = "collection";
            item.MatchAll = true;
            item.ActivePaths = [PluginzRoutes.Plugins, PluginzRoutes.Browse];
            configure?.Invoke(item);
        });
    }
}

public static class PluginzMenuExtensions
{
    /// <summary>Adds the Plugins link. The host can still retitle it.</summary>
    public static INetForgeMenuGroupBuilder AddPluginz(
        this INetForgeMenuGroupBuilder group,
        Action<NetForgeMenuItemOptions>? configure = null) =>
        group.AddContributor(PluginzMenuContributor.ContributorName, configure);
}
