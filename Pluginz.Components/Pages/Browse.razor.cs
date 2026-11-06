using NuGet.Versioning;

namespace Pluginz.Components.Pages;

/// <summary>
/// Represents the browse.
/// </summary>
public partial class Browse
{
    /// <summary>
    /// Gets or sets the manager.
    /// </summary>
    [Inject]
    public IPluginManager Manager { get; set; } = default!;

    /// <summary>
    /// Gets or sets the options.
    /// </summary>
    [Inject]
    public IOptions<PluginzOptions> Options { get; set; } = default!;

    /// <summary>
    /// Gets or sets the services.
    /// </summary>
    [Inject]
    public IServiceProvider Services { get; set; } = default!;

    /// <summary>
    /// Gets or sets the toasts.
    /// </summary>
    [Inject]
    public ToastService Toasts { get; set; } = default!;

    private List<PluginPackageInfo> results = [];
    private List<PluginUpdateInfo>? updates;
    private string? query;
    private string? error;
    private string? working;
    private bool includePrerelease;
    private bool showUpdates;
    private bool loading;
    private bool denied;

    private IPluginPackageService? Packages => Services.GetService<IPluginPackageService>();

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        if (Packages is null)
        {
            return;
        }

        if (!await PluginzAccess.CanManageAsync(Options.Value, Services))
        {
            denied = true;
            return;
        }

        await SearchAsync();
    }

    private void OnQueryChanged(string? value) => query = value;

    private async Task SearchAsync()
    {
        showUpdates = false;
        loading = true;
        error = null;
        StateHasChanged();

        var found = await Packages!.SearchAsync(query, includePrerelease);
        loading = false;

        if (!found.IsSuccess)
        {
            error = found.GetDisplayMessage();
            return;
        }

        results = [.. found.Value];
    }

    private void ShowSearch() => showUpdates = false;

    private async Task ShowUpdatesAsync()
    {
        showUpdates = true;
        loading = true;
        error = null;
        StateHasChanged();

        var found = await Packages!.GetUpdatesAsync(includePrerelease);
        loading = false;

        if (!found.IsSuccess)
        {
            error = found.GetDisplayMessage();
            return;
        }

        updates = [.. found.Value];
    }

    private async Task InstallAsync(PluginPackageInfo package)
    {
        working = package.PackageId;
        bool wasInstalled = package.InstalledSystemName is not null;

        try
        {
            var added = await Packages!.InstallAsync(package.PackageId, package.Version, includePrerelease);
            if (!added.IsSuccess)
            {
                Toasts.ShowFailure(added);
                return;
            }

            if (wasInstalled)
            {
                Toasts.Success($"{added.Value.FriendlyName} {package.Version} was downloaded. It replaces the running version after a restart.");
            }
            else
            {
                // Downloading is the first half. Installing runs the plugin's own install step.
                var installed = await Manager.InstallAsync(added.Value.SystemName);
                if (!installed.IsSuccess)
                {
                    Toasts.Warning($"{added.Value.FriendlyName} was downloaded but not installed. {installed.GetDisplayMessage()}");
                }
                else
                {
                    Toasts.Success($"{added.Value.FriendlyName} is installed. It starts after a restart.");
                }
            }
        }
        finally
        {
            working = null;
        }

        await SearchAsync();
    }

    private async Task UpdateAsync(PluginUpdateInfo update)
    {
        working = update.PackageId;

        try
        {
            var added = await Packages!.InstallAsync(update.PackageId, update.LatestVersion, includePrerelease);
            if (!added.IsSuccess)
            {
                Toasts.ShowFailure(added);
                return;
            }

            Toasts.Success($"{added.Value.FriendlyName} {update.LatestVersion} was downloaded. It replaces the running version after a restart.");
        }
        finally
        {
            working = null;
        }

        await ShowUpdatesAsync();
    }

    private static bool IsNewer(PluginPackageInfo package) =>
        NuGetVersion.TryParse(package.Version, out var latest)
        && NuGetVersion.TryParse(package.InstalledVersion, out var installed)
        && latest > installed;
}