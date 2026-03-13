# Skybox & Lighting

## Procedural Star Skybox

**Shader**: `Skybox/ProceduralStarSkybox`
**Material**: `Assets/Materials/StarSkybox.mat`

Generates a star field procedurally using hash-based pseudo-random star placement.

### Parameters

| Property | Default | Description |
|----------|---------|-------------|
| `_StarDensity` | 80 | Number of cells per radian (higher = more stars) |
| `_StarBrightness` | 1.5 | Global brightness multiplier |
| `_StarSize` | 0.015 | Star disc radius in UV space |

### Algorithm

1. Convert view direction to spherical UV coordinates
2. Tile UV space into cells scaled by `_StarDensity`
3. Hash each cell to determine star position offset and brightness
4. Apply cubic power curve to brightness (`pow(hash, 3)`) — most stars dim, few bright
5. `smoothstep` disc falloff for each star point

## RealtimeSunLight

Rotates a directional light to match the real-world sun position based on UTC time.

### Solar Model

1. **Declination** (latitude of subsolar point):
   ```
   declination = 23.44° × sin(2π / 365 × (dayOfYear − 81))
   ```
   Ranges from −23.44° (winter solstice) to +23.44° (summer solstice).

2. **Subsolar longitude**:
   ```
   subsolarLon = −15° × (utcHours − 12)
   ```
   Sun is directly over longitude 0° at UTC noon. Moves 15°/hour westward.

3. **Sun direction**: Converted from lat/lon to Unity world position, then rotated by `CubeSphere.EarthRotation` (which includes axial tilt).

4. **Light rotation**: `Quaternion.LookRotation(-sunDir)` — light shines from sun toward the origin.

### Custom Time Override

Inspector toggle `useCustomTime` allows manual control of month/day/hour/minute for testing different lighting conditions without waiting for real time.
