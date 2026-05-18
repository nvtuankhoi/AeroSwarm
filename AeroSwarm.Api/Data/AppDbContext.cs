using Microsoft.EntityFrameworkCore;
using AeroSwarm.Api.Models;

namespace AeroSwarm.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TelemetryHistory> TelemetryHistory => Set<TelemetryHistory>();
    public DbSet<DroneEvent> DroneEvents => Set<DroneEvent>();
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<Waypoint> Waypoints => Set<Waypoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetryHistory>()
            .HasIndex(t => new { t.DroneId, t.RecordedAt });

        modelBuilder.Entity<DroneEvent>()
            .HasIndex(e => new { e.DroneId, e.OccurredAt });

        modelBuilder.Entity<Waypoint>()
            .HasOne(w => w.Flight)
            .WithMany(f => f.Waypoints)
            .HasForeignKey(w => w.FlightId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Waypoint>()
            .HasIndex(w => new { w.FlightId, w.DroneId, w.Sequence });
    }
}
