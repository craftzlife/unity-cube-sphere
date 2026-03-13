# Camera & LOD System

## EarthCamera

An orbit camera that revolves around the globe origin.

### Controls (Play Mode)

| Input | Action |
|-------|--------|
| Left mouse drag | Rotate orbit (yaw + pitch) |
| Scroll wheel | Zoom in/out |

### Orbit Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `distance` | 300 | Current orbit distance |
| `minDistance` | 101 | Closest zoom (just above surface, radius=100) |
| `maxDistance` | 300 | Farthest zoom |
| `rotationSpeed` | 5 | Mouse sensitivity multiplier |
| `zoomSpeed` | 50 | Scroll sensitivity multiplier |

### Distance-Scaled Rotation

Rotation speed scales cubically with distance so dragging feels consistent at all zoom levels:

```
ratio = distance / maxDistance
distanceFactor = ratio³
yaw  += delta.x × rotationSpeed × 0.1 × distanceFactor
pitch -= delta.y × rotationSpeed × 0.1 × distanceFactor
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

On `Start()`, if a `GpsMarker` exists, the camera aligns yaw/pitch to look at the marker's lat/lon position on the globe.

## LOD System

### Computation

LOD is computed logarithmically from the camera's effective distance:

```
t = InverseLerp(log(minDist), log(maxDist), log(distance))
continuous = (1 − t) × 10
lod = round(continuous), clamped to [0, 10]
```

- **LOD 0** = farthest (coarsest detail)
- **LOD 10** = closest (finest detail)

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

### Visual Updates (Per Frame)

- **Line width**: Scaled by `distToCenter / referenceDistance` so lines maintain consistent screen thickness.
- **Minor line alpha**: `lerp(0.3, 1.0, lodProgress)` — fades in smoothly as the camera zooms closer within the LOD band.
- **Label billboard**: Rotated to face camera. Scaled by `(distToLabel / referenceDistance) × labelScale × fovFactor`. Depth boost: labels facing the camera directly appear ~1.3× larger than edge labels.
- **Label culling**: Hidden when `dot(labelDir, camDir) < 0.2` (backfacing to camera).

## Elevation Tile LOD Mapping

`ElevationTileLoader` maps the EarthCamera LOD [0–10] to tile LOD range from the manifest:

```
tileLod = lerp(manifest.lod_range[0], manifest.lod_range[1], earthLod / 10)
```
