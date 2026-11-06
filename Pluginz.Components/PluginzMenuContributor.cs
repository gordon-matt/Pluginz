using Heroicsoft.NetForge.Components.Navigation;

namespace Pluginz.Components;

/// <summary>
/// Represents the pluginz menu contributor.
/// </summary>
public sealed class PluginzMenuContributor : INetForgeMenuContributor
{
    /// <summary>
    /// The contributor name constant.
    /// </summary>
    public const string ContributorName = "Pluginz";

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => ContributorName;

    /// <summary>
    /// Gets the default menu.
    /// </summary>
    public string DefaultMenu => "Settings";

    /// <summary>
    /// Performs the contribute operation.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="configure">The configure.</param>
    public void Contribute(INetForgeMenuGroupBuilder group, Action<NetForgeMenuItemOptions>? configure) => group.AddCustom(item =>
    {
        item.Title = "Plugins";
        item.Url = PluginzRoutes.Plugins;
        item.Icon = "collection";
        item.MatchAll = true;
        item.ActivePaths = [PluginzRoutes.Plugins, PluginzRoutes.Browse];
        configure?.Invoke(item);
    });
}

/// <summary>
/// Provides extension methods for pluginz menus.
/// </summary>
public static class PluginzMenuExtensions
{
    /// <summary>Adds the Plugins link. The host can still retitle it.</summary>
    public static INetForgeMenuGroupBuilder AddPluginz(
        this INetForgeMenuGroupBuilder group,
        Action<NetForgeMenuItemOptions>? configure = null) =>
        group.AddContributor(PluginzMenuContributor.ContributorName, configure);
}