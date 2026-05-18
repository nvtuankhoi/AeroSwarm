namespace AeroSwarm.Api.Models;

public class DroneTelemetry
{
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
    public int GpsSatellites { get; set; }     // GPS_RAW_INT (MSG_ID 24)
    public float WindSpeed { get; set; }        // WIND (MSG_ID 168)
    public float WindDirectionDeg { get; set; } // WIND (MSG_ID 168)

    public DateTime LastSeen { get; set; } = DateTime.MinValue;
    public bool IsOnline { get; set; }
    public string Ip { get; set; } = string.Empty;
}
