using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Hosting;

namespace Pluginz.Components.Pages;

public partial class Plugins : IDisposable
{
    [Inject]
    public IPluginRegistry Registry { get; set; } = default!;

    [Inject]
    public IPluginManager Manager { get; set; } = default!;

    [Inject]
    public IPluginArchiveInstaller Archives { get; set; } = default!;

    [Inject]
    public IOptions<PluginzOptions> Options { get; set; } = default!;

    [Inject]
    public IServiceProvider Services { get; set; } = default!;

    [Inject]
    public IHostApplicationLifetime Lifetime { get; set; } = default!;

    [Inject]
    public ToastService Toasts { get; set; } = default!;

    [Inject]
    public DialogService Dialogs { get; set; } = default!;

    private enum PluginFilter
    {
        All,
        Active,
        Available,
        Problems,
    }

    private List<PluginInfo> plugins = [];
    private List<PluginInfo> visible = [];
    private PluginInfo? selected;
    private PluginForm form = new();
    private int? displayOrder;
    private string tenantsText = string.Empty;
    private string rolesText = string.Empty;
    private PluginFilter filter = PluginFilter.All;
    private string? search;
    private string? loadError;
    private bool loading = true;
    private bool denied;
    private bool canManage;
    private bool restartRequired;
    private bool showUpload;
    private bool busy;
    private int uploadKey;

    private IPluginPackageService? Packages => Services.GetService<IPluginPackageService>();

    protected override async Task OnInitializedAsync()
    {
        if (!await PluginzAccess.CanReadAsync(Options.Value, Services))
        {
            denied = true;
            loading = false;
            return;
        }

        canManage = await PluginzAccess.CanManageAsync(Options.Value, Services);
        Registry.Changed += OnRegistryChanged;
        Load();
        loading = false;
    }

    public void Dispose() => Registry.Changed -= OnRegistryChanged;

    private void OnRegistryChanged(object? sender, PluginChangedEventArgs args) =>
        _ = InvokeAsync(() =>
        {
            Load();
            StateHasChanged();
        });

    private void Load()
    {
        try
        {
            plugins = [.. Registry.GetAll()];
            restartRequired = Registry.RestartRequired;
            loadError = null;

            if (selected is not null)
            {
                selected = Registry.Find(selected.SystemName);
            }
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }

        ApplyFilter();
    }

    private void ApplyFilter() =>
        visible = [.. plugins.Where(plugin => Matches(plugin, filter) && MatchesSearch(plugin))];

