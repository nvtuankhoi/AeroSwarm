using Microsoft.EntityFrameworkCore;
using AeroSwarm.Api.Models;

namespace AeroSwarm.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<TelemetryHistory> TelemetryHistory => Set<TelemetryHistory>();
    public DbSet<DroneEvent> DroneEvents => Set<DroneEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetryHistory>()
            .HasIndex(t => new { t.DroneId, t.RecordedAt });

        modelBuilder.Entity<DroneEvent>()
            .HasIndex(e => new { e.DroneId, e.OccurredAt });
    }
}
