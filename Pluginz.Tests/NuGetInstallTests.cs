using Pluginz.Packages;
using Pluginz.Tests.Support;

namespace Pluginz.Tests;

public class NuGetInstallTests
{
    private const string Hello = "Pluginz.Samples.Hello";

    private static TestHost CreateHost(TestFeed feed, string? root = null, Action<PluginzOptions>? configure = null) =>
        TestHost.Create(
            root,
            configure,
            services => services.AddPluginzNuGet(options =>
            {
                options.TagFilter = null;
                options.Sources = [new PluginPackageSource { Name = "test", Url = feed.Path }];
            }));

    [Fact]
    public async Task Installs_a_plugin_with_the_dependencies_the_host_lacks()
    {
        var feed = new TestFeed();
        feed.AddHello();
        feed.AddNewtonsoft();

        var root = TestHost.NewRoot();
        using (var host = CreateHost(feed, root))
        {
            var packages = host.Provider.GetRequiredService<IPluginPackageService>();
            var added = await packages.InstallAsync(Hello);

            Assert.True(added.IsSuccess, string.Join(' ', added.Errors));
            Assert.Equal(PluginStatus.NotInstalled, added.Value.Status);
            Assert.Equal(PluginOrigin.NuGet, added.Value.Origin);
            Assert.Equal(Hello, added.Value.PackageId);

            // Newtonsoft.Json is private to this plugin, so it is copied. Pluginz.Core comes from the host.
            var folder = Path.Combine(root, "Plugins", Hello);
            Assert.True(File.Exists(Path.Combine(folder, "Newtonsoft.Json.dll")));
            Assert.False(File.Exists(Path.Combine(folder, "Pluginz.Core.dll")));
            Assert.True(File.Exists(Path.Combine(folder, "plugin.json")));

            Assert.True((await host.Manager.InstallAsync(Hello)).IsSuccess);
        }

        using var restarted = CreateHost(feed, root);
        Assert.Equal(PluginStatus.Active, restarted.Registry.Find(Hello)!.Status);

        var greeter = restarted.Provider.GetRequiredService<IGreeter>();
        Assert.Contains("Newtonsoft.Json 12.0.0.0", greeter.Describe());
    }

    [Fact]
    public async Task Picks_the_latest_stable_version_unless_prerelease_is_allowed()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello("1.0.0");
        feed.AddHello("1.1.0");
        feed.AddHello("2.0.0-beta.1");

        using var host = CreateHost(feed);
        var packages = host.Provider.GetRequiredService<IPluginPackageService>();

        var versions = await packages.GetVersionsAsync(Hello, includePrerelease: true);
        Assert.Equal(["2.0.0-beta.1", "1.1.0", "1.0.0"], versions.Value);

        var stable = await packages.InstallAsync(Hello);
        Assert.Equal("1.1.0", stable.Value.Version);

