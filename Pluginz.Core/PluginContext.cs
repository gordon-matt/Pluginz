namespace Pluginz;

/// <summary>What a plugin knows about itself and the host while it configures services.</summary>
public class PluginContext
{
    /// <summary>
    /// Initializes a new instance of the PluginContext class.
    /// </summary>
    /// <param name="plugin">The plugin.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="contentRootPath">The content root path.</param>
    /// <param name="environmentName">The environment name.</param>
    /// <param name="dataRoot">The data root.</param>
    public PluginContext(
        PluginInfo plugin,
        IConfiguration configuration,
        string contentRootPath,
        string environmentName,
        string dataRoot)
    {
        Plugin = plugin;
        Configuration = configuration;
        ContentRootPath = contentRootPath;
        EnvironmentName = environmentName;
        DataRoot = dataRoot;
    }

    internal string DataRoot { get; }

    /// <summary>A snapshot of this plugin's manifest and state when the context was created.</summary>
    public PluginInfo Plugin { get; }

    /// <summary>
    /// Gets the system name.
    /// </summary>
    public string SystemName => Plugin.SystemName;

    /// <summary>The host configuration.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>The host configuration section <c>Plugins:{SystemName}</c>. Use it for the plugin's own settings.</summary>
    public IConfigurationSection Section => Configuration.GetSection($"Plugins:{SystemName}");

    /// <summary>
    /// Gets the content root path.
    /// </summary>
    public string ContentRootPath { get; }

    /// <summary>
    /// Gets the environment name.
    /// </summary>
    public string EnvironmentName { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is development.
    /// </summary>
    public bool IsDevelopment => string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder the plugin was discovered in. Treat it as read-only.</summary>
    public string DirectoryPath => Plugin.DirectoryPath;

    /// <summary>
    /// A writable folder that belongs to this plugin and survives updates and uninstalls. It is created the first
    /// time you read this property.
    /// </summary>
    public string DataPath
    {
        get
        {
            string path = Path.Combine(DataRoot, "data", SystemName);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

/// <summary>A <see cref="PluginContext"/> that also exposes the host's built service provider.</summary>
public sealed class PluginRuntimeContext : PluginContext
{
    /// <summary>
    /// Initializes a new instance of the PluginRuntimeContext class.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="services">The services.</param>
    public PluginRuntimeContext(PluginContext context, IServiceProvider services)
        : base(context.Plugin, context.Configuration, context.ContentRootPath, context.EnvironmentName, context.DataRoot)
    {
        Services = services;
    }

    /// <summary>The host's service provider. Create a scope before resolving scoped services.</summary>
    public IServiceProvider Services { get; }
}