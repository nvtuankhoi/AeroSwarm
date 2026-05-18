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
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_COMPONENT_ARM_DISARM, p1: 1.0f);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent MAV_CMD_COMPONENT_ARM (ARM) to Drone #{droneId}.");
        return Ok(new { message = $"ARM command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/disarm")]
    public async Task<IActionResult> Disarm(int droneId)
    {
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_COMPONENT_ARM_DISARM, p1: 0.0f);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent MAV_CMD_COMPONENT_ARM (DISARM) to Drone #{droneId}.");
        return Ok(new { message = $"DISARM command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/rtl")]
    public async Task<IActionResult> Rtl(int droneId)
    {
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        // SET_MODE customMode=6 (RTL) — keeps backward compat with existing firmware mapping
        var frame = MavlinkV2.EncodeSetMode((byte)droneId, baseMode: 1, customMode: 6);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent SET_MODE RTL to Drone #{droneId}.");
        return Ok(new { message = $"RTL command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/land")]
    public async Task<IActionResult> Land(int droneId)
    {
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeSetMode((byte)droneId, baseMode: 1, customMode: 9);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent SET_MODE LAND to Drone #{droneId}.");
        return Ok(new { message = $"LAND command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/takeoff")]
    public async Task<IActionResult> Takeoff(int droneId, [FromBody] TakeoffRequest req)
    {
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        var alt = req.Altitude <= 0 ? 10f : req.Altitude;
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_NAV_TAKEOFF, p7: alt);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent NAV_TAKEOFF alt={alt}m to Drone #{droneId}.");
        return Ok(new { message = $"TAKEOFF command sent to drone {droneId}", altitude = alt });
    }

    [HttpPost("{droneId:int}/goto")]
    public async Task<IActionResult> GoTo(int droneId, [FromBody] GotoRequest req)
    {
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        int latE7 = (int)(req.Lat * 1e7);
        int lonE7 = (int)(req.Lon * 1e7);
        var frame = MavlinkV2.EncodeMissionItemInt(
            targetSysId: (byte)droneId,
            targetCompId: MavlinkV2.AutopilotCompId,
            seq: 0,
            frame: 3, // MAV_FRAME_GLOBAL_RELATIVE_ALT
            command: MavlinkV2.CMD_NAV_WAYPOINT,
            current: 2, // GUIDED-mode "fly to"
            autocontinue: 1,
            p1: 0, p2: 0, p3: 0, p4: 0,
            latE7: latE7, lonE7: lonE7, alt: req.Alt);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent GOTO ({req.Lat:F5},{req.Lon:F5}) alt={req.Alt}m to Drone #{droneId}.");
        return Ok(new { message = $"GOTO sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/sethome")]
    public async Task<IActionResult> SetHome(int droneId, [FromBody] SetHomeRequest req)
    {
        if (!Resolve(droneId, out var ip)) return BadRequest("Invalid drone ID");
        var frame = MavlinkV2.EncodeCommandLong((byte)droneId, MavlinkV2.AutopilotCompId,
            MavlinkV2.CMD_DO_SET_HOME,
            p1: 0, // use specified location (not current)
            p5: (float)req.Lat,
            p6: (float)req.Lon,
            p7: req.Alt);
        await SendAsync(ip, frame);
        await LogAndBroadcast(droneId, "CMD", $"Sent DO_SET_HOME ({req.Lat:F5},{req.Lon:F5}) to Drone #{droneId}.");
        return Ok(new { message = $"SET_HOME sent to drone {droneId}" });
    }

    private bool Resolve(int droneId, out string ip)
    {
        ip = "";
        if (droneId < 1 || droneId > _opts.DroneCount) return false;
        return _stateService.TryGetIp(droneId, out ip);
    }

    private async Task SendAsync(string ip, byte[] payload)
    {
        using var udp = new UdpClient();
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse(ip), _opts.UdpPort));
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
