using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using AeroSwarm.Api.Core;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Models;
using AeroSwarm.Api.Options;
using AeroSwarm.Api.Services;

namespace AeroSwarm.Api.Controllers;

[ApiController]
[Route("api/drones")]
[Authorize]
public class DroneCommandController : ControllerBase
{
    private readonly ILogger<DroneCommandController> _logger;
    private readonly IHubContext<DroneHub> _hubContext;
    private readonly AppDbContext _db;
    private readonly IDroneStateService _stateService;
    private readonly SwarmOptions _opts;

    public DroneCommandController(
        ILogger<DroneCommandController> logger,
        IHubContext<DroneHub> hubContext,
        AppDbContext db,
        IDroneStateService stateService,
        IOptions<SwarmOptions> opts)
    {
        _logger = logger;
        _hubContext = hubContext;
        _db = db;
        _stateService = stateService;
        _opts = opts.Value;
    }

    [HttpPost("{droneId:int}/arm")]
    public async Task<IActionResult> Arm(int droneId)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_COMPONENT_ARM_DISARM, p1: 1.0f);
        await SendAsync(ip, port, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent MAV_CMD_COMPONENT_ARM (ARM) to Drone #{droneId}.");
        return Ok(new { message = $"ARM command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/disarm")]
    public async Task<IActionResult> Disarm(int droneId)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_COMPONENT_ARM_DISARM, p1: 0.0f);
        await SendAsync(ip, port, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent MAV_CMD_COMPONENT_ARM (DISARM) to Drone #{droneId}.");
        return Ok(new { message = $"DISARM command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/rtl")]
    public async Task<IActionResult> Rtl(int droneId)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        // SET_MODE customMode=6 (RTL) — keeps backward compat with existing firmware mapping
        var frame = MavlinkV2.EncodeSetMode((byte)droneId, baseMode: 1, customMode: 6);
        await SendAsync(ip, port, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent SET_MODE RTL to Drone #{droneId}.");
        return Ok(new { message = $"RTL command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/land")]
    public async Task<IActionResult> Land(int droneId)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeSetMode((byte)droneId, baseMode: 1, customMode: 9);
        await SendAsync(ip, port, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent SET_MODE LAND to Drone #{droneId}.");
        return Ok(new { message = $"LAND command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/takeoff")]
    public async Task<IActionResult> Takeoff(int droneId, [FromBody] TakeoffRequest req)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        var alt = req.Altitude <= 0 ? 10f : req.Altitude;

        // ArduPilot (SITL and real) requires GUIDED mode before TAKEOFF
        var modeFrame = MavlinkV2.EncodeSetMode((byte)droneId, baseMode: 1, customMode: 4);
        await SendAsync(ip, port, modeFrame);

        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_NAV_TAKEOFF, p7: alt);
        await SendAsync(ip, port, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent GUIDED + NAV_TAKEOFF alt={alt}m to Drone #{droneId}.");
        return Ok(new { message = $"TAKEOFF command sent to drone {droneId}", altitude = alt });
    }

    [HttpPost("{droneId:int}/goto")]
    public async Task<IActionResult> GoTo(int droneId, [FromBody] GotoRequest req)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        int latE7 = (int)(req.Lat * 1e7);
        int lonE7 = (int)(req.Lon * 1e7);

        // 1. Set GUIDED mode first (required for SITL and ArduPilot autopilots)
        var modeFrame = MavlinkV2.EncodeSetMode((byte)droneId, baseMode: 1, customMode: 4);
        await SendAsync(ip, port, modeFrame);

        // 2. Send position target using SET_POSITION_TARGET_GLOBAL_INT (msg 86)
        //    type_mask 0x0FF8 = position only (ignore velocity, accel, yaw)
        var posFrame = MavlinkV2.EncodeSetPositionTargetGlobalInt(
            targetSysId: (byte)droneId,
            targetCompId: MavlinkV2.AutopilotCompId,
            typeMask: 0x0FF8,
            coordinateFrame: 3, // MAV_FRAME_GLOBAL_RELATIVE_ALT
            latE7: latE7,
            lonE7: lonE7,
            alt: req.Alt);
        await SendAsync(ip, port, posFrame);

        await LogAndBroadcast(droneId, "CMD", $"Sent GOTO ({req.Lat:F5},{req.Lon:F5}) alt={req.Alt}m to Drone #{droneId}.");
        return Ok(new { message = $"GOTO sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/sethome")]
    public async Task<IActionResult> SetHome(int droneId, [FromBody] SetHomeRequest req)
    {
        if (!Resolve(droneId, out var ip, out var port)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_DO_SET_HOME,
            p1: 0, // use specified location (not current)
            p5: (float)req.Lat,
            p6: (float)req.Lon,
            p7: req.Alt);
        await SendAsync(ip, port, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent DO_SET_HOME ({req.Lat:F5},{req.Lon:F5}) to Drone #{droneId}.");
        return Ok(new { message = $"SET_HOME sent to drone {droneId}" });
    }

    private bool Resolve(int droneId, out string ip, out int port)
    {
        ip = "";
        port = 0;
        if (droneId < 1 || droneId > _opts.DroneCount) return false;
        if (_stateService.TryGetEndpoint(droneId, out ip!, out port) && port > 0)
            return true;
        // Fallback: use configured port if no live endpoint discovered yet
        if (_stateService.TryGetIp(droneId, out ip!))
        {
            port = _opts.GetPort(droneId);
            return true;
        }
        return false;
    }

    private async Task SendAsync(string ip, int port, byte[] payload)
    {
        using var udp = new UdpClient();
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse(ip), port));
    }

    private async Task LogAndBroadcast(int droneId, string eventType, string message)
    {
        _db.DroneEvents.Add(new DroneEvent
        {
            DroneId = droneId,
            EventType = eventType,
            Message = message,
            OccurredAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("ReceiveEvent", new
        {
            DroneId = droneId,
            Type = eventType,
            Message = message,
            Time = DateTime.UtcNow.ToString("HH:mm:ss"),
        });
    }
}

public record TakeoffRequest(float Altitude);
public record GotoRequest(double Lat, double Lon, float Alt);
public record SetHomeRequest(double Lat, double Lon, float Alt);
