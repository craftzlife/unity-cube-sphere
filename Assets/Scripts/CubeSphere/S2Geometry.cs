using UnityEngine;

/// <summary>
/// S2 Geometry utilities for cube-sphere coordinate conversions.
///
/// Coordinate system mapping:
///   Geographic (Z-up)       Unity (Y-up)
///   ─────────────────       ────────────
///   X  (lon 0°/180°)   →   X  (unchanged)
///   Y  (lon 90°/270°)  →   Z
///   Z  (poles/up)       →   Y
///
/// In Unity world space:
///   North Pole  = +Y     South Pole = -Y
///   Equator lies in the XZ plane
///
/// S2 face index → geographic dominant axis → Unity axis:
///   Face 0: +X → +X       Face 3: -X → -X
///   Face 1: +Y → +Z       Face 4: -Y → -Z
///   Face 2: +Z (north) → +Y   Face 5: -Z (south) → -Y
///
/// GeoToUnity / UnityToGeo handle the Y↔Z swap at the boundary.
/// All internal math (LatLonToPoint, GetFace, FaceUVToXYZ, etc.)
/// operates in geographic Z-up coordinates.
/// </summary>
public static class S2Geometry
{
    /// Geographic (X,Y,Z) → Unity (X,Z,Y): swaps Y↔Z so geographic Z-up becomes Unity Y-up.
    public static Vector3 GeoToUnity(Vector3 geo) => new Vector3(geo.x, geo.z, geo.y);
    /// Unity (X,Y,Z) → Geographic (X,Z,Y): reverses the swap for internal S2 math.
    public static Vector3 UnityToGeo(Vector3 unity) => new Vector3(unity.x, unity.z, unity.y);

    /// <summary>Convert latitude/longitude (degrees) to unit sphere point (Z-up).</summary>
    public static Vector3 LatLonToPoint(float latDeg, float lonDeg)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Cos(lat) * Mathf.Sin(lon),
            Mathf.Sin(lat)
        );
    }

    /// <summary>Convert unit sphere point to (latitude, longitude) degrees.</summary>
    public static Vector2 PointToLatLon(Vector3 p)
    {
        p.Normalize();
        float lat = Mathf.Asin(Mathf.Clamp(p.z, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        return new Vector2(lat, lon);
    }

    /// <summary>Get S2 face index for a point on the unit sphere.</summary>
    public static int GetFace(Vector3 p)
    {
        float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y), az = Mathf.Abs(p.z);
        if (ax >= ay && ax >= az) return p.x > 0 ? 0 : 3;
        if (ay >= az) return p.y > 0 ? 1 : 4;
        return p.z > 0 ? 2 : 5;
    }

    /// <summary>Convert XYZ point to (face, u, v) where u,v in [-1,1].</summary>
    public static void XYZToFaceUV(Vector3 p, out int face, out float u, out float v)
    {
        face = GetFace(p);
        ValidXYZToFaceUV(face, p, out u, out v);
    }

    public static void ValidXYZToFaceUV(int face, Vector3 p, out float u, out float v)
    {
        switch (face)
        {
            case 0: u =  p.y / p.x; v =  p.z / p.x; break;
            case 1: u = -p.x / p.y; v =  p.z / p.y; break;
            case 2: u = -p.x / p.z; v = -p.y / p.z; break;
            case 3: u =  p.z / p.x; v =  p.y / p.x; break;
            case 4: u =  p.z / p.y; v = -p.x / p.y; break;
            case 5: u = -p.y / p.z; v = -p.x / p.z; break;
            default: u = v = 0; break;
        }
    }

    /// <summary>Convert (face, u, v) to XYZ point (unnormalized).</summary>
    public static Vector3 FaceUVToXYZ(int face, float u, float v)
    {
        switch (face)
        {
            case 0: return new Vector3( 1,  u,  v);
            case 1: return new Vector3(-u,  1,  v);
            case 2: return new Vector3(-u, -v,  1);
            case 3: return new Vector3(-1, -v, -u);
            case 4: return new Vector3( v, -1, -u);
            case 5: return new Vector3( v,  u, -1);
            default: return Vector3.zero;
        }
    }

    /// <summary>UV [-1,1] to ST [0,1] (linear projection).</summary>
    public static float UVToST(float u) => 0.5f * (u + 1f);

    /// <summary>ST [0,1] to UV [-1,1] (linear projection).</summary>
    public static float STToUV(float s) => 2f * s - 1f;

    /// <summary>Convert lat/lon to face and ST coordinates [0,1].</summary>
    public static void LatLonToFaceST(float latDeg, float lonDeg, out int face, out float s, out float t)
    {
        Vector3 p = LatLonToPoint(latDeg, lonDeg);
        XYZToFaceUV(p, out face, out float u, out float v);
        s = UVToST(u);
        t = UVToST(v);
    }

    /// <summary>Convert lat/lon to Unity world position on sphere.</summary>
    public static Vector3 LatLonToUnityPosition(float latDeg, float lonDeg, float radius)
    {
        return GeoToUnity(LatLonToPoint(latDeg, lonDeg) * radius);
    }
}
