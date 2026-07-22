using First10.Domain.Channels;

namespace First10.Domain.Triage;

/// <summary>
/// Distance from a point to the corridor centerline (Stage 0). Equirectangular
/// approximation is fine at corridor scale (&lt;50km, near-equator).
/// </summary>
public static class CorridorGeofence
{
    private const double EarthRadiusKm = 6371.0;

    public static bool IsNearCorridor(GeoPoint point, GeoPoint[] centerline, double bufferKm)
    {
        if (centerline.Length == 0) return true; // no corridor configured → never flag
        if (centerline.Length == 1) return DistanceKm(point, centerline[0]) <= bufferKm;

        for (var i = 0; i < centerline.Length - 1; i++)
        {
            if (DistanceToSegmentKm(point, centerline[i], centerline[i + 1]) <= bufferKm)
            {
                return true;
            }
        }
        return false;
    }

    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        var (ax, ay) = Project(a);
        var (bx, by) = Project(b);
        return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    private static double DistanceToSegmentKm(GeoPoint p, GeoPoint a, GeoPoint b)
    {
        var (px, py) = Project(p);
        var (ax, ay) = Project(a);
        var (bx, by) = Project(b);

        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));

        var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        var cx = ax + t * dx;
        var cy = ay + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    private static (double X, double Y) Project(GeoPoint p)
    {
        // Equirectangular projection anchored near the corridor (lat ~6.7°).
        var x = p.Longitude * Math.Cos(6.7 * Math.PI / 180.0) * Math.PI / 180.0 * EarthRadiusKm;
        var y = p.Latitude * Math.PI / 180.0 * EarthRadiusKm;
        return (x, y);
    }
}
