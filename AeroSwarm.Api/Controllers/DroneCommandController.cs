using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Models;

namespace AeroSwarm.Api.Controllers;

[ApiController]
[Route("api/drones")]
[Authorize]
public class DroneCommandController : ControllerBase
{
    private readonly ILogger<DroneCommandController> _logger;
    private readonly IConfiguration _config;
    private readonly IHubContext<DroneHub> _hubContext;
    private readonly AppDbContext _db;

    private static readonly Dictionary<int, string> DroneIpMap =
        Enumerable.Range(1, 5).ToDictionary(id => id, id => $"10.105.151.{100 + id}");

    public DroneCommandController(
        ILogger<DroneCommandController> logger,
        IConfiguration config,
        IHubContext<DroneHub> hubContext,
        AppDbContext db)
    {
        _logger = logger;
        _config = config;
        _hubContext = hubContext;
        _db = db;
    }

    [HttpPost("{droneId:int}/arm")]
    public async Task<IActionResult> Arm(int droneId)
    {
        if (!DroneIpMap.TryGetValue(droneId, out var ip))
            return BadRequest("Invalid drone ID");

        var payload = BuildCommandLong(droneId, 400, param1: 1.0f);
        await SendUdpCommand(ip, payload);
        await LogAndBroadcast(droneId, "CMD", $"Sent MAV_CMD_COMPONENT_ARM to Drone #{droneId}.");
        _logger.LogInformation("ARM → Drone #{Id} @ {IP}", droneId, ip);
        return Ok(new { message = $"ARM command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/disarm")]
    public async Task<IActionResult> Disarm(int droneId)
    {
        if (!DroneIpMap.TryGetValue(droneId, out var ip))
            return BadRequest("Invalid drone ID");

        var payload = BuildCommandLong(droneId, 400, param1: 0.0f);
        await SendUdpCommand(ip, payload);
        await LogAndBroadcast(droneId, "CMD", $"Sent MAV_CMD_COMPONENT_ARM_DISARM (DISARM) to Drone #{droneId}.");
        return Ok(new { message = $"DISARM command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/rtl")]
    public async Task<IActionResult> Rtl(int droneId)
    {
        if (!DroneIpMap.TryGetValue(droneId, out var ip))
            return BadRequest("Invalid drone ID");

        var payload = BuildSetMode(droneId, customMode: 6);
        await SendUdpCommand(ip, payload);
        await LogAndBroadcast(droneId, "CMD", $"Sent SET_MODE RTL to Drone #{droneId}.");
        return Ok(new { message = $"RTL command sent to drone {droneId}" });
    }

    [HttpPost("{droneId:int}/land")]
    public async Task<IActionResult> Land(int droneId)
    {
        if (!DroneIpMap.TryGetValue(droneId, out var ip))
            return BadRequest("Invalid drone ID");

        var payload = BuildSetMode(droneId, customMode: 9);
        await SendUdpCommand(ip, payload);
        await LogAndBroadcast(droneId, "CMD", $"Sent SET_MODE LAND to Drone #{droneId}.");
        return Ok(new { message = $"LAND command sent to drone {droneId}" });
    }

    private async Task SendUdpCommand(string ip, byte[] payload)
    {
        using var udp = new UdpClient();
        await udp.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse(ip), 14550));
    }

    private static byte[] BuildCommandLong(int droneId, ushort command,
        float param1 = 0, float param2 = 0, float param3 = 0,
        float param4 = 0, float param5 = 0, float param6 = 0, float param7 = 0)
    {
        var payload = new byte[33];
        int o = 0;
        foreach (var p in new[] { param1, param2, param3, param4, param5, param6, param7 })
        { BitConverter.GetBytes(p).CopyTo(payload, o); o += 4; }
        BitConverter.GetBytes(command).CopyTo(payload, o); o += 2;
        payload[o++] = (byte)droneId;
        payload[o++] = 1;
        payload[o] = 0;
        return WrapMavlink2(76, 255, 190, payload);
    }

    private static byte[] BuildSetMode(int droneId, uint customMode)
    {
        var payload = new byte[6];
        BitConverter.GetBytes(customMode).CopyTo(payload, 0);
        payload[4] = (byte)droneId;
        payload[5] = 1;
        return WrapMavlink2(11, 255, 190, payload);
    }

    private static byte[] WrapMavlink2(uint msgId, byte sysId, byte compId, byte[] payload)
    {
        int frameLen = 10 + payload.Length + 2;
        var frame = new byte[frameLen];
        frame[0] = 0xFD;
        frame[1] = (byte)payload.Length;
        frame[2] = 0; frame[3] = 0; frame[4] = 0;
        frame[5] = sysId; frame[6] = compId;
        frame[7] = (byte)(msgId & 0xFF);
        frame[8] = (byte)((msgId >> 8) & 0xFF);
        frame[9] = (byte)((msgId >> 16) & 0xFF);
        payload.CopyTo(frame, 10);

        ushort crc = 0xFFFF;
        for (int i = 1; i < 10 + payload.Length; i++)
        {
            byte tmp = (byte)(frame[i] ^ (byte)(crc & 0xFF));
            tmp ^= (byte)(tmp << 4);
            crc = (ushort)((crc >> 8) ^ (tmp << 8) ^ (tmp << 3) ^ (tmp >> 4));
        }
        frame[frameLen - 2] = (byte)(crc & 0xFF);
        frame[frameLen - 1] = (byte)((crc >> 8) & 0xFF);
        return frame;
    }

    private async Task LogAndBroadcast(int droneId, string eventType, string message)
    {
        _db.DroneEvents.Add(new DroneEvent
        {
            DroneId = droneId,
            EventType = eventType,
            Message = message,
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("ReceiveEvent", new
        {
            DroneId = droneId,
            Type = eventType,
            Message = message,
            Time = DateTime.UtcNow.ToString("HH:mm:ss")
        });
    }
}
