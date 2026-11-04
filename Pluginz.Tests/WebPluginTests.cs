using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Pluginz.Tests.Support;
using Pluginz.Web;

namespace Pluginz.Tests;

public class WebPluginTests
{
    private const string WebSample = "Pluginz.Samples.Web";

    /// <summary>Deploys and installs the web sample in a temporary folder, then starts a web host over it, as a restart would.</summary>
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(bool install = true)
    {
        string root = TestHost.NewRoot();
        TestHost.DeploySample(root, WebSample);

        if (install)
        {
            using var setup = TestHost.Create(root);
            var installed = await setup.Manager.InstallAsync(WebSample);
            Assert.True(installed.IsSuccess, string.Join(' ', installed.Errors));
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddPluginz(options => options.HostVersion = "1.0.0");
        builder.Services.AddControllers().AddPluginzParts();
        builder.Services.AddPluginzWeb();

        var app = builder.Build();
        app.UseStaticFiles();
        app.UsePluginz();
        app.MapControllers();
        app.MapPluginz();

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task An_active_web_plugin_maps_its_endpoints()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync("/samples/web/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WebSample, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_controllers_of_an_active_plugin_are_part_of_the_application()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync("/samples/web/api/time");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/_content/Pluginz.Samples.Web/sample.css", "text/css")]
    [InlineData("/plugins/Pluginz.Samples.Web/sample.svg", "image/svg+xml")]
    public async Task The_static_files_of_an_active_plugin_are_served(string url, string contentType)
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_plugin_that_is_not_installed_contributes_nothing()
    {
        var (app, client) = await StartAsync(install: false);
        await using var _ = app;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/samples/web/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/samples/web/api/time")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/_content/Pluginz.Samples.Web/sample.css")).StatusCode);
        Assert.Empty(app.Services.GetRequiredService<IPluginWebAssemblies>().Assemblies);
    }

    [Fact]
    public async Task The_router_is_given_the_assemblies_of_active_plugins()
    {
        var (app, _) = await StartAsync();
        await using var _ = app;

        var assemblies = app.Services.GetRequiredService<IPluginWebAssemblies>().Assemblies;

        Assert.Equal(WebSample, Assert.Single(assemblies).GetName().Name);
        Assert.True(app.Services.GetRequiredService<IPluginRegistry>().IsActive(WebSample));
    }
}