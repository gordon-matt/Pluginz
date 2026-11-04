using Heroicsoft.NetForge.Abstractions.Auth;
using Heroicsoft.NetForge.Components;
using Heroicsoft.NetForge.Components.Layout;
using Pluginz;
using Pluginz.Components;
using Pluginz.Components.Pages;
using Pluginz.Demo;
using Pluginz.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// Plugins are discovered and loaded right here, because only now can they still add services. Settings come from the
// "Pluginz" section of appsettings.json.
builder.AddPluginz();
builder.Services.AddControllers().AddPluginzParts();
builder.Services.AddPluginzWeb();
builder.Services.AddPluginzNuGet();
builder.Services.AddPluginzComponents();
builder.Services.AddSingleton<IAuthProviderService, DemoAuth>();

builder.Services.Configure<ForgeShellOptions>(shell =>
{
    shell.Brand = "Pluginz";
    shell.LogoUrl = "img/Logo.png";
    shell.MarkUrl = "img/Icon.png";
    shell.UseSharedMenu = true;
    shell.ShowUserMenu = false;
});

builder.Services.BuildMenu(menu =>
{
    menu.AddItems().AddCustom(item =>
    {
        item.Title = "Home";
        item.Url = "/";
        item.Icon = "home";
        item.MatchAll = true;
    });
    menu.IncludeRegistered();
});

var app = builder.Build();

app.UseStaticFiles();
app.MapStaticAssets();
app.UsePluginz();
app.UseAntiforgery();
app.MapControllers();
app.MapPluginz();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Plugins).Assembly)
    .AddPluginzAssemblies(app.Services)
    .WithStaticAssets();

app.Run();