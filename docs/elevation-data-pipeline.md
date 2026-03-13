# Elevation Data Pipeline

## Overview

Elevation data is served from a local HTTP tile server and rendered as colored terrain on the cube-sphere faces.

## Tile Server

- **URL**: `http://localhost:8000` (configurable via inspector)
- **Manifest**: `GET /manifest.json`
- **Tiles**: `GET /{tile.path}` (PNG-encoded RFloat data)

## Manifest Schema

```json
{
  "version": "string",
  "format": "string",
  "elevation_unit": "string",
  "sphere_radius": 100.0,
  "elevation_base_scale": 1.0,
  "resolution": 256,
  "lod_range": [0, 3],
  "tile_count": 24,
  "tiles": [
    {
      "face": 0,
      "level": 2,
      "ix": 0,
      "iy": 1,
      "path": "tiles/face0/lod2/tile_0_1.png",
      "elevation_min": -100.5,
      "elevation_max": 2500.3,
      "elevation_mean": 450.0
    }
  ]
}
```

## Loading Sequence

1. **Fetch manifest** → parse `TileManifest`
2. **Sync radius**: If manifest's `sphere_radius` differs from `CubeSphere.radius`, update the sphere mesh, grid, and GPS marker
3. **Subscribe** to `EarthCamera.OnLodChanged`
4. **Apply ocean baseline**: Fill all 6 faces with a small uniform RFloat texture (zero elevation)
5. **Load best LOD per face**:
   - Group tiles by face
   - For each face, find the highest available LOD ≤ `targetLod`
   - Load and composite tiles into a single face texture

## Tile Compositing

Each face's final texture is composited from sub-tiles:

```
gridSize = 2^lod                    (tiles per axis at this LOD)
faceRes  = gridSize × tileResolution
```

If `faceRes > 4096`, tiles are downscaled to fit. Sub-tiles are bilinear-sampled and placed at their `(ix, iy)` grid position.

The resulting `Texture2D` (RFloat, bilinear, clamp) is assigned via `CubeSphere.SetFaceElevationTexture()`.

## ElevationColorRamp Shader

### Properties

| Property | Default | Description |
|----------|---------|-------------|
| `_MainTex` | — | RFloat elevation texture |
| `_ElevMin` | -100 | Minimum elevation (from manifest) |
| `_ElevMax` | 2000 | Maximum elevation (from manifest) |
| `_ElevScale` | 1.0 | Elevation multiplier |
| `_MaxLandElev` | 1500 | Scale for land color ramp normalization |
| `_MaxDepth` | 100 | Scale for water depth normalization |

### Color Mapping

**Water** (elevation ≤ 0):
```
depth01 = clamp(-elevation / MaxDepth, 0, 1)
color = lerp(shallow_blue, deep_blue, depth01)
```

**Land** (elevation > 0):
```
height01 = clamp(elevation / MaxLandElev, 0, 1)

0.00–0.05: coast green → lowland green
0.05–0.15: lowland green → midland olive
0.15–0.35: midland olive → highland brown
0.35–0.65: highland brown → mountain brown
0.65–1.00: mountain brown → snow white
```

### Lighting

- Main directional light with `saturate(N·L)` diffuse
- Ambient: `(0.30, 0.30, 0.35)` — slight blue tint
- Combined: `color *= ambient + NdotL × 0.70 × lightColor`
