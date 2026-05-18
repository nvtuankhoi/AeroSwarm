using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Models;
using AeroSwarm.Api.Options;
using AeroSwarm.Api.Services;

namespace AeroSwarm.Api.Workers;

/// <summary>
/// Scans drone state every 500ms. Detects dropout (LastSeen too old), low/critical battery.
/// Emits SignalR events: ReceiveDroneStatus, ReceiveEvent.
/// Persists DroneEvent rows for offline review.
/// </summary>
public class FailsafeMonitor : BackgroundService
{
    private readonly ILogger<FailsafeMonitor> _logger;
    private readonly IDroneStateService _stateService;
    private readonly IHubContext<DroneHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SwarmOptions _opts;

    private readonly Dictionary<int, BattLevel> _battLevel = new();

    private enum BattLevel { Normal, Warning, Critical }

    public FailsafeMonitor(
        ILogger<FailsafeMonitor> logger,
        IDroneStateService stateService,
        IHubContext<DroneHub> hub,
        IServiceScopeFactory scopeFactory,
        IOptions<SwarmOptions> opts)
    {
        _logger = logger;
        _stateService = stateService;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FailsafeMonitor starting (dropout threshold {Sec}s)", _opts.DropoutThresholdSec);
        var period = TimeSpan.FromMilliseconds(500);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FailsafeMonitor scan error");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var lowV = _opts.LowVoltagePerCell * _opts.LiPoCellCount;
        var critV = _opts.CriticalVoltagePerCell * _opts.LiPoCellCount;

        foreach (var t in _stateService.All())
        {
            // Dropout
            bool seenEver = t.LastSeen > DateTime.MinValue;
            bool stale = seenEver && (now - t.LastSeen).TotalSeconds > _opts.DropoutThresholdSec;
            if (stale && t.IsOnline)
            {
                _stateService.SetOnline(t.DroneId, false);
                await EmitAsync(t.DroneId, "WARN",
                    $"Drone #{t.DroneId} dropout — no telemetry for {_opts.DropoutThresholdSec}s.", ct);
                await _hub.Clients.All.SendAsync("ReceiveDroneStatus",
                    new { DroneId = t.DroneId, IsOnline = false }, ct);
            }
            else if (!stale && seenEver && !t.IsOnline)
            {
                _stateService.SetOnline(t.DroneId, true);
                await EmitAsync(t.DroneId, "SYS",
                    $"Drone #{t.DroneId} reconnected.", ct);
                await _hub.Clients.All.SendAsync("ReceiveDroneStatus",
                    new { DroneId = t.DroneId, IsOnline = true }, ct);
            }

            // Battery (only when we have a reading)
            if (t.BatteryVoltage > 0)
            {
                var newLevel = t.BatteryVoltage < critV ? BattLevel.Critical
                             : t.BatteryVoltage < lowV ? BattLevel.Warning
                             : BattLevel.Normal;

                var prev = _battLevel.GetValueOrDefault(t.DroneId, BattLevel.Normal);
                if (newLevel != prev)
                {
                    _battLevel[t.DroneId] = newLevel;
                    if (newLevel == BattLevel.Warning)
                        await EmitAsync(t.DroneId, "WARN",
                            $"Drone #{t.DroneId} battery low ({t.BatteryVoltage:F2}V).", ct);
                    else if (newLevel == BattLevel.Critical)
                        await EmitAsync(t.DroneId, "WARN",
                            $"Drone #{t.DroneId} battery CRITICAL ({t.BatteryVoltage:F2}V) — RTL recommended.", ct);
                }
            }
        }
    }

    private async Task EmitAsync(int droneId, string type, string message, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DroneEvents.Add(new DroneEvent
            {
                DroneId = droneId,
                EventType = type,
                Message = message,
                OccurredAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist DroneEvent for drone {Id}", droneId);
        }

        await _hub.Clients.All.SendAsync("ReceiveEvent", new
        {
            DroneId = droneId,
            Type = type,
            Message = message,
            Time = DateTime.UtcNow.ToString("HH:mm:ss"),
        }, ct);
    }
}
