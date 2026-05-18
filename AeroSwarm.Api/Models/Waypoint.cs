namespace AeroSwarm.Api.Models;

public class Waypoint
{
    public int Id { get; set; }
    public int FlightId { get; set; }
    public Flight? Flight { get; set; }

    public int DroneId { get; set; }
    public int Sequence { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float Altitude { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTime? ReachedAt { get; set; }
}
