using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using AeroSwarm.Api.Core;
using AeroSwarm.Api.Options;
using AeroSwarm.Api.Services;

namespace AeroSwarm.Api.Workers;

/// <summary>
/// Sends a GCS HEARTBEAT (sysid=255) to each known drone at 1 Hz.
/// Drones use this to drive their watchdog — losing it for 3+ s triggers RTL on-board.
/// </summary>
public class HeartbeatSender : BackgroundService
{
    private readonly ILogger<HeartbeatSender> _logger;
    private readonly IDroneStateService _stateService;
    private readonly SwarmOptions _opts;

    public HeartbeatSender(
        ILogger<HeartbeatSender> logger,
        IDroneStateService stateService,
        IOptions<SwarmOptions> opts)
    {
        _logger = logger;
        _stateService = stateService;
        _opts = opts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatSender starting at {Hz}Hz", 1.0 / _opts.HeartbeatIntervalSec);
        var period = TimeSpan.FromSeconds(_opts.HeartbeatIntervalSec);
        var frame = MavlinkV2.EncodeHeartbeat();

        using var udp = new UdpClient();
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var id in _opts.DroneIds)
            {
                if (!_stateService.TryGetIp(id, out var ip))
                {
                    ip = "255.255.255.255"; // broadcast until drone is discovered
                }
                try
                {
                    var port = _opts.GetPort(id);
                    var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
                    await udp.SendAsync(frame, frame.Length, endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "HeartbeatSender failed for drone {Id} @ {Ip}", id, ip);
                }
            }
            try { await Task.Delay(period, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }

        _logger.LogInformation("HeartbeatSender stopped");
    }
}
