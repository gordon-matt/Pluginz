namespace Pluginz.Loading;

/// <summary>
/// Decides, for each assembly a plugin asks for, whether the plugin gets the host's copy or its own. This one
/// decision is what keeps type identity intact: a type that crosses the plugin boundary must come from one assembly.
/// </summary>
public sealed class SharedAssemblyRules
{
    private static readonly string[] pluginzAssemblies =
    [
        "Pluginz.Core", "Pluginz.Services", "Pluginz.Web", "Pluginz.NuGet", "Pluginz.Components",
    ];

    private readonly PluginIsolationOptions options;

    /// <summary>
    /// Initializes a new instance of the SharedAssemblyRules class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="catalog">The catalog.</param>
    public SharedAssemblyRules(PluginIsolationOptions options, HostAssemblyCatalog? catalog = null)
    {
        this.options = options;
        Catalog = catalog ?? HostAssemblyCatalog.Current;
    }

    /// <summary>
    /// Gets the catalog.
    /// </summary>
    public HostAssemblyCatalog Catalog { get; }

    /// <summary>
    /// True when the plugin must use the host's copy of <paramref name="assemblyName"/>. The loader still falls back
    /// to the plugin's own copy when the host turns out not to have one.
    /// </summary>
    public bool IsShared(string assemblyName, IReadOnlyCollection<string>? privateAssemblies = null)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return false;
        }

        // Never separable. A second copy of these breaks the plugin contract or the runtime.
        return pluginzAssemblies.Contains(assemblyName, StringComparer.OrdinalIgnoreCase) || Catalog.IsFramework(assemblyName) || ((privateAssemblies?.Contains(assemblyName, StringComparer.OrdinalIgnoreCase)) != true && (options.SharedAssemblies.Contains(assemblyName, StringComparer.OrdinalIgnoreCase)
            || options.SharedPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || (options.Mode == PluginIsolationMode.PreferHost && Catalog.Provides(assemblyName))));
    }

    /// <summary>
    /// True when a package dependency does not need to be copied into the plugin folder because the plugin would
    /// load the host's copy anyway.
    /// </summary>
    public bool IsProvidedByHost(string assemblyName, IReadOnlyCollection<string>? privateAssemblies = null) =>
        IsShared(assemblyName, privateAssemblies) && Catalog.Provides(assemblyName);
}