namespace AeroSwarm.Api.Models;

public class DroneEvent
{
    public int Id { get; set; }
    public int DroneId { get; set; }
    /// <summary>SYS | WARN | CMD | ACK | INFO</summary>
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
