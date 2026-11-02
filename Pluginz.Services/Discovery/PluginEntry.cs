using Pluginz.Loading;
using Pluginz.State;

namespace Pluginz.Discovery;

/// <summary>
/// The registry's mutable record of one plugin: what the manifest says, what the administrator decided and, when
/// the plugin is active, what is loaded. Only the registry changes it, under its lock.
/// </summary>
internal sealed class PluginEntry
{
    public required PluginManifest Manifest { get; set; }

    public required string Directory { get; set; }

    public PluginOriginRecord? OriginRecord { get; set; }

    public PluginState State { get; set; } = new();

    /// <summary>Problems found by reading the folder: bad manifest, missing assembly, wrong host version.</summary>
    public List<string> Problems { get; set; } = [];

    /// <summary>Problems found while activating: missing dependency, exceptions from the plugin.</summary>
    public List<string> ActivationProblems { get; } = [];

    public string? PendingVersion { get; set; }

    // Set only while the plugin is loaded in this process.
    public PluginLoadContext? LoadContext { get; set; }

    public Assembly? Assembly { get; set; }

    public Type? PluginType { get; set; }

    public IPlugin? Instance { get; set; }

    public PluginContext? Context { get; set; }

    public string? LoadedVersion { get; set; }

    public string? LoadedDirectory { get; set; }

    public bool ActivationFailed { get; set; }

    public string SystemName => Manifest.SystemName;

    public bool IsLoaded => LoadContext is not null && !ActivationFailed;

    public string FriendlyName => string.IsNullOrWhiteSpace(State.FriendlyName)
        ? string.IsNullOrWhiteSpace(Manifest.FriendlyName) ? Manifest.SystemName : Manifest.FriendlyName
        : State.FriendlyName;

    public int DisplayOrder => State.DisplayOrder ?? Manifest.DisplayOrder;

    public IReadOnlyList<string> Tenants => State.Tenants ?? Manifest.Tenants;

    public IReadOnlyList<string> Roles => State.Roles ?? Manifest.Roles;

    public PluginStatus Status
    {
        get
        {
            if (Problems.Count > 0)
            {
                return PluginStatus.Incompatible;
            }

            if (!State.Installed || !State.Enabled)
            {
                if (IsLoaded)
                {
                    return PluginStatus.PendingDeactivation;
                }

                return State.Installed ? PluginStatus.Disabled : PluginStatus.NotInstalled;
            }

            if (IsLoaded)
            {
                return PluginStatus.Active;
            }

            return ActivationFailed || ActivationProblems.Count > 0 ? PluginStatus.Failed : PluginStatus.PendingActivation;
        }
    }

    public bool RestartRequired =>
        Status is PluginStatus.PendingActivation or PluginStatus.PendingDeactivation
        || PendingVersion is not null
        || (IsLoaded && LoadedVersion is not null && !string.Equals(LoadedVersion, Manifest.Version, StringComparison.OrdinalIgnoreCase));

    public PluginInfo ToInfo() => new()
    {
        SystemName = Manifest.SystemName,
        FriendlyName = FriendlyName,
        Group = Manifest.Group,
        Version = Manifest.Version,
        LoadedVersion = IsLoaded && !string.Equals(LoadedVersion, Manifest.Version, StringComparison.OrdinalIgnoreCase) ? LoadedVersion : null,
        PendingVersion = PendingVersion,
        Author = Manifest.Author,
        Description = Manifest.Description,
        DisplayOrder = DisplayOrder,
        DirectoryPath = Directory,
        AssemblyFileName = Manifest.AssemblyFileName,
        Origin = OriginRecord?.Origin ?? PluginOrigin.Folder,
        PackageId = OriginRecord?.PackageId,
        Source = OriginRecord?.Source,
        IsInstalled = State.Installed,
        IsEnabled = State.Enabled,
        Status = Status,
        RestartRequired = RestartRequired,
        Problems = [.. Problems, .. ActivationProblems],
        Dependencies = Manifest.Dependencies,
        Tenants = Tenants,
        Roles = Roles,
        Tags = Manifest.Tags,
        IconUrl = Manifest.IconUrl,
        ProjectUrl = Manifest.ProjectUrl,
        License = Manifest.License,
        ConfigurationUrl = IsLoaded ? SafeConfigurationUrl() : null,
    };

    private string? SafeConfigurationUrl()
    {
        try
        {
            return Instance?.ConfigurationUrl;
        }
        catch
        {
            return null;
        }
    }
}
