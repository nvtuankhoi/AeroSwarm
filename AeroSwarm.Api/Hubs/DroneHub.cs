using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Models;

namespace AeroSwarm.Api.Hubs;

[Authorize]
public class DroneHub : Hub
{
    private readonly AppDbContext _db;
    private readonly ILogger<DroneHub> _logger;

    public DroneHub(AppDbContext db, ILogger<DroneHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var username = Context.User?.Identity?.Name ?? "Unknown";
        _logger.LogInformation("Client connected: {ConnectionId}, User: {Username}", Context.ConnectionId, username);

        _db.DroneEvents.Add(new DroneEvent
        {
            DroneId = 0,
            EventType = "SYS",
            Message = $"Operator '{username}' connected from terminal.",
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var username = Context.User?.Identity?.Name ?? "Unknown";
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);

        _db.DroneEvents.Add(new DroneEvent
        {
            DroneId = 0,
            EventType = "SYS",
            Message = $"Operator '{username}' disconnected.",
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendTelemetry(DroneTelemetry data)
    {
        await Clients.All.SendAsync("ReceiveTelemetry", data);
    }

    public async Task LogEvent(int droneId, string eventType, string message)
    {
        _db.DroneEvents.Add(new DroneEvent
        {
            DroneId = droneId,
            EventType = eventType,
            Message = message,
            OccurredAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await Clients.All.SendAsync("ReceiveEvent", new
        {
            DroneId = droneId,
            Type = eventType,
            Message = message,
            Time = DateTime.UtcNow.ToString("HH:mm:ss")
        });
    }
}
