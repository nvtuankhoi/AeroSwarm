namespace AeroSwarm.Api.Options;

public class SwarmOptions
{
    public const string SectionName = "Swarm";

    public int DroneCount { get; set; } = 3;
    public string IpTemplate { get; set; } = "10.105.151.{0}";
    public int IpStart { get; set; } = 101;
    public int UdpPort { get; set; } = 14550;
    public string BindHost { get; set; } = "0.0.0.0";
    public double HeartbeatIntervalSec { get; set; } = 1.0;
    public double DropoutThresholdSec { get; set; } = 3.0;
    public double TelemetryPersistIntervalSec { get; set; } = 5.0;
    public double LowVoltagePerCell { get; set; } = 3.30;
    public double CriticalVoltagePerCell { get; set; } = 3.20;
    public int LiPoCellCount { get; set; } = 1;

    public IEnumerable<int> DroneIds => Enumerable.Range(1, DroneCount);

    public string GetIp(int droneId) =>
        string.Format(IpTemplate, IpStart + (droneId - 1));

    public IReadOnlyDictionary<int, string> BuildIpMap() =>
        DroneIds.ToDictionary(id => id, GetIp);
}
