using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pluginz.Tests.Support;

/// <summary>
/// A host in a temporary folder. Create one, then create another over the same folder to simulate a restart: plugins
/// load only when <c>AddPluginz</c> runs.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly bool ownsRoot;

    private TestHost(string root, bool ownsRoot, Action<PluginzOptions>? configure, Action<IServiceCollection>? register)
    {
        Root = root;
        this.ownsRoot = ownsRoot;

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
        services.AddPluginz(
            new ConfigurationBuilder().Build(),
            new TestEnvironment(root),
            options =>
            {
                options.HostVersion = "1.0.0";
                configure?.Invoke(options);
            });

        register?.Invoke(services);

        Services = services;
        Provider = services.BuildServiceProvider();
    }

    public string Root { get; }

    public string PluginsPath => Path.Combine(Root, "Plugins");

    public IServiceCollection Services { get; }

    public ServiceProvider Provider { get; }

    public IPluginRegistry Registry => Provider.GetRequiredService<IPluginRegistry>();

    public IPluginManager Manager => Provider.GetRequiredService<IPluginManager>();

    public static TestHost Create(string? root = null, Action<PluginzOptions>? configure = null, Action<IServiceCollection>? register = null) =>
        new(root ?? NewRoot(), root is null, configure, register);

    public static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pluginz-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Copies the built output of a sample plugin into a plugins folder, as a deployment would.</summary>
    public static string DeploySample(string root, string project, string? systemName = null)
    {
        var metadata = typeof(TestHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        var source = Path.Combine(metadata["SamplesRoot"]!, project, "bin", metadata["Configuration"]!, "net10.0");
        var target = Path.Combine(root, "Plugins", systemName ?? project);

        CopyDirectory(source, target);
        return target;
    }

    public static string SampleOutput(string project)
    {
        var metadata = typeof(TestHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value);
        return Path.Combine(metadata["SamplesRoot"]!, project, "bin", metadata["Configuration"]!, "net10.0");
    }

    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    public void Dispose()
    {
        Provider.Dispose();

        if (!ownsRoot)
        {
            // The test passed the folder in, so the test decides when it goes away.
            return;
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loaded plugin assemblies stay locked until the process exits. The temp folder is harmless.
        }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "Pluginz.Tests";

        public string ContentRootPath { get; set; } = root;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
