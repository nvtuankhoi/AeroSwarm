using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AeroSwarm.Api.Options;

namespace AeroSwarm.Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly SwarmOptions _opts;

    public ConfigController(IOptions<SwarmOptions> opts) => _opts = opts.Value;

    /// <summary>Returns swarm configuration for clients (drone count + IDs).</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new
    {
        DroneCount = _opts.DroneCount,
        DroneIds = _opts.DroneIds.ToArray(),
        UdpPort = _opts.UdpPort,
        UdpPorts = _opts.UdpPorts.ToArray(),
        DropoutThresholdSec = _opts.DropoutThresholdSec,
        LowVoltage = _opts.LowVoltagePerCell * _opts.LiPoCellCount,
        CriticalVoltage = _opts.CriticalVoltagePerCell * _opts.LiPoCellCount,
    });
}
