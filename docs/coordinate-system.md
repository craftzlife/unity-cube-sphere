# Coordinate System & S2 Geometry

## Coordinate Spaces

### Geographic (Z-up) — used internally by S2 math

```
       +Z (North Pole)
        │
        │
        ├──── +Y (lon 90°E)
       ╱
      +X (lon 0°, Equator)
```

- **X**: longitude 0° / 180° axis
- **Y**: longitude 90° / 270° axis
- **Z**: poles / vertical axis (North = +Z)

### Unity (Y-up) — used for rendering

```
       +Y (up / North Pole)
        │
        │
        ├──── +Z (forward)
       ╱
      +X (right)
```

### Conversion

```
GeoToUnity(X, Y, Z) → (X, Z, Y)   // swap Y ↔ Z
UnityToGeo(X, Y, Z) → (X, Z, Y)   // same swap, self-inverse
```

All `S2Geometry` methods (`LatLonToPoint`, `GetFace`, `FaceUVToXYZ`, etc.) operate in geographic Z-up.
`GeoToUnity` is applied at the boundary when producing Unity world positions.

## S2 Face Mapping

| Face | Dominant Geo Axis | Unity Axis | Hemisphere |
|------|-------------------|------------|------------|
| 0    | +X                | +X         | lon 0° centered |
| 1    | +Y                | +Z         | lon 90°E centered |
| 2    | +Z (north)        | +Y         | North Pole |
| 3    | −X                | −X         | lon 180° centered |
| 4    | −Y                | −Z         | lon 90°W centered |
| 5    | −Z (south)        | −Y         | South Pole |

## Face UV Projections

Each face projects a point `(x, y, z)` onto its dominant plane:

| Face | u | v |
|------|---|---|
| 0 (+X) | y/x | z/x |
| 1 (+Y) | −x/y | z/y |
| 2 (+Z) | −x/z | −y/z |
| 3 (−X) | z/x | y/x |
| 4 (−Y) | z/y | −x/y |
| 5 (−Z) | −y/z | −x/z |

UV range: `[-1, 1]`. Converted to ST `[0, 1]` via linear transform:
```
ST = 0.5 * (UV + 1)
UV = 2 * ST - 1
```

## Mesh UV Convention

The mesh uses a uniform-UV layout:
- **Faces 0–2** (positive): `uv = (s, t)` — direct ST mapping
- **Faces 3–5** (negative): `uv = (t, 1−s)` — remapped to match the axis rotation on negative faces so elevation tile data aligns correctly

## Lat/Lon Conversion

```
LatLonToPoint(lat°, lon°):
  x = cos(lat) * cos(lon)
  y = cos(lat) * sin(lon)
  z = sin(lat)

PointToLatLon(x, y, z):
  lat = asin(z)
  lon = atan2(y, x)
```

## Earth Rotation Model

Applied in `CubeSphere.OnEnable()`:

1. **Axial tilt** (23.44°): Tilt axis rotates seasonally around the Y-axis (geographic Z-axis) based on day-of-year.
   ```
   phi = 2π / 365 * (dayOfYear − 81)
   alpha = π/2 − phi
   tiltAxis = (sin(alpha), 0, −cos(alpha))
   tilt = Quaternion.AngleAxis(23.44°, tiltAxis)
   ```

2. **Time-of-day rotation**: Earth rotates 15°/hour. Longitude 0° faces the sun at UTC noon.
   ```
   hourAngle = 15° × (utcHours − 12)
   timeRot = Quaternion.AngleAxis(−hourAngle, Vector3.up)
   ```

3. **Combined**: `EarthRotation = tilt × timeRot` — stored as a static property for use by `RealtimeSunLight`.
