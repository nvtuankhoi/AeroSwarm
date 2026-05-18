namespace AeroSwarm.Api.Core;

public static class Geo
{
    public const double EarthRadiusM = 6378137.0;
    private const double MetersPerDegLat = 111111.0;

    /// <summary>
    /// Offset a lat/lon by N meters north + E meters east using local-tangent-plane approximation.
    /// Accurate to ~1m for offsets &lt; 10km at moderate latitudes.
    /// </summary>
    public static (double Lat, double Lon) OffsetLatLon(double lat, double lon, double northM, double eastM)
    {
        double lat2 = lat + northM / MetersPerDegLat;
        double lon2 = lon + eastM / (MetersPerDegLat * Math.Cos(lat * Math.PI / 180.0));
        return (lat2, lon2);
    }

    /// <summary>Great-circle distance in meters between two lat/lon points.</summary>
    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double rLat1 = lat1 * Math.PI / 180.0;
        double rLat2 = lat2 * Math.PI / 180.0;

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(rLat1) * Math.Cos(rLat2) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusM * c;
    }

    /// <summary>Offset by distance + bearing (degrees from north, clockwise).</summary>
    public static (double Lat, double Lon) BearingOffset(double lat, double lon, double bearingDeg, double distanceM)
    {
        double brng = bearingDeg * Math.PI / 180.0;
        double northM = distanceM * Math.Cos(brng);
        double eastM = distanceM * Math.Sin(brng);
        return OffsetLatLon(lat, lon, northM, eastM);
    }
}