    private bool MatchesSearch(PluginInfo plugin) =>
        string.IsNullOrWhiteSpace(search)
        || plugin.FriendlyName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || plugin.SystemName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (plugin.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || (plugin.Group?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool Matches(PluginInfo plugin, PluginFilter filter) => filter switch
    {
        PluginFilter.Active => plugin.IsActive,
        PluginFilter.Available => !plugin.IsInstalled,
        PluginFilter.Problems => plugin.Status is PluginStatus.Failed or PluginStatus.Incompatible,
        _ => true,
    };

    private int Count(PluginFilter value) => plugins.Count(plugin => Matches(plugin, value));

    private void SetFilter(PluginFilter value)
    {
        filter = value;
        ApplyFilter();
    }

    private void OnSearchChanged(string? value)
    {
        search = value;
        ApplyFilter();
    }

    private void ToggleUpload() => showUpload = !showUpload;

    private void Select(PluginInfo plugin)
    {
        selected = plugin;
        form = new PluginForm { SystemName = plugin.SystemName, FriendlyName = plugin.FriendlyName };
        displayOrder = plugin.DisplayOrder;
        tenantsText = string.Join(", ", plugin.Tenants);
        rolesText = string.Join(", ", plugin.Roles);
    }

    private void CloseDetails() => selected = null;

    private async Task SaveAsync()
    {
        form.DisplayOrder = displayOrder;
        form.Tenants = Split(tenantsText);
        form.Roles = Split(rolesText);

        await RunAsync(() => Manager.UpdateAsync(form), "Saved.");
    }

    private Task InstallAsync(PluginInfo plugin) =>
        RunAsync(() => Manager.InstallAsync(plugin.SystemName), result => result.IsActive
            ? $"{plugin.FriendlyName} is installed."
            : $"{plugin.FriendlyName} is installed. It starts after a restart.");

    private Task EnableAsync(PluginInfo plugin) =>
        RunAsync(() => Manager.EnableAsync(plugin.SystemName), result => result.Status == PluginStatus.Active
            ? $"{plugin.FriendlyName} is enabled."
            : $"{plugin.FriendlyName} is enabled. It starts after a restart.");

    private Task DisableAsync(PluginInfo plugin) =>
        RunAsync(() => Manager.DisableAsync(plugin.SystemName), result => result.IsActive
            ? $"{plugin.FriendlyName} is disabled. It stops after a restart."
            : $"{plugin.FriendlyName} is disabled.");

    private async Task UninstallAsync(PluginInfo plugin)
    {
        bool confirmed = await Dialogs.ConfirmAsync(
            $"Uninstall {plugin.FriendlyName}?",
            "This runs the plugin's uninstall step. Its settings and data folder are kept. It stops after a restart.",
            "Uninstall",
            isDestructive: true);

        if (confirmed)
        {
            await RunAsync(() => Manager.UninstallAsync(plugin.SystemName), $"{plugin.FriendlyName} is uninstalled.");
        }
    }

    private async Task DeleteAsync(PluginInfo plugin)
    {
        bool confirmed = await Dialogs.ConfirmAsync(
            $"Delete {plugin.FriendlyName}?",
            "The plugin's files are removed from the server. Add it again to get it back.",
            "Delete",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        busy = true;
        var deleted = await Manager.DeleteAsync(plugin.SystemName);
        busy = false;

        if (!deleted.IsSuccess)
        {
            Toasts.ShowFailure(deleted);
            return;
        }

        if (selected?.SystemName == plugin.SystemName)
        {
            selected = null;
        }

        Toasts.Success($"{plugin.FriendlyName} was deleted.");
        Load();
    }

    private async Task RescanAsync()
    {
        busy = true;
        var rescanned = await Manager.RescanAsync();
        busy = false;

        if (!rescanned.IsSuccess)
        {
            Toasts.ShowFailure(rescanned);
            return;
        }

        Load();
        Toasts.Info("The plugins folder was read again.");
    }

    private async Task RestartAsync()
    {
        bool confirmed = await Dialogs.ConfirmAsync(
            "Restart the site?",
            "Everyone is disconnected while it restarts.",
            "Restart",
            isDestructive: true);

        if (confirmed)
        {
            Toasts.Info("Restarting.");
            Lifetime.StopApplication();
        }
    }

    private async Task UploadAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        long maxBytes = (long)Options.Value.MaxUploadMegabytes * 1024 * 1024;

        if (file.Size > maxBytes)
        {
            Toasts.Error($"That file is larger than {Options.Value.MaxUploadMegabytes} MB.");
            return;
        }

        bool isPackage = file.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
        if (!isPackage && !file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Toasts.Error("Choose a .zip or a .nupkg file.");
            return;
        }

        if (isPackage && Packages is null)
        {
            Toasts.Error("NuGet packages are not turned on for this site.");
            return;
        }

        busy = true;
        var temp = Path.Combine(Path.GetTempPath(), $"pluginz-{Guid.NewGuid():N}.upload");

        try
        {
            // The archive readers need a seekable stream, and a browser upload is not one.
            await using (var target = File.Create(temp))
            await using (var source = file.OpenReadStream(maxBytes))
            {
                await source.CopyToAsync(target);
            }

            await using var stream = File.OpenRead(temp);
            var added = isPackage
                ? await Packages!.InstallFromFileAsync(stream, file.Name)
                : await Archives.InstallAsync(stream, file.Name);

            if (!added.IsSuccess)
            {
                Toasts.ShowFailure(added);
                return;
            }

            showUpload = false;
            uploadKey++;
            Toasts.Success($"{added.Value.FriendlyName} was added. Install it to use it.");
            Load();
        }
        finally
        {
            busy = false;
            TryDelete(temp);
        }
    }

    private Task RunAsync(Func<Task<Result<PluginInfo>>> action, string success) =>
        RunAsync(action, _ => success);

    private async Task RunAsync(Func<Task<Result<PluginInfo>>> action, Func<PluginInfo, string> success)
    {
        busy = true;
        var result = await action();
        busy = false;

        if (!result.IsSuccess)
        {
            Toasts.ShowFailure(result);
            return;
        }

        Toasts.Success(success(result.Value));
        Load();
    }

    private static List<string> Split(string text) =>
        [.. text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool HasImage(PluginInfo plugin) =>
        plugin.IconUrl is not null
        && (plugin.IconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || plugin.IconUrl.StartsWith("/", StringComparison.Ordinal));

    private static string SourceText(PluginInfo plugin) => plugin.Origin switch
    {
        PluginOrigin.NuGet => string.IsNullOrWhiteSpace(plugin.Source) ? "NuGet" : $"NuGet · {plugin.Source}",
        PluginOrigin.Archive => string.IsNullOrWhiteSpace(plugin.Source) ? "Uploaded" : $"Uploaded · {plugin.Source}",
        _ => "Folder",
    };

    private static string StatusText(PluginInfo plugin) => plugin.Status switch
    {
        PluginStatus.Active => "Active",
        PluginStatus.PendingActivation => "Starts after restart",
        PluginStatus.PendingDeactivation => "Stops after restart",
        PluginStatus.Disabled => "Disabled",
        PluginStatus.Failed => "Failed",
        PluginStatus.Incompatible => "Incompatible",
        _ => "Not installed",
    };

    private static string StatusClass(PluginInfo plugin) => plugin.Status switch
    {
        PluginStatus.Active => "nf-badge-success",
        PluginStatus.PendingActivation or PluginStatus.PendingDeactivation => "nf-badge-warning",
        PluginStatus.Failed or PluginStatus.Incompatible => "nf-badge-danger",
        _ => "nf-badge-muted",
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort. The system temp folder is cleaned up eventually.
        }
    }
}
