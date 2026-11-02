using Microsoft.AspNetCore.Mvc;

namespace Pluginz.Samples.Web;

/// <summary>An MVC controller in a plugin. The host picks it up through <c>AddPluginzParts()</c>.</summary>
[ApiController]
[Route("samples/web/api/time")]
public sealed class SampleController(SampleClock clock) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { utc = clock.UtcNow, from = "controller" });
}
