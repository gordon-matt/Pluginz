namespace Pluginz.Packages;

/// <summary>Settings for installing plugins from NuGet. Bind them from the <c>Pluginz:NuGet</c> section.</summary>
public sealed class PluginzNuGetOptions
{
    /// <summary>
    /// The section name constant.
    /// </summary>
    public const string SectionName = "Pluginz:NuGet";

    /// <summary>The feeds to search and install from. When empty, nuget.org is used.</summary>
    public List<PluginPackageSource> Sources { get; set; } = [];

    /// <summary>
    /// Only packages that carry this tag are listed when browsing. nuget.org has far more packages than plugins, so
    /// without a tag the list would be meaningless. Set it to null for a private feed that holds only plugins.
    /// </summary>
    public string? TagFilter { get; set; } = "pluginz";

    /// <summary>The framework that package assets are chosen for. Use the host's target framework.</summary>
    public string TargetFramework { get; set; } = "net10.0";

    /// <summary>Whether prerelease versions are offered by default.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>The largest package that is downloaded or accepted, in megabytes.</summary>
    public int MaxPackageMegabytes { get; set; } = 100;

    /// <summary>The most dependency packages that one install pulls in. A guard against runaway dependency trees.</summary>
    public int MaxDependencyPackages { get; set; } = 200;

    internal IReadOnlyList<PluginPackageSource> EffectiveSources =>
        Sources.Count > 0 ? Sources : [new PluginPackageSource { Name = "nuget.org", Url = "https://api.nuget.org/v3/index.json" }];
}

/// <summary>A NuGet feed. Either a v3 service index URL or the path of a folder that holds .nupkg files.</summary>
public sealed class PluginPackageSource
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>For feeds that need basic authentication. Keep the password in a secret store, not in a settings file.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string? Password { get; set; }
}