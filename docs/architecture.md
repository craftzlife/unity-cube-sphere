# Project Architecture

A Unity (URP) application that renders Earth as a cube-sphere with S2 geometry, elevation-colored terrain tiles, a latitude/longitude grid overlay, GPS marker, procedural star skybox, and real-time sunlight.

## Component Hierarchy

```
CubeSphere (GameObject, root)
├── CubeSphere          — generates the 6-face sphere mesh
├── ElevationTileLoader — fetches elevation data from a tile server and composites textures
├── LatLonGrid          — procedural lat/lon grid lines + labels with LOD
├── GpsMarker           — pulsing GPS dot pinned to lat/lon
└── [child faces]       — 6 auto-generated Face0–Face5 GameObjects (HideFlags.DontSave)

EarthCamera (separate GameObject)
├── Camera              — orbit camera with mouse input
└── EarthCamera         — orbit controller, LOD computation, dynamic clip planes

Directional Light
└── RealtimeSunLight    — rotates light to match real-world sun position (UTC-based)

Skybox
└── Material using Skybox/ProceduralStarSkybox shader
```

## Script Responsibilities

| Script | File | Role |
|--------|------|------|
| `CubeSphere` | `Scripts/CubeSphere/CubeSphere.cs` | Generates 6 quad meshes, projects them onto a sphere using S2 projection. Applies Earth axial tilt + time-of-day rotation on enable. Provides API to set per-face materials/textures. |
| `S2Geometry` | `Scripts/CubeSphere/S2Geometry.cs` | Static utility class. Lat/lon ↔ XYZ ↔ face/UV conversions. Handles geographic Z-up to Unity Y-up coordinate swap. |
| `EarthCamera` | `Scripts/CubeSphere/EarthCamera.cs` | Orbit camera around a target. Mouse drag rotates, scroll zooms. Computes LOD [0–10] from distance using logarithmic mapping with FOV correction. Fires `OnLodChanged` event. Dynamic near/far clip planes. |
| `ElevationTileLoader` | `Scripts/CubeSphere/ElevationTileLoader.cs` | Fetches `manifest.json` from a local tile server, then loads per-face elevation tiles. Composites sub-tiles into a single RFloat texture per face. Listens to `EarthCamera.OnLodChanged` to swap tile LOD. |
| `LatLonGrid` | `Scripts/CubeSphere/LatLonGrid.cs` | Draws latitude/longitude lines using `LineRenderer` at 9,000m altitude, labels at 10,000m. Uses a LOD table (11 levels) for major/minor line spacing. Labels at major intersections via `TextMesh`. Per-frame: scales line width by distance, fades minor lines by `lodProgress`, billboard-rotates labels. |
| `GpsMarker` | `Scripts/CubeSphere/GpsMarker.cs` | Places a sphere primitive at a lat/lon/altitude position. Altitude (meters above sea level) is converted to globe scale via `radius × (1 + alt / 6,371,000)`. Constant screen-size scaling. Pulse animation in play mode. Optional device GPS on mobile (reads altitude from device). |
| `RealtimeSunLight` | `Scripts/CubeSphere/RealtimeSunLight.cs` | Computes solar declination and subsolar longitude from UTC time. Rotates a directional light to match. Supports custom time override via inspector. |

## Shaders

| Shader | File | Purpose |
|--------|------|---------|
| `CubeSphere/ElevationColorRamp` | `Shaders/ElevationColorRamp.shader` | URP forward-lit. Reads RFloat elevation texture, maps to water/land color ramps. Supports main light with ambient. |
| `Skybox/ProceduralStarSkybox` | `Shaders/ProceduralStarSkybox.shader` | URP skybox. Hash-based procedural star field with configurable density, brightness, and size. |

## Data Flow

```
Tile Server (localhost:8000)
    │
    ├── manifest.json ─────► ElevationTileLoader
    │                              │
    └── tiles/*.png (RFloat) ──► Composite per-face Texture2D
                                       │
                                       ▼
                            CubeSphere.SetFaceElevationTexture()
                                       │
                                       ▼
                            ElevationColorRamp shader renders terrain
```

## Key Design Decisions

- **S2 Projection**: Uses Google S2 geometry's cube-to-sphere mapping with linear ST↔UV transform for uniform area distribution across faces.
- **ExecuteAlways**: All MonoBehaviours use `[ExecuteAlways]` so the globe renders in the editor without entering play mode.
- **HideFlags.DontSave**: Generated GameObjects (faces, grid lines, markers) are transient — regenerated on enable, not serialized to the scene.
- **Coordinate convention**: Internal S2 math operates in geographic Z-up coordinates. `GeoToUnity`/`UnityToGeo` perform the Y↔Z swap at the boundary.
- **Earth rotation**: Applied once in `CubeSphere.OnEnable()` — axial tilt (23.44°) with seasonal axis orientation + time-of-day rotation (15°/hour from UTC noon).
