using Pluginz.Tests.Support;

namespace Pluginz.Tests;

public class ArchiveInstallTests
{
    private const string Hello = "Pluginz.Samples.Hello";

    private static MemoryStream ZipOf(string directory, string? rootFolder = null, Func<string, byte[], byte[]>? edit = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                var bytes = File.ReadAllBytes(file);
                bytes = edit?.Invoke(relative, bytes) ?? bytes;

                var entry = zip.CreateEntry(rootFolder is null ? relative : $"{rootFolder}/{relative}");
                using var target = entry.Open();
                target.Write(bytes);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Uploading_a_zip_adds_the_plugin_without_installing_it()
    {
        using var host = TestHost.Create();
        var installer = host.Provider.GetRequiredService<IPluginArchiveInstaller>();

        await using var zip = ZipOf(TestHost.SampleOutput(Hello));
        var result = await installer.InstallAsync(zip, "hello.zip");

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));
        Assert.Equal(PluginStatus.NotInstalled, result.Value.Status);
        Assert.Equal(PluginOrigin.Archive, result.Value.Origin);
        Assert.Equal("hello.zip", result.Value.Source);
        Assert.True(Directory.Exists(Path.Combine(host.PluginsPath, Hello)));
    }

    [Fact]
    public async Task A_zip_with_one_top_level_folder_is_accepted()
    {
        using var host = TestHost.Create();
        var installer = host.Provider.GetRequiredService<IPluginArchiveInstaller>();

        await using var zip = ZipOf(TestHost.SampleOutput(Hello), rootFolder: "Some Folder Name");
        var result = await installer.InstallAsync(zip, "hello.zip");

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));
        Assert.Equal(Hello, result.Value.SystemName);
    }

    [Fact]
    public async Task A_zip_entry_that_escapes_the_staging_folder_is_rejected()
    {
        var root = TestHost.NewRoot();
        using var host = TestHost.Create(root);
        var installer = host.Provider.GetRequiredService<IPluginArchiveInstaller>();

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = zip.CreateEntry("plugin.json").Open())
            {
                entry.Write("{ \"systemName\": \"Evil\" }"u8);
            }

            using (var entry = zip.CreateEntry("../../../escaped.txt").Open())
            {
                entry.Write("x"u8);
            }
        }

        stream.Position = 0;
        var result = await installer.InstallAsync(stream, "evil.zip");

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains(result.ValidationErrors, e => e.ErrorMessage.Contains("unsafe"));
        Assert.Empty(host.Registry.GetAll());
    }

    [Fact]
    public async Task A_zip_without_a_manifest_is_rejected()
    {
        using var host = TestHost.Create();
        var installer = host.Provider.GetRequiredService<IPluginArchiveInstaller>();

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("readme.txt").Open();
            entry.Write("nothing here"u8);
        }

        stream.Position = 0;
        var result = await installer.InstallAsync(stream, "empty.zip");

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Upload_can_be_turned_off()
    {
        using var host = TestHost.Create(configure: options => options.AllowUpload = false);
        var installer = host.Provider.GetRequiredService<IPluginArchiveInstaller>();

        await using var zip = ZipOf(TestHost.SampleOutput(Hello));
        var result = await installer.InstallAsync(zip, "hello.zip");

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task Uploading_a_newer_version_of_a_loaded_plugin_waits_for_a_restart()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using (var first = TestHost.Create(root))
        {
            await first.Manager.InstallAsync(Hello);
        }

        using var host = TestHost.Create(root);
        Assert.Equal(PluginStatus.Active, host.Registry.Find(Hello)!.Status);

        await using var zip = ZipOf(TestHost.SampleOutput(Hello), edit: (name, bytes) =>
            name == PluginNames.ManifestFileName
                ? System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("\"version\": \"1.0.0\"", "\"version\": \"1.1.0\""))
                : bytes);

        var result = await host.Provider.GetRequiredService<IPluginArchiveInstaller>().InstallAsync(zip, "hello-1.1.zip");

        Assert.True(result.IsSuccess, string.Join(' ', result.Errors));

        var plugin = host.Registry.Find(Hello)!;
        Assert.Equal(PluginStatus.Active, plugin.Status);
        Assert.Equal("1.1.0", plugin.Version);
        Assert.Equal("1.0.0", plugin.LoadedVersion);
        Assert.True(plugin.RestartRequired);

        // The running copy is untouched and the new version loads on the next start.
        using var restarted = TestHost.Create(root);
        Assert.Equal("1.1.0", restarted.Registry.Find(Hello)!.Version);
        Assert.Null(restarted.Registry.Find(Hello)!.LoadedVersion);
        Assert.NotNull(restarted.Provider.GetService<IGreeter>());
    }

    [Fact]
    public async Task Without_shadow_copy_an_update_is_staged_and_applied_at_the_next_start()
    {
        var root = TestHost.NewRoot();
        TestHost.DeploySample(root, Hello);

        using (var first = TestHost.Create(root, options => options.ShadowCopy = false))
        {
            await first.Manager.InstallAsync(Hello);
        }

        using (var host = TestHost.Create(root, options => options.ShadowCopy = false))
        {
            await using var zip = ZipOf(TestHost.SampleOutput(Hello), edit: (name, bytes) =>
                name == PluginNames.ManifestFileName
                    ? System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("\"version\": \"1.0.0\"", "\"version\": \"3.0.0\""))
                    : bytes);

            var result = await host.Provider.GetRequiredService<IPluginArchiveInstaller>().InstallAsync(zip, "hello-3.zip");
            Assert.True(result.IsSuccess, string.Join(' ', result.Errors));

            var plugin = host.Registry.Find(Hello)!;
            Assert.Equal("1.0.0", plugin.Version);
            Assert.Equal("3.0.0", plugin.PendingVersion);
            Assert.True(plugin.RestartRequired);
        }

        // Nothing is loaded yet at this point of the second start, so the staged folder is moved into place first.
        using var restarted = TestHost.Create(root, options => options.ShadowCopy = false);
        var after = restarted.Registry.Find(Hello)!;

        Assert.Equal("3.0.0", after.Version);
        Assert.Null(after.PendingVersion);
    }
}