        var prerelease = await packages.InstallAsync(Hello, includePrerelease: true);
        Assert.Equal("2.0.0-beta.1", prerelease.Value.PendingVersion ?? prerelease.Value.Version);
    }

    [Fact]
    public async Task The_tag_filter_hides_packages_that_are_not_plugins()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello();

        using var host = TestHost.Create(
            register: services => services.AddPluginzNuGet(options =>
            {
                options.TagFilter = "pluginz";
                options.Sources = [new PluginPackageSource { Name = "test", Url = feed.Path }];
            }));

        var packages = host.Provider.GetRequiredService<IPluginPackageService>();
        var found = await packages.SearchAsync(null);

        Assert.True(found.IsSuccess, string.Join(' ', found.Errors));
        Assert.Equal([Hello], found.Value.Select(package => package.PackageId));
    }

    [Fact]
    public async Task Searching_lists_the_newest_version_of_a_package()
    {
        var feed = new TestFeed();
        feed.AddHello("1.0.0");
        feed.AddHello("1.1.0");
        feed.AddHello("2.0.0-beta");

        using var host = CreateHost(feed);
        var packages = host.Provider.GetRequiredService<IPluginPackageService>();

        var stable = await packages.SearchAsync(null);
        var preview = await packages.SearchAsync(null, includePrerelease: true);

        Assert.Equal("1.1.0", Assert.Single(stable.Value).Version);
        Assert.Equal("2.0.0-beta", Assert.Single(preview.Value).Version);
    }

    [Fact]
    public async Task A_relative_feed_folder_is_resolved_against_the_content_root()
    {
        var root = TestHost.NewRoot();
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello();
        TestHost.CopyDirectory(feed.Path, Path.Combine(root, "feed"));

        using var host = TestHost.Create(
            root,
            register: services => services.AddPluginzNuGet(options =>
            {
                options.TagFilter = null;
                options.Sources = [new PluginPackageSource { Name = "relative", Url = "feed" }];
            }));

        var packages = host.Provider.GetRequiredService<IPluginPackageService>();
        var installed = await packages.InstallAsync(Hello);

        Assert.True(installed.IsSuccess, string.Join(' ', installed.Errors));
    }

    [Fact]
    public async Task A_plugin_that_was_not_installed_from_a_package_is_marked_installed_by_its_system_name()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello();

        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using var host = TestHost.Create(
            root,
            register: services => services.AddPluginzNuGet(options =>
            {
                options.TagFilter = null;
                options.Sources = [new PluginPackageSource { Name = "test", Url = feed.Path }];
            }));

        var packages = host.Provider.GetRequiredService<IPluginPackageService>();
        var found = await packages.SearchAsync("Hello");

        Assert.Equal(Hello, found.Value.Single(package => package.PackageId == Hello).InstalledSystemName);
    }

    [Fact]
    public async Task Searching_marks_installed_packages()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello();

        using var host = CreateHost(feed);
        var packages = host.Provider.GetRequiredService<IPluginPackageService>();

        var before = await packages.SearchAsync("Hello");
        Assert.True(before.IsSuccess, string.Join(' ', before.Errors));
        var hello = Assert.Single(before.Value, p => p.PackageId == Hello);
        Assert.Null(hello.InstalledSystemName);

        await packages.InstallAsync(Hello);

        var after = await packages.SearchAsync("Hello");
        Assert.Equal(Hello, after.Value.Single(p => p.PackageId == Hello).InstalledSystemName);
    }

    [Fact]
    public async Task Reports_updates_for_packages_that_came_from_nuget()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello("1.0.0");

        using var host = CreateHost(feed);
        var packages = host.Provider.GetRequiredService<IPluginPackageService>();

        await packages.InstallAsync(Hello);
        Assert.Empty((await packages.GetUpdatesAsync()).Value);

        feed.AddHello("1.2.0");
        var updates = (await packages.GetUpdatesAsync()).Value;

        var update = Assert.Single(updates);
        Assert.Equal("1.0.0", update.CurrentVersion);
        Assert.Equal("1.2.0", update.LatestVersion);
    }

    [Fact]
    public async Task A_missing_dependency_fails_the_install_and_leaves_nothing_behind()
    {
        var feed = new TestFeed();
        feed.AddHello();

        var root = TestHost.NewRoot();
        using var host = CreateHost(feed, root);
        var packages = host.Provider.GetRequiredService<IPluginPackageService>();

        var result = await packages.InstallAsync(Hello);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Contains("Newtonsoft.Json"));
        Assert.Empty(host.Registry.GetAll());
        Assert.False(Directory.Exists(Path.Combine(root, "Plugins", Hello)));
    }

    [Fact]
    public async Task Dependencies_that_the_host_provides_are_not_downloaded()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();

        // Microsoft.Extensions.Logging.Abstractions is not in this feed. The host has it, so it must not be needed.
        feed.AddHello(dependencies: [("Newtonsoft.Json", "12.0.3"), ("Microsoft.Extensions.Logging.Abstractions", "10.0.0")]);

        using var host = CreateHost(feed);
        var result = await host.Provider.GetRequiredService<IPluginPackageService>().InstallAsync(Hello);

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));
    }

    [Fact]
    public async Task Installs_from_an_uploaded_nupkg_file()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        var package = feed.AddHello();

        using var host = CreateHost(feed);
        await using var stream = File.OpenRead(package);

        var result = await host.Provider.GetRequiredService<IPluginPackageService>().InstallFromFileAsync(stream, "hello.nupkg");

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));
        Assert.Equal("hello.nupkg", result.Value.Source);
    }

    [Fact]
    public async Task A_file_that_is_not_a_package_is_rejected()
    {
        var feed = new TestFeed();
        using var host = CreateHost(feed);
        await using var stream = new MemoryStream("this is not a zip"u8.ToArray());

        var result = await host.Provider.GetRequiredService<IPluginPackageService>().InstallFromFileAsync(stream, "junk.nupkg");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task A_package_cannot_write_outside_the_plugin_folder()
    {
        var feed = new TestFeed();
        feed.Add(
            "Evil.Plugin",
            "1.0.0",
            new Dictionary<string, byte[]>
            {
                ["lib/net10.0/Evil.Plugin.dll"] = [1, 2, 3],
                ["lib/net10.0/../../../../escaped.txt"] = [1],
            });

        var root = TestHost.NewRoot();
        using var host = CreateHost(feed, root);
        var result = await host.Provider.GetRequiredService<IPluginPackageService>().InstallAsync("Evil.Plugin");

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escaped.txt")));
    }

    [Fact]
    public async Task The_prefix_allow_list_applies_to_packages()
    {
        var feed = new TestFeed();
        feed.AddNewtonsoft();
        feed.AddHello();

        using var host = CreateHost(feed, configure: options => options.AllowedPrefixes = ["Acme."]);
        var result = await host.Provider.GetRequiredService<IPluginPackageService>().InstallAsync(Hello);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("a b")]
    [InlineData("")]
    public async Task Invalid_package_ids_are_rejected(string id)
    {
        using var host = CreateHost(new TestFeed());
        var result = await host.Provider.GetRequiredService<IPluginPackageService>().InstallAsync(id);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}
