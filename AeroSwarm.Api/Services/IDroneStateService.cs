using AeroSwarm.Api.Models;

namespace AeroSwarm.Api.Services;

public interface IDroneStateService
{
    DroneTelemetry GetState(int droneId);
    IEnumerable<DroneTelemetry> All();
    void UpdateFromTelemetry(int droneId, Action<DroneTelemetry> mutator);
    void SetOnline(int droneId, bool isOnline);
    bool TryGetIp(int droneId, out string ip);
    bool TryGetEndpoint(int droneId, out string ip, out int port);
}
