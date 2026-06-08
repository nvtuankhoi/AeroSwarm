using AeroSwarm.Api.Models;
using AeroSwarm.Api.Options;
using Microsoft.Extensions.Options;

namespace AeroSwarm.Api.Services;

public class DroneStateService : IDroneStateService
{
    private readonly Dictionary<int, DroneTelemetry> _state = new();
    private readonly Lock _lock = new();
    private readonly IReadOnlyDictionary<int, string> _ipMap;

    public DroneStateService(IOptions<SwarmOptions> opts)
    {
        var o = opts.Value;
        _ipMap = o.BuildIpMap();

        foreach (var id in o.DroneIds)
        {
            _state[id] = new DroneTelemetry
            {
                DroneId = id,
                Mode = "STABILIZE",
                Ip = _ipMap[id],
                IsOnline = false,
            };
        }
    }

    public DroneTelemetry GetState(int droneId)
    {
        lock (_lock)
        {
            return _state.TryGetValue(droneId, out var t)
                ? Clone(t)
                : new DroneTelemetry { DroneId = droneId };
        }
    }

    public IEnumerable<DroneTelemetry> All()
    {
        lock (_lock)
        {
            return _state.Values.Select(Clone).ToList();
        }
    }

    public void UpdateFromTelemetry(int droneId, Action<DroneTelemetry> mutator)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(droneId, out var t))
            {
                t = new DroneTelemetry { DroneId = droneId, Ip = _ipMap.GetValueOrDefault(droneId, "") };
                _state[droneId] = t;
            }
            mutator(t);
            t.LastSeen = DateTime.UtcNow;
            t.IsOnline = true;
        }
    }

    public void SetOnline(int droneId, bool isOnline)
    {
        lock (_lock)
        {
            if (_state.TryGetValue(droneId, out var t))
                t.IsOnline = isOnline;
        }
    }

    public bool TryGetIp(int droneId, out string ip)
    {
        lock (_lock)
        {
            // Prefer live IP discovered from MAVLink packets
            if (_state.TryGetValue(droneId, out var t) && !string.IsNullOrEmpty(t.Ip))
            {
                ip = t.Ip;
                return true;
            }
            return _ipMap.TryGetValue(droneId, out ip!);
        }
    }

    private static DroneTelemetry Clone(DroneTelemetry t) => new()
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
        WindDirectionDeg = t.WindDirectionDeg,
        LastSeen = t.LastSeen,
        IsOnline = t.IsOnline,
        Ip = t.Ip,
    };
}
