using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using AeroSwarm.Api.Core;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Models;
using AeroSwarm.Api.Options;
using AeroSwarm.Api.Services;

namespace AeroSwarm.Api.Workers;

public class MavlinkWorker : BackgroundService
{
    private readonly ILogger<MavlinkWorker> _logger;
    private readonly IHubContext<DroneHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDroneStateService _stateService;
    private readonly SwarmOptions _opts;

    private readonly Dictionary<int, DateTime> _lastDbWrite = new();

    public bool IsRunning { get; private set; }
    public IPEndPoint? BoundEndpoint { get; private set; }

    public MavlinkWorker(
        ILogger<MavlinkWorker> logger,
        IHubContext<DroneHub> hubContext,
        IServiceScopeFactory scopeFactory,
        IDroneStateService stateService,
        IOptions<SwarmOptions> opts)
    {
        _logger = logger;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _stateService = stateService;
        _opts = opts.Value;

        foreach (var id in _opts.DroneIds)
            _lastDbWrite[id] = DateTime.MinValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bindAddress = IPAddress.Parse(_opts.BindHost);
        BoundEndpoint = new IPEndPoint(bindAddress, _opts.UdpPort);
        _logger.LogInformation("MAVLink Worker starting on UDP {Endpoint}", BoundEndpoint);

        using var udp = new UdpClient(BoundEndpoint);
        IsRunning = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                await HandlePacketAsync(result.Buffer, result.RemoteEndPoint, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MAVLink packet");
            }
        }

        IsRunning = false;
        _logger.LogInformation("MAVLink Worker stopped");
    }

    private async Task HandlePacketAsync(byte[] data, IPEndPoint source, CancellationToken ct)
    {
        var msg = MavlinkV2.Decode(data);
        if (msg is null) return;

        int droneId = msg.SysId;
        if (droneId < 1 || droneId > _opts.DroneCount) return;

        bool changed = false;
        _stateService.UpdateFromTelemetry(droneId, t =>
        {
            t.Ip = source.Address.ToString();
            switch (msg.MsgId)
            {
                case MavlinkV2.MSG_HEARTBEAT:
                    if (msg.Payload.Length >= 9)
                    {
                        uint customMode = BinaryPrimitives.ReadUInt32LittleEndian(msg.Payload.AsSpan(0, 4));
                        byte baseMode = msg.Payload[6];
                        t.IsArmed = (baseMode & 0x80) != 0;
                        t.Mode = MapCustomModeToName(customMode);
                        changed = true;
                    }
                    break;

                case MavlinkV2.MSG_GLOBAL_POSITION_INT:
                    if (msg.Payload.Length >= 28)
                    {
                        var p = msg.Payload.AsSpan();
                        int lat = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(4, 4));
                        int lon = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(8, 4));
                        int relAlt = BinaryPrimitives.ReadInt32LittleEndian(p.Slice(16, 4));
                        short vx = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(20, 2));
                        short vy = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(22, 2));
                        ushort hdg = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(26, 2));

                        t.Latitude = lat / 1e7;
                        t.Longitude = lon / 1e7;
                        t.Altitude = relAlt / 1000f;
                        t.Speed = (float)Math.Sqrt(vx * vx + vy * vy) / 100f;
                        t.Heading = hdg / 100f;
                        t.LinkQuality = 100;
                        changed = true;
                    }
                    break;

                case MavlinkV2.MSG_BATTERY_STATUS:
                    if (msg.Payload.Length >= 36)
                    {
                        var p = msg.Payload.AsSpan();
                        ushort voltMv = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(10, 2));
                        t.BatteryVoltage = voltMv / 1000f;
                        sbyte pct = (sbyte)p[33];
                        t.BatteryPercent = pct < 0 ? 0 : pct;
                        changed = true;
                    }
                    break;

                case MavlinkV2.MSG_GPS_RAW_INT:
                    if (msg.Payload.Length >= 30)
                    {
                        t.GpsSatellites = msg.Payload[29];
                        changed = true;
                    }
                    break;
            }
        });

        if (!changed) return;

        var snapshot = _stateService.GetState(droneId);
        await _hubContext.Clients.All.SendAsync("ReceiveTelemetry", snapshot, ct);

        var now = DateTime.UtcNow;
        if ((now - _lastDbWrite[droneId]).TotalSeconds >= _opts.TelemetryPersistIntervalSec)
        {
            _lastDbWrite[droneId] = now;
            await PersistTelemetryAsync(snapshot, now);
        }
    }

    private async Task PersistTelemetryAsync(DroneTelemetry t, DateTime recordedAt)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TelemetryHistory.Add(new TelemetryHistory
            {
                DroneId = t.DroneId,
                Mode = t.Mode,
                IsArmed = t.IsArmed,
                Latitude = t.Latitude,
                Longitude = t.Longitude,
                Altitude = t.Altitude,
                Speed = t.Speed,
                Heading = t.Heading,
                BatteryPercent = t.BatteryPercent,
                BatteryVoltage = t.BatteryVoltage,
                LinkQuality = t.LinkQuality,
                RecordedAt = recordedAt
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist telemetry for Drone #{DroneId}", t.DroneId);
        }
    }

    private static string MapCustomModeToName(uint mode) => mode switch
    {
        0 => "STABILIZE",
        2 => "ALT_HOLD",
        3 => "AUTO",
        4 => "GUIDED",
        5 => "LOITER",
        6 => "RTL",
        9 => "LAND",
        _ => $"MODE_{mode}",
    };
}
