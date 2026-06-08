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
    private readonly HashSet<int> _streamRequestsSent = new();

    public bool IsRunning { get; private set; }
    public IReadOnlyList<IPEndPoint> BoundEndpoints { get; private set; } = new List<IPEndPoint>();

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
        var ports = _opts.UdpPorts.Count > 0 ? _opts.UdpPorts : new List<int> { _opts.UdpPort };

        var clients = new List<UdpClient>();
        var endpoints = new List<IPEndPoint>();
        var tasks = new List<Task>();

        foreach (var port in ports)
        {
            var endpoint = new IPEndPoint(bindAddress, port);
            var udp = new UdpClient(endpoint);
            clients.Add(udp);
            endpoints.Add(endpoint);
            tasks.Add(ReceiveLoopAsync(udp, port, stoppingToken));
        }

        BoundEndpoints = endpoints.AsReadOnly();
        _logger.LogInformation("MAVLink Worker starting on UDP ports {Ports}", string.Join(", ", ports));
        IsRunning = true;

        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            IsRunning = false;
            foreach (var udp in clients)
            {
                try { udp.Dispose(); }
                catch (Exception ex) { _logger.LogDebug(ex, "Error disposing UdpClient"); }
            }
            _logger.LogInformation("MAVLink Worker stopped");
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, int port, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                await HandlePacketAsync(udp, result.Buffer, result.RemoteEndPoint, port, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MAVLink packet on port {Port}", port);
            }
        }
    }

    private async Task HandlePacketAsync(UdpClient udp, byte[] data, IPEndPoint source, int port, CancellationToken ct)
    {
        int droneId = _opts.UdpPorts.IndexOf(port) + 1;
        if (droneId < 1 || droneId > _opts.DroneCount) return;

        bool anyChanged = false;
        bool isFirstHeartbeat = false;
        int offset = 0;
        while (offset < data.Length)
        {
            var msg = MavlinkV2.Decode(data.AsSpan(offset));
            if (msg is null) break;

            int packetLen = MavlinkV2.HeaderLen + msg.Payload.Length + 2;
            offset += packetLen;

            // Ignore GCS heartbeats (our own HeartbeatSender) so stream requests go to the real drone.
            bool fromGcs = msg.SysId == MavlinkV2.GcsSysId;
            bool changed = false;
            // Ignore GCS heartbeats entirely — they should not overwrite real drone state.
            if (fromGcs) continue;

            _stateService.UpdateFromTelemetry(droneId, t =>
            {
                t.Ip = source.Address.ToString();
                t.RemotePort = source.Port;
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
                            lock (_streamRequestsSent)
                            {
                                isFirstHeartbeat = _streamRequestsSent.Add(droneId);
                            }
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
            if (changed) anyChanged = true;
        }

        if (!anyChanged) return;

        if (isFirstHeartbeat)
        {
            await RequestDataStreamsAsync(udp, source, (byte)droneId, ct);
        }

        var snapshot = _stateService.GetState(droneId);
        await _hubContext.Clients.All.SendAsync("ReceiveTelemetry", snapshot, ct);

        // Forward SITL state to paired ESP32 for hardware feedback sync
        if (_opts.SitlToEsp32Map.TryGetValue(droneId, out var esp32Id))
        {
            await ForwardStateToEsp32Async(snapshot, esp32Id, ct);
        }

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

    private async Task RequestDataStreamsAsync(UdpClient udp, IPEndPoint target, byte targetSysId, CancellationToken ct)
    {
        try
        {
            // Use MAVLink 1 REQUEST_DATA_STREAM (msg 66) which SITL and real autopilots
            // respond to reliably. Stream IDs: 0=ALL, 6=POSITION.
            var requests = new[]
            {
                (reqStreamId: (byte)6, rate: (ushort)4),   // POSITION @ 4Hz
                (reqStreamId: (byte)0, rate: (ushort)4),   // ALL @ 4Hz
            };
            foreach (var (reqStreamId, rate) in requests)
            {
                var frame = MavlinkV2.EncodeRequestDataStream(targetSysId, MavlinkV2.AutopilotCompId,
                    reqStreamId, rate, startStop: 1);
                await udp.SendAsync(frame, frame.Length, target);
            }
            _logger.LogInformation("Requested data streams for Drone #{DroneId} @ {Endpoint}", targetSysId, target);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to request data streams for Drone #{DroneId}", targetSysId);
        }
    }

    private async Task ForwardStateToEsp32Async(DroneTelemetry state, int esp32Id, CancellationToken ct)
    {
        if (!_stateService.TryGetEndpoint(esp32Id, out var ip, out var port) || port == 0)
        {
            _logger.LogDebug("Cannot forward SITL state: ESP32 #{Esp32Id} endpoint unknown", esp32Id);
            return;
        }

        try
        {
            using var udp = new UdpClient();
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
            uint t = (uint)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

            var frames = new[]
            {
                MavlinkV2.EncodeNamedValueFloat(t, "AS_ARM", state.IsArmed ? 1.0f : 0.0f),
                MavlinkV2.EncodeNamedValueFloat(t, "AS_MOD", ModeToSyncValue(state.Mode)),
                MavlinkV2.EncodeNamedValueFloat(t, "AS_ALT", state.Altitude),
                MavlinkV2.EncodeNamedValueFloat(t, "AS_LAT", (float)state.Latitude),
                MavlinkV2.EncodeNamedValueFloat(t, "AS_LON", (float)state.Longitude),
            };

            foreach (var f in frames)
            {
                await udp.SendAsync(f, endpoint, ct);
            }
            _logger.LogDebug("Forwarded SITL state from #{SitlId} to ESP32 #{Esp32Id} @ {Ip}:{Port}",
                state.DroneId, esp32Id, ip, port);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to forward SITL state to ESP32 #{Esp32Id}", esp32Id);
        }
    }

    private static float ModeToSyncValue(string mode) => mode switch
    {
        "STABILIZE" => 0.0f,
        "ARMED"     => 1.0f,
        "TAKEOFF"   => 2.0f,
        "GUIDED"    => 3.0f,
        "LOITER"    => 3.0f,
        "AUTO"      => 3.0f,
        "RTL"       => 4.0f,
        "LAND"      => 5.0f,
        _           => 0.0f,
    };

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
