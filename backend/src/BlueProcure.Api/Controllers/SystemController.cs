using System.Reflection;
using BlueProcure.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BlueProcure.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(IClock clock, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Public build/runtime information, used by the web app to verify API connectivity.</summary>
    [HttpGet("info")]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> GetInfo()
    {
        var version = typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        return new SystemInfoResponse("BlueProcure", version, environment.EnvironmentName, clock.UtcNow, clock.TodayLocal);
    }
}

public sealed record SystemInfoResponse(string Name, string Version, string Environment, DateTimeOffset ServerTimeUtc, DateOnly TodayLocal);
