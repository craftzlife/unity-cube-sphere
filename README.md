# Unity CubeSphere Earth

A Unity 6 (URP) application that renders Earth as a cube-sphere using S2 geometry, with elevation-colored terrain, latitude/longitude grid overlay, GPS marker, procedural star skybox, and real-time sunlight.

## Requirements

- **Unity** 6000.3.10f1 (Unity 6)
- **Render Pipeline**: Universal Render Pipeline (URP)
- **Input System**: New Input System package

## Project Structure

```
Assets/
├── Scripts/CubeSphere/
│   ├── CubeSphere.cs          — 6-face sphere mesh using S2 projection
│   ├── S2Geometry.cs          — lat/lon, face/UV, coordinate conversions
│   ├── EarthCamera.cs         — orbit camera, LOD [0–13], dynamic clip planes
│   ├── ElevationTileLoader.cs — fetches & composites elevation tiles from server
│   ├── LatLonGrid.cs          — procedural grid lines + labels with 14-level LOD
│   ├── GpsMarker.cs           — pulsing GPS dot at lat/lon/altitude
│   └── RealtimeSunLight.cs    — UTC-based sun direction for directional light
├── Shaders/
│   ├── ElevationColorRamp.shader    — water/land color ramp with day/night shading
│   └── ProceduralStarSkybox.shader  — hash-based procedural star field
└── Scenes/
    └── GlobalScene.unity
```

## Scene Hierarchy

```
CubeSphere (root)
├── CubeSphere          — generates 6 face meshes (DontSave)
├── ElevationTileLoader — loads tiles from HTTP server
├── LatLonGrid          — grid lines at 9 km altitude, labels at 10 km
└── GpsMarker           — default: Ho Chi Minh City (10.82°N, 106.63°E)

EarthCamera
└── Camera + orbit controller + LOD computation

Directional Light
└── RealtimeSunLight    — real-time solar position
```

## Key Features

- **S2 Cube-Sphere**: Google S2 geometry projection with linear ST-UV transform for uniform face distribution
- **14-Level LOD** (0–13): Logarithmic distance mapping with FOV correction; drives both elevation tile loading and grid detail
- **Elevation Tiles**: Fetched from a local tile server (`localhost:8000`), composited per-face as RFloat textures, rendered with a water/land color ramp shader
- **Lat/Lon Grid**: Major/minor lines from 20° down to 5°/1° spacing; grid density capped at LOD 10 level for LOD 11–13 to prevent excessive object creation
- **Earth Rotation**: Axial tilt (23.44°) with seasonal axis + time-of-day rotation (15°/hour from UTC noon)
- **Real-Time Sunlight**: Solar declination and subsolar longitude computed from UTC; configurable custom time override
- **GPS Marker**: Constant screen-size dot with pulse animation; supports device GPS on mobile
- **Procedural Skybox**: Hash-based star field with configurable density, brightness, and size

## Running

1. Open the project in Unity 6
2. Open `Assets/Scenes/GlobalScene.unity`
3. Start the elevation tile server on `localhost:8000` (serves `manifest.json` and tile PNGs)
4. Enter Play Mode

The globe renders in both editor and play mode (`[ExecuteAlways]`). Elevation tiles require the tile server to be running.

## Documentation

Detailed design docs are in the [`docs/`](docs/) directory:

- [Architecture](docs/architecture.md) — component hierarchy, script roles, data flow
- [Camera & LOD](docs/camera-and-lod.md) — orbit controls, LOD computation, grid LOD table
- [Coordinate System](docs/coordinate-system.md) — S2 geometry, face mapping, Earth rotation model
- [Elevation Data Pipeline](docs/elevation-data-pipeline.md) — tile server, compositing, shader color ramps
- [Skybox & Lighting](docs/skybox-and-lighting.md) — procedural stars, real-time sun model
