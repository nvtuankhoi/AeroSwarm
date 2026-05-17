using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using AeroSwarm.Api.Data;
using AeroSwarm.Api.Hubs;
using AeroSwarm.Api.Models;

namespace AeroSwarm.Api.Workers;

public class MavlinkWorker : BackgroundService
{
    private readonly ILogger<MavlinkWorker> _logger;
    private readonly IHubContext<DroneHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly Dictionary<int, DroneTelemetry> _currentState = new();
    private readonly Dictionary<int, DateTime> _lastDbWrite = new();

    public MavlinkWorker(
        ILogger<MavlinkWorker> logger,
        IHubContext<DroneHub> hubContext,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;

        for (int i = 1; i <= 5; i++)
        {
            _currentState[i] = new DroneTelemetry { DroneId = i, Mode = "STABILIZE" };
            _lastDbWrite[i] = DateTime.MinValue;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MAVLink Worker starting on UDP 0.0.0.0:14550");

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 14550));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                var data = result.Buffer;

                // MAVLink v2: magic=0xFD, len, incompat, compat, seq, sysid, compid, msgid(3 bytes)
                if (data.Length < 10 || data[0] != 0xFD)
                    continue;

                int payloadLen = data[1];
                int droneId = data[5]; // system ID maps to drone 1–5

                if (droneId < 1 || droneId > 5)
                    continue;

                uint msgId = (uint)(data[7] | (data[8] << 8) | (data[9] << 16));

                if (!_currentState.TryGetValue(droneId, out var telemetry))
                    continue;

                const int headerLen = 10;

                if (msgId == 0) // HEARTBEAT
                {
                    if (data.Length < headerLen + 9) continue;
                    var payload = data.AsSpan(headerLen);
                    uint customMode = BitConverter.ToUInt32(payload[0..4]);
                    byte baseMode = payload[6];
                    telemetry.IsArmed = (baseMode & 0x80) != 0;
                    telemetry.Mode = customMode switch
                    {
                        0 => "STABILIZE",
                        2 => "ALT_HOLD",
                        3 => "AUTO",
                        4 => "GUIDED",
                        5 => "LOITER",
                        6 => "RTL",
                        9 => "LAND",
                        _ => $"MODE_{customMode}"
                    };
                }
                else if (msgId == 33) // GLOBAL_POSITION_INT
                {
                    if (data.Length < headerLen + 28) continue;
                    var payload = data.AsSpan(headerLen);
                    int lat = BitConverter.ToInt32(payload[0..4]);
                    int lon = BitConverter.ToInt32(payload[4..8]);
                    int relAlt = BitConverter.ToInt32(payload[12..16]);
                    short vx = BitConverter.ToInt16(payload[16..18]);
                    short vy = BitConverter.ToInt16(payload[18..20]);
                    ushort hdg = BitConverter.ToUInt16(payload[26..28]);

                    telemetry.Latitude = lat / 1e7;
                    telemetry.Longitude = lon / 1e7;
                    telemetry.Altitude = relAlt / 1000.0f;
                    telemetry.Speed = (float)Math.Sqrt(vx * vx + vy * vy) / 100.0f;
                    telemetry.Heading = hdg / 100.0f;
                }
                else if (msgId == 147) // BATTERY_STATUS
                {
                    if (data.Length < headerLen + 36) continue;
                    var payload = data.AsSpan(headerLen);
                    ushort voltageRaw = BitConverter.ToUInt16(payload[10..12]);
                    telemetry.BatteryVoltage = voltageRaw / 1000.0f;
                    sbyte pct = (sbyte)payload[33];
                    telemetry.BatteryPercent = pct < 0 ? 0 : pct;
                }
                else if (msgId == 24) // GPS_RAW_INT — satellites_visible at payload byte 29
                {
                    if (data.Length < headerLen + 30) continue;
                    var payload = data.AsSpan(headerLen);
                    telemetry.GpsSatellites = payload[29];
                }
                else if (msgId == 168) // WIND — direction (rad, bytes 0-3), speed m/s (bytes 4-7)
                {
                    if (data.Length < headerLen + 8) continue;
                    var payload = data.AsSpan(headerLen);
                    float dirRad = BitConverter.ToSingle(payload[0..4]);
                    float speed  = BitConverter.ToSingle(payload[4..8]);
                    telemetry.WindSpeed = speed;
                    telemetry.WindDirectionDeg = dirRad * (180f / MathF.PI);
                }

                // Push live telemetry to all SignalR clients
                await _hubContext.Clients.All.SendAsync("ReceiveTelemetry", telemetry, stoppingToken);

                // Persist snapshot to DB every 5 seconds per drone
                var now = DateTime.UtcNow;
                if ((now - _lastDbWrite[droneId]).TotalSeconds >= 5)
                {
                    _lastDbWrite[droneId] = now;
                    await PersistTelemetryAsync(telemetry, now);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MAVLink packet");
            }
        }

        _logger.LogInformation("MAVLink Worker stopped");
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
                GpsSatellites = t.GpsSatellites,
                WindSpeed = t.WindSpeed,
                RecordedAt = recordedAt
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist telemetry for Drone #{DroneId}", t.DroneId);
        }
    }
}
