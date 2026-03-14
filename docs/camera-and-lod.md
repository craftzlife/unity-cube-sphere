# Camera & LOD System

## EarthCamera

An orbit camera that revolves around the globe origin using quaternion-based free rotation (no Euler angles). The camera can orbit in any direction, including over the poles, without gimbal lock or axis restrictions.

### Controls (Play Mode)

| Input | Action |
|-------|--------|
| Left mouse drag | Rotate orbit freely in any direction |
| Scroll wheel | Zoom in/out |

### Orbit Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `distance` | 300 | Current orbit distance |
| `minDistance` | 101 | Closest zoom (just above surface, radius=100) |
| `maxDistance` | 300 | Farthest zoom |
| `rotationSpeed` | 5 | Mouse sensitivity multiplier |
| `zoomSpeed` | 50 | Scroll sensitivity multiplier |

### Screen-Space Rotation

Mouse drag rotates the orbit quaternion around the camera's screen-space axes (`transform.up` for horizontal drag, `transform.right` for vertical drag). This ensures drag direction always matches visual movement regardless of Earth tilt or viewing angle.

Rotation speed scales cubically with distance so dragging feels consistent at all zoom levels:

```
ratio = distance / maxDistance
distanceFactor = ratio³
speed = rotationSpeed × 0.1 × distanceFactor
yawDelta   = AngleAxis( delta.x × speed, camera.up)
pitchDelta = AngleAxis(-delta.y × speed, camera.right)
orbitRotation = pitchDelta × yawDelta × orbitRotation
```

- At `maxDistance` (300): full speed (distanceFactor = 1.0)
- At distance 110: distanceFactor ≈ 0.05 — much slower, matching the closer perspective

### Dynamic Clip Planes

Near and far clip planes adjust based on orbit distance to prevent z-fighting and clipping:

```
surfaceDist = distance − 100  (globe radius)
near = max(0.1, surfaceDist × 0.1)
far  = distance + 150
```

### GPS Initialization

On `Start()`, if a `GpsMarker` exists, the camera computes the orbit quaternion to look at the marker's lat/lon position on the globe.

## LOD System

### Computation

LOD is computed logarithmically from the camera's effective distance:

```
t = InverseLerp(log(minDist), log(maxDist), log(distance))
continuous = (1 − t) × 13
lod = round(continuous), clamped to [0, 13]
```

- **LOD 0** = farthest (coarsest detail)
- **LOD 13** = closest (finest detail)
- Default `minDist = 101`, `maxDist = 300` (matches play mode orbit range)

### FOV-Aware Effective Distance

Narrow FOV (zoom lens) should produce coarser detail, so the system applies an FOV correction:

```
effectiveDistance = distance × tan(FOV/2) / tan(30°)
```

Reference half-FOV is 30° (60° total). At FOV < 60°, effective distance increases → coarser LOD.

### LOD Progress

`lodProgress` is a `[0, 1]` fractional position within the current LOD band:
- 0 = just entered this LOD level
- 1 = about to transition to the next

Used by `LatLonGrid` to smoothly fade minor lines in/out.

### Event System

`EarthCamera` fires `OnLodChanged(int)` whenever the LOD level changes. Subscribers:
- `ElevationTileLoader` — remaps to tile LOD and reloads textures
- `LatLonGrid` — rebuilds grid with new spacing parameters

## LatLonGrid LOD Table

| LOD | Major° | Minor° | Labels° | charSize |
|-----|--------|--------|---------|----------|
| 0–3 | 20 | — | 20 | 0.8 |
| 4 | 20 | 10 | 20 | 0.65 |
| 5–6 | 20 | 10 | 20 | 0.55 |
| 7 | 10 | 5 | 10 | 0.45 |
| 8 | 10 | 5 | 10 | 0.4 |
| 9 | 5 | — | 5 | 0.45 |
| 10 | 5 | 1 | 5 | 0.4 |
| 11 | 5 | 1 | 5 | 0.35 |
| 12 | 5 | 1 | 5 | 0.3 |
| 13 | 5 | 1 | 5 | 0.25 |

LOD 11–13 reuse the same 5°/1° grid density as LOD 10 with progressively smaller label sizes. Finer grid spacings (e.g. 1° major) are avoided because they generate ~64,000 labels which causes crashes.

### Visual Updates (Per Frame)

- **Line width**: Scaled by `distToCenter / referenceDistance` so lines maintain consistent screen thickness.
- **Minor line alpha**: `lerp(0.3, 1.0, lodProgress)` — fades in smoothly as the camera zooms closer within the LOD band.
- **Label billboard**: Rotated to face camera. Scaled by `(distToLabel / referenceDistance) × labelScale × fovFactor`. Depth boost: labels facing the camera directly appear ~1.3× larger than edge labels.
- **Label culling**: Hidden when `dot(labelDir, camDir) < 0.2` (backfacing to camera).

## Elevation Tile LOD Mapping

`ElevationTileLoader` maps the EarthCamera LOD [0–13] to tile LOD range from the manifest:

```
tileLod = lerp(manifest.lod_range[0], manifest.lod_range[1], earthLod / 13)
```

The configurable `minLod` (default 0) and `maxLod` (default 13) further clamp the tile LOD range.
