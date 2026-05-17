namespace AeroSwarm.Api.Models;

public class TelemetryHistory
{
    public int Id { get; set; }
    public int DroneId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public bool IsArmed { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float Altitude { get; set; }
    public float Speed { get; set; }
    public float Heading { get; set; }
    public int BatteryPercent { get; set; }
    public float BatteryVoltage { get; set; }
    public int LinkQuality { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
