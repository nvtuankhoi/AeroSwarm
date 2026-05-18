using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AeroSwarm.Api.Core;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Models;
using AeroSwarm.Api.Options;
using AeroSwarm.Api.Services;

namespace AeroSwarm.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MissionController : ControllerBase
{
    private readonly ILogger<MissionController> _logger;
    private readonly AppDbContext _db;
    private readonly IHubContext<DroneHub> _hub;
    private readonly IDroneStateService _stateService;
    private readonly SwarmOptions _opts;

    public MissionController(
        ILogger<MissionController> logger,
        AppDbContext db,
        IHubContext<DroneHub> hub,
        IDroneStateService stateService,
        IOptions<SwarmOptions> opts)
    {
        _logger = logger;
        _db = db;
        _hub = hub;
        _stateService = stateService;
        _opts = opts.Value;
    }

    [HttpPost("missions/swarm")]
    public async Task<IActionResult> CreateSwarmMission([FromBody] SwarmMissionRequest req)
    {
        if (req.LeaderWaypoints is null || req.LeaderWaypoints.Count == 0)
            return BadRequest("LeaderWaypoints must contain at least one item.");

        var formation = FormationFactory.Resolve(req.Formation ?? "V");
        var spacing = req.SpacingM <= 0 ? 10.0 : req.SpacingM;

        // Only target online drones; if none online, target all configured drones (still try)
        var targetIds = _stateService.All()
            .Where(t => t.IsOnline)
            .OrderBy(t => t.DroneId)
            .Select(t => t.DroneId)
            .ToList();
        if (targetIds.Count == 0)
            targetIds = _opts.DroneIds.ToList();

        var leaderWaypoints = req.LeaderWaypoints
            .Select(w => new WaypointDto(w.Lat, w.Lon, w.Alt))
            .ToList();

        var plan = MissionPlanner.PlanSwarmMission(leaderWaypoints, formation, targetIds, spacing);

        // Persist flight
        var flight = new Flight
        {
            MissionType = $"SWARM_{formation.GetType().Name.Replace("Formation", "").ToUpperInvariant()}",
            StartTime = DateTime.UtcNow,
            Status = "IN_PROGRESS",
            SpacingM = spacing,
            Formation = req.Formation ?? "V",
        };
        foreach (var (droneId, waypoints) in plan)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                flight.Waypoints.Add(new Waypoint
                {
                    DroneId = droneId,
                    Sequence = i,
                    Latitude = waypoints[i].Lat,
                    Longitude = waypoints[i].Lon,
                    Altitude = waypoints[i].Alt,
                    Status = "PENDING",
                });
            }
        }
        _db.Flights.Add(flight);
        await _db.SaveChangesAsync();

        // Upload to each drone via MAVLink (best-effort, fire-and-forget pattern with logging)
        using var udp = new UdpClient();
        int uploadedDrones = 0;
        foreach (var (droneId, waypoints) in plan)
        {
            if (!_stateService.TryGetIp(droneId, out var ip)) continue;
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), _opts.UdpPort);

            try
            {
                var countFrame = MavlinkV2.EncodeMissionCount((byte)droneId, MavlinkV2.AutopilotCompId, (ushort)waypoints.Count);
                await udp.SendAsync(countFrame, countFrame.Length, endpoint);

                for (int seq = 0; seq < waypoints.Count; seq++)
                {
                    var w = waypoints[seq];
                    var itemFrame = MavlinkV2.EncodeMissionItemInt(
                        targetSysId: (byte)droneId,
                        targetCompId: MavlinkV2.AutopilotCompId,
                        seq: (ushort)seq,
                        frame: 3, // MAV_FRAME_GLOBAL_RELATIVE_ALT
                        command: MavlinkV2.CMD_NAV_WAYPOINT,
                        current: (byte)(seq == 0 ? 1 : 0),
                        autocontinue: 1,
                        p1: 0, p2: 0, p3: 0, p4: 0,
                        latE7: (int)(w.Lat * 1e7),
                        lonE7: (int)(w.Lon * 1e7),
                        alt: w.Alt);
                    await udp.SendAsync(itemFrame, itemFrame.Length, endpoint);
                    await Task.Delay(20); // small spacing to avoid drone UDP buffer overrun
                }
                uploadedDrones++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mission upload failed for drone {Id}", droneId);
            }
        }

        await _hub.Clients.All.SendAsync("ReceiveEvent", new
        {
            DroneId = 0,
            Type = "SYS",
            Message = $"Swarm mission #{flight.Id} uploaded to {uploadedDrones} drone(s) ({req.Formation}, spacing {spacing}m).",
            Time = DateTime.UtcNow.ToString("HH:mm:ss"),
        });

        return Ok(new
        {
            flightId = flight.Id,
            formation = req.Formation,
            spacingM = spacing,
            droneIds = targetIds,
            uploadedDrones,
            waypointsPerDrone = leaderWaypoints.Count,
        });
    }

    [HttpGet("flights")]
    public async Task<IActionResult> ListFlights([FromQuery] int limit = 50)
    {
        var flights = await _db.Flights
            .OrderByDescending(f => f.StartTime)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(f => new
            {
                f.Id,
                f.MissionType,
                f.Formation,
                f.SpacingM,
                f.StartTime,
                f.EndTime,
                f.Status,
                WaypointCount = f.Waypoints.Count,
            })
            .ToListAsync();
        return Ok(flights);
    }

    [HttpGet("flights/{id:int}")]
    public async Task<IActionResult> GetFlight(int id)
    {
        var flight = await _db.Flights
            .Include(f => f.Waypoints)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (flight is null) return NotFound();
        return Ok(flight);
    }
}

public record SwarmMissionRequest(string Formation, double SpacingM, List<LeaderWaypoint> LeaderWaypoints);
public record LeaderWaypoint(double Lat, double Lon, float Alt);
