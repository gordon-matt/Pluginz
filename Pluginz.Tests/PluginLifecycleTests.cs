using Newtonsoft.Json;
using Pluginz.Tests.Support;

namespace Pluginz.Tests;

public class PluginLifecycleTests
{
    private const string Hello = "Pluginz.Samples.Hello";

    [Fact]
    public void Discovers_a_plugin_folder_without_loading_it()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using var host = TestHost.Create(root);

        var plugin = Assert.Single(host.Registry.GetAll());
        Assert.Equal(Hello, plugin.SystemName);
        Assert.Equal("Hello", plugin.FriendlyName);
        Assert.Equal("Samples", plugin.Group);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.Equal(PluginStatus.NotInstalled, plugin.Status);
        Assert.Empty(plugin.Problems);
        Assert.Empty(host.Registry.ActiveAssemblies);
        Assert.Null(host.Provider.GetService<IGreeter>());
    }

    [Fact]
    public void A_folder_with_no_manifest_and_no_assembly_is_not_a_plugin()
    {
        var root = TestHost.NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "Plugins", "bin"));

        using var host = TestHost.Create(root);

        Assert.Empty(host.Registry.GetAll());
    }

    [Fact]
    public async Task Install_runs_the_hook_and_waits_for_a_restart()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using var host = TestHost.Create(root);
        var result = await host.Manager.InstallAsync(Hello);

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));
        Assert.Equal(PluginStatus.PendingActivation, result.Value.Status);
        Assert.True(result.Value.RestartRequired);
        Assert.True(host.Registry.RestartRequired);
        Assert.True(File.Exists(Path.Combine(root, "App_Data", "Pluginz", "data", Hello, "installed.txt")));
        Assert.Null(host.Provider.GetService<IGreeter>());
    }

    [Fact]
    public async Task Installed_plugin_registers_services_after_restart()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using (var first = TestHost.Create(root))
        {
            Assert.True((await first.Manager.InstallAsync(Hello)).IsSuccess);
        }

        using var restarted = TestHost.Create(root);

        var plugin = restarted.Registry.Find(Hello)!;
        Assert.Equal(PluginStatus.Active, plugin.Status);
        Assert.False(restarted.Registry.RestartRequired);

        // The contract comes from the host's assembly, so the plugin's type can be cast to it.
        var greeter = restarted.Provider.GetRequiredService<IGreeter>();
        Assert.Contains("Hello, Matt!", greeter.Greet("Matt"));
        Assert.Same(restarted.Registry.ActiveAssemblies.Single(), greeter.GetType().Assembly);
    }

    [Fact]
    public async Task A_plugin_can_use_its_own_version_of_a_library()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using (var first = TestHost.Create(root))
        {
            await first.Manager.InstallAsync(Hello);
        }

        using var host = TestHost.Create(root);
        var greeter = host.Provider.GetRequiredService<IGreeter>();

        // The host has 13.x. The manifest lists Newtonsoft.Json as private, so the plugin gets the 12.x it ships.
        Assert.Equal(13, typeof(JsonConvert).Assembly.GetName().Version!.Major);
        Assert.Contains("Newtonsoft.Json 12.0.0.0", greeter.Describe());
    }

    [Fact]
    public async Task Without_a_private_declaration_the_host_copy_wins()
    {
        var root = TestHost.NewRoot();
        var folder = TestHost.DeploySample(root, Hello);

        var manifestPath = Path.Combine(folder, PluginNames.ManifestFileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"Newtonsoft.Json\"", string.Empty));

        using (var first = TestHost.Create(root))
        {
            await first.Manager.InstallAsync(Hello);
        }

        using var host = TestHost.Create(root);
        var greeter = host.Provider.GetRequiredService<IGreeter>();

        Assert.Contains("Newtonsoft.Json 13.0.0.0", greeter.Describe());
    }

    [Fact]
    public async Task Disabling_keeps_the_plugin_loaded_until_restart()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using (var first = TestHost.Create(root))
        {
            await first.Manager.InstallAsync(Hello);
        }

        using (var second = TestHost.Create(root))
        {
            var disabled = await second.Manager.DisableAsync(Hello);

            Assert.Equal(PluginStatus.PendingDeactivation, disabled.Value.Status);
            Assert.True(disabled.Value.RestartRequired);
            Assert.NotNull(second.Provider.GetService<IGreeter>());
        }

        using var third = TestHost.Create(root);
        Assert.Equal(PluginStatus.Disabled, third.Registry.Find(Hello)!.Status);
        Assert.Null(third.Provider.GetService<IGreeter>());

        var enabled = await third.Manager.EnableAsync(Hello);
        Assert.Equal(PluginStatus.PendingActivation, enabled.Value.Status);
    }

    [Fact]
    public async Task Uninstall_runs_the_hook_and_forgets_the_install()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using var host = TestHost.Create(root);
        await host.Manager.InstallAsync(Hello);
        var result = await host.Manager.UninstallAsync(Hello);

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));
        Assert.Equal(PluginStatus.NotInstalled, result.Value.Status);
        Assert.True(File.Exists(Path.Combine(root, "App_Data", "Pluginz", "data", Hello, "uninstalled.txt")));
    }

    [Fact]
    public async Task Installing_twice_and_disabling_twice_are_conflicts()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using var host = TestHost.Create(root);
        await host.Manager.InstallAsync(Hello);

        Assert.Equal(ResultStatus.Conflict, (await host.Manager.InstallAsync(Hello)).Status);

        await host.Manager.DisableAsync(Hello);
        Assert.Equal(ResultStatus.Conflict, (await host.Manager.DisableAsync(Hello)).Status);
    }

    [Fact]
    public async Task Unknown_plugins_are_not_found()
    {
        using var host = TestHost.Create();

        Assert.Equal(ResultStatus.NotFound, (await host.Manager.InstallAsync("Nope")).Status);
        Assert.Equal(ResultStatus.NotFound, (await host.Manager.DeleteAsync("Nope")).Status);
    }

    [Fact]
    public async Task Overrides_are_saved_and_survive_a_restart()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using (var first = TestHost.Create(root))
        {
            var saved = await first.Manager.UpdateAsync(new PluginForm
            {
                SystemName = Hello,
                FriendlyName = "  Greeter  ",
                DisplayOrder = 7,
                Tenants = ["a", "A", " b ", ""],
                Roles = ["Admin"],
            });

            Assert.True(saved.IsSuccess);
        }

        using var second = TestHost.Create(root);
        var plugin = second.Registry.Find(Hello)!;

        Assert.Equal("Greeter", plugin.FriendlyName);
        Assert.Equal(7, plugin.DisplayOrder);
        Assert.Equal(["a", "b"], plugin.Tenants);
        Assert.Equal(["Admin"], plugin.Roles);
    }

    [Fact]
    public async Task Delete_removes_the_folder_of_a_plugin_that_is_not_installed()
    {
        var root = TestHost.NewRoot();
        var folder = TestHost.DeploySample(root, Hello);

        using var host = TestHost.Create(root);
        await host.Manager.InstallAsync(Hello);

        Assert.Equal(ResultStatus.Conflict, (await host.Manager.DeleteAsync(Hello)).Status);

        await host.Manager.UninstallAsync(Hello);
        var deleted = await host.Manager.DeleteAsync(Hello);

        Assert.True(deleted.IsSuccess, string.Join(' ', deleted.Errors));
        Assert.False(Directory.Exists(folder));
        Assert.Null(host.Registry.Find(Hello));
    }

    [Fact]
    public async Task Rescan_finds_a_folder_copied_in_while_running()
    {
        var root = TestHost.NewRoot();
        using var host = TestHost.Create(root);
        Assert.Empty(host.Registry.GetAll());

        var changes = new List<PluginChangeKind>();
        host.Registry.Changed += (_, e) => changes.Add(e.Kind);

        TestHost.DeploySample(root, Hello);
        var rescanned = await host.Manager.RescanAsync();

        Assert.Single(rescanned.Value);
        Assert.Contains(PluginChangeKind.Discovered, changes);
    }

    [Fact]
    public async Task An_installed_plugin_that_needs_a_missing_plugin_is_not_loaded()
    {
        var root = TestHost.NewRoot();
        var folder = TestHost.DeploySample(root, Hello);

        var manifestPath = Path.Combine(folder, PluginNames.ManifestFileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"privateAssemblies\"", "\"dependencies\": [{ \"systemName\": \"Missing\" }], \"privateAssemblies\""));

        Directory.CreateDirectory(Path.Combine(root, "App_Data", "Pluginz"));
        File.WriteAllText(
            Path.Combine(root, "App_Data", "Pluginz", "plugins.json"),
            $$"""{ "plugins": { "{{Hello}}": { "installed": true, "enabled": true } } }""");

        using var host = TestHost.Create(root);
        var plugin = host.Registry.Find(Hello)!;

        Assert.Equal(PluginStatus.Failed, plugin.Status);
        Assert.Contains(plugin.Problems, p => p.Contains("Missing"));
        Assert.Null(host.Provider.GetService<IGreeter>());
    }

    [Fact]
    public async Task Install_refuses_while_a_dependency_is_missing()
    {
        var root = TestHost.NewRoot();
        var folder = TestHost.DeploySample(root, Hello);

        var manifestPath = Path.Combine(folder, PluginNames.ManifestFileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"privateAssemblies\"", "\"dependencies\": [{ \"systemName\": \"Missing\" }], \"privateAssemblies\""));

        using var host = TestHost.Create(root);
        var result = await host.Manager.InstallAsync(Hello);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(PluginStatus.NotInstalled, host.Registry.Find(Hello)!.Status);
    }

    [Fact]
    public void A_host_version_outside_the_declared_range_is_incompatible()
    {
        var root = TestHost.NewRoot();
        var folder = TestHost.DeploySample(root, Hello);

        var manifestPath = Path.Combine(folder, PluginNames.ManifestFileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"hostVersion\": \"\"", "\"hostVersion\": \"[2.0,3.0)\""));

        using var host = TestHost.Create(root);
        var plugin = host.Registry.Find(Hello)!;

        Assert.Equal(PluginStatus.Incompatible, plugin.Status);
        Assert.Contains(plugin.Problems, p => p.Contains("host version"));
    }

    [Fact]
    public void A_folder_that_does_not_match_its_system_name_is_flagged()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello, systemName: "WrongName");

        using var host = TestHost.Create(root);
        var plugin = Assert.Single(host.Registry.GetAll());

        Assert.Equal(PluginStatus.Incompatible, plugin.Status);
        Assert.Contains(plugin.Problems, p => p.Contains("must match"));
    }

    [Fact]
    public void A_folder_with_a_broken_manifest_stays_visible()
    {
        var root = TestHost.NewRoot();
        var folder = Path.Combine(root, "Plugins", "Broken");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, PluginNames.ManifestFileName), "{ not json");

        using var host = TestHost.Create(root);
        var plugin = Assert.Single(host.Registry.GetAll());

        Assert.Equal("Broken", plugin.SystemName);
        Assert.Equal(PluginStatus.Incompatible, plugin.Status);
    }

    [Theory]
    [InlineData("Good.Name-1_x", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData(".hidden", false)]
    [InlineData("trailing.", false)]
    [InlineData("has space", false)]
    public void System_names_are_safe_folder_names(string name, bool expected) =>
        Assert.Equal(expected, PluginNames.IsValidSystemName(name));
}
