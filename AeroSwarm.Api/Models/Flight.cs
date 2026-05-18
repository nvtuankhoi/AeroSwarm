namespace AeroSwarm.Api.Models;

public class Flight
{
    public int Id { get; set; }
    public string MissionType { get; set; } = "SINGLE";
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "IN_PROGRESS";
    public double SpacingM { get; set; }
    public string Formation { get; set; } = string.Empty;

    public List<Waypoint> Waypoints { get; set; } = new();
}
