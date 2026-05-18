namespace AeroSwarm.Api.Core;

public record WaypointDto(double Lat, double Lon, float Alt);

public interface IFormation
{
    /// <summary>
    /// Compute per-drone (north, east) offsets in meters from the leader for an n-drone group.
    /// Index 0 is the leader (offset 0,0). Subsequent indices follow the formation pattern.
    /// </summary>
    IReadOnlyList<(double NorthM, double EastM)> Offsets(int droneCount, double spacingM);
}

/// <summary>
/// V-shape formation opening to the rear. Leader at index 0.
/// Slave i alternates left/right with each row further behind.
/// </summary>
public class VFormation : IFormation
{
    public IReadOnlyList<(double NorthM, double EastM)> Offsets(int droneCount, double spacingM)
    {
        var result = new List<(double, double)>(droneCount) { (0.0, 0.0) };
        for (int i = 1; i < droneCount; i++)
        {
            int row = (i + 1) / 2;             // 1,1,2,2,3,3,...
            int sign = (i % 2 == 1) ? -1 : +1; // alternate left/right
            double north = -row * spacingM;
            double east = sign * row * spacingM * 0.5;
            result.Add((north, east));
        }
        return result;
    }
}

/// <summary>Line formation along the east axis. Leader at index 0.</summary>
public class LineFormation : IFormation
{
    public IReadOnlyList<(double NorthM, double EastM)> Offsets(int droneCount, double spacingM)
    {
        var result = new List<(double, double)>(droneCount);
        for (int i = 0; i < droneCount; i++)
            result.Add((0.0, i * spacingM));
        return result;
    }
}

public static class FormationFactory
{
    public static IFormation Resolve(string name) => name?.ToUpperInvariant() switch
    {
        "V" => new VFormation(),
        "LINE" => new LineFormation(),
        _ => new VFormation(),
    };
}

public static class MissionPlanner
{
    /// <summary>
    /// Given a list of leader waypoints, expand to per-drone waypoints by applying the formation offset.
    /// Returns: drone id → ordered list of waypoints.
    /// </summary>
    public static Dictionary<int, List<WaypointDto>> PlanSwarmMission(
        IReadOnlyList<WaypointDto> leaderWaypoints,
        IFormation formation,
        IReadOnlyList<int> droneIds,
        double spacingM)
    {
        var offsets = formation.Offsets(droneIds.Count, spacingM);
        var result = new Dictionary<int, List<WaypointDto>>();

        for (int i = 0; i < droneIds.Count; i++)
        {
            var (n, e) = offsets[i];
            var waypoints = new List<WaypointDto>(leaderWaypoints.Count);
            foreach (var lw in leaderWaypoints)
            {
                var (lat, lon) = Geo.OffsetLatLon(lw.Lat, lw.Lon, n, e);
                waypoints.Add(new WaypointDto(lat, lon, lw.Alt));
            }
            result[droneIds[i]] = waypoints;
        }
        return result;
    }
}
