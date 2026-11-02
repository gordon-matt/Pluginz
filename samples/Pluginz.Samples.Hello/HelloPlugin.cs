using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Pluginz.Samples.Hello;

public sealed class HelloPlugin : PluginBase
{
    public static string? LastHook { get; private set; }

    public override void ConfigureServices(IServiceCollection services, PluginContext context) =>
        services.AddSingleton<IGreeter, HelloGreeter>();

    public override Task InstallAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default)
    {
        // Leave a trace in the plugin's own data folder so that tests can see the hook ran.
        File.WriteAllText(Path.Combine(context.DataPath, "installed.txt"), DateTime.UtcNow.ToString("O"));
        LastHook = "install";
        return Task.CompletedTask;
    }

    public override Task UninstallAsync(PluginRuntimeContext context, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(Path.Combine(context.DataPath, "uninstalled.txt"), DateTime.UtcNow.ToString("O"));
        LastHook = "uninstall";
        return Task.CompletedTask;
    }
}

internal sealed class HelloGreeter : IGreeter
{
    public string Greet(string name) => JsonConvert.SerializeObject(new { message = $"Hello, {name}!" });

    public string Describe() => $"Hello plugin, Newtonsoft.Json {typeof(JsonConvert).Assembly.GetName().Version}";
}
