using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

[ExecuteAlways]
[RequireComponent(typeof(CubeSphere))]
public class ElevationTileLoader : MonoBehaviour
{
    [Header("Server")]
    public string serverUrl = "http://localhost:8000";

    [Header("LOD")]
    [Tooltip("Tile LOD derived from EarthCamera. Updated automatically at runtime.")]
    public int targetLod = 2;

    [Tooltip("Minimum tile LOD to load. Clamped to manifest range.")]
    public int minLod = 0;
    [Tooltip("Maximum tile LOD to load. Clamped to manifest range.")]
    public int maxLod = 13;

    private CubeSphere _cubeSphere;
    private TileManifest _manifest;
    private Material _elevationShaderMat;
    private Coroutine _loadCoroutine;
    private bool _isLoading;
    private int _loadedTileLod = -1;

    [Serializable]
    public class TileInfo
    {
        public int face;
        public int level;
        public int ix;
        public int iy;
        public string path;
        public float elevation_min;
        public float elevation_max;
        public float elevation_mean;
    }

    [Serializable]
    public class TileManifest
    {
        public string version;
        public string format;
        public string elevation_unit;
        public float sphere_radius;
        public float elevation_base_scale;
        public int resolution;
        public int[] lod_range;
        public int tile_count;
        public TileInfo[] tiles;
    }

    void OnEnable()
    {
        _cubeSphere = GetComponent<CubeSphere>();

        Shader elevShader = Shader.Find("CubeSphere/ElevationColorRamp");
        if (elevShader != null)
            _elevationShaderMat = new Material(elevShader);
        else
            Debug.LogWarning("ElevationColorRamp shader not found");

        _loadCoroutine = StartCoroutine(LoadManifestAndTiles());
    }

    void OnDisable()
    {
        if (_loadCoroutine != null)
        {
            StopCoroutine(_loadCoroutine);
            _loadCoroutine = null;
        }
        _isLoading = false;

        if (EarthCamera.Instance != null)
            EarthCamera.Instance.OnLodChanged -= OnEarthLodChanged;
    }


    void OnEarthLodChanged(int earthLod)
    {
        if (_manifest == null || _isLoading) return;

        int newTileLod = EarthLodToTileLod(earthLod);
        if (newTileLod != _loadedTileLod)
        {
            targetLod = newTileLod;
            ReloadTiles();
        }
    }

    int EarthLodToTileLod(int earthLod)
    {
        if (_manifest == null || _manifest.lod_range == null || _manifest.lod_range.Length < 2)
            return targetLod;

        int minTile = Mathf.Max(_manifest.lod_range[0], minLod);
        int maxTile = Mathf.Min(_manifest.lod_range[1], maxLod);
        if (minTile > maxTile) minTile = maxTile;
        // Map earth LOD [0,13] to tile LOD [minTile, maxTile]
        float t = earthLod / 13f;
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minTile, maxTile, t)), minTile, maxTile);
    }

    public void ReloadTiles()
    {
        if (_isLoading || _manifest == null) return;
        _loadCoroutine = StartCoroutine(ReloadTilesCoroutine());
    }

    IEnumerator ReloadTilesCoroutine()
    {
        _isLoading = true;

        ApplyOceanToAllFaces();
        yield return LoadBestLodPerFace();

        FinishLoading();
    }

    IEnumerator LoadManifestAndTiles()
    {
        _isLoading = true;

        string url = $"{serverUrl}/manifest.json";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to load manifest: {req.error}");
                FinishLoading();
                yield break;
            }
            _manifest = JsonUtility.FromJson<TileManifest>(req.downloadHandler.text);
            Debug.Log($"Manifest loaded: {_manifest.tile_count} tiles, radius={_manifest.sphere_radius}, res={_manifest.resolution}");
        }

        if (_cubeSphere != null && Mathf.Abs(_cubeSphere.radius - _manifest.sphere_radius) > 0.01f)
        {
            _cubeSphere.radius = _manifest.sphere_radius;
            _cubeSphere.GenerateCubeSphere();
            LatLonGrid grid = GetComponent<LatLonGrid>();
            if (grid != null) grid.GenerateGrid();
            GpsMarker gps = GetComponentInChildren<GpsMarker>();
            if (gps != null) gps.UpdatePosition();
        }

        // Subscribe to EarthCamera LOD changes
        if (EarthCamera.Instance != null)
        {
            targetLod = EarthLodToTileLod(EarthCamera.Instance.currentLod);
            EarthCamera.Instance.OnLodChanged += OnEarthLodChanged;
        }

        ApplyOceanToAllFaces();
        yield return LoadBestLodPerFace();

        FinishLoading();
    }

    void FinishLoading()
    {
        _isLoading = false;
        _loadCoroutine = null;
    }

    void ApplyOceanToAllFaces()
    {
        if (_elevationShaderMat == null) return;
        Texture2D oceanTex = new Texture2D(4, 4, TextureFormat.RFloat, false);
        oceanTex.filterMode = FilterMode.Bilinear;
        Color[] fill = new Color[16];
        oceanTex.SetPixels(fill);
        oceanTex.Apply();

        for (int f = 0; f < 6; f++)
            _cubeSphere.SetFaceElevationTexture(f, oceanTex, -100, 100, _elevationShaderMat);
    }

    IEnumerator LoadBestLodPerFace()
    {
        Dictionary<int, List<TileInfo>> tilesByFace = new Dictionary<int, List<TileInfo>>();
        foreach (var t in _manifest.tiles)
        {
            if (!tilesByFace.ContainsKey(t.face))
                tilesByFace[t.face] = new List<TileInfo>();
            tilesByFace[t.face].Add(t);
        }

        foreach (var kvp in tilesByFace)
        {
            int face = kvp.Key;
            List<TileInfo> faceTiles = kvp.Value;

            int bestLod = 0;
            foreach (var t in faceTiles)
            {
                if (t.level <= targetLod && t.level > bestLod)
                    bestLod = t.level;
            }

            List<TileInfo> lodTiles = new List<TileInfo>();
            foreach (var t in faceTiles)
            {
                if (t.level == bestLod)
                    lodTiles.Add(t);
            }

            Debug.Log($"Face {face}: loading {lodTiles.Count} tiles at LOD {bestLod}");
            yield return LoadAndCompositeFace(face, bestLod, lodTiles);
        }

        _loadedTileLod = targetLod;
    }

    IEnumerator LoadAndCompositeFace(int face, int lod, List<TileInfo> tiles)
    {
        int tileRes = _manifest.resolution;
        int gridSize = 1 << lod;
        int faceRes = gridSize * tileRes;

        int maxFaceRes = 4096;
        float scale = 1f;
        if (faceRes > maxFaceRes)
        {
            scale = (float)maxFaceRes / faceRes;
            faceRes = maxFaceRes;
        }

        Texture2D faceTex = new Texture2D(faceRes, faceRes, TextureFormat.RFloat, false);
        faceTex.filterMode = FilterMode.Bilinear;
        faceTex.wrapMode = TextureWrapMode.Clamp;

        Color[] fill = new Color[faceRes * faceRes];
        faceTex.SetPixels(fill);

        float globalMin = float.MaxValue;
        float globalMax = float.MinValue;

        foreach (var tile in tiles)
        {
            globalMin = Mathf.Min(globalMin, tile.elevation_min);
            globalMax = Mathf.Max(globalMax, tile.elevation_max);

            string url = $"{serverUrl}/{tile.path}";
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Failed: {tile.path}: {req.error}");
                    continue;
                }

                Texture2D tileTex = new Texture2D(2, 2, TextureFormat.RGBAFloat, false);
                if (!ImageConversion.LoadImage(tileTex, req.downloadHandler.data))
                {
                    Debug.LogWarning($"Failed to decode: {tile.path}");
                    SafeDestroy(tileTex);
                    continue;
                }

                int destX = Mathf.RoundToInt(tile.ix * tileRes * scale);
                int destY = Mathf.RoundToInt(tile.iy * tileRes * scale);
                int destW = Mathf.RoundToInt(tileRes * scale);
                int destH = Mathf.RoundToInt(tileRes * scale);

                destW = Mathf.Min(destW, faceRes - destX);
                destH = Mathf.Min(destH, faceRes - destY);

                if (destW > 0 && destH > 0)
                {
                    Color[] tilePixels = new Color[destW * destH];
                    for (int y = 0; y < destH; y++)
                    {
                        float ty = (float)y / destH;
                        for (int x = 0; x < destW; x++)
                        {
                            float tx = (float)x / destW;
                            Color c = tileTex.GetPixelBilinear(tx, ty);
                            tilePixels[y * destW + x] = new Color(c.r, 0, 0, 1);
                        }
                    }
                    faceTex.SetPixels(destX, destY, destW, destH, tilePixels);
                }

                SafeDestroy(tileTex);
            }
        }

        faceTex.Apply();

        if (globalMin == float.MaxValue) globalMin = 0;
        if (globalMax == float.MinValue) globalMax = 1;

        Debug.Log($"Face {face} composite: {faceRes}x{faceRes}, elev=[{globalMin:F1}, {globalMax:F1}]");

        if (_elevationShaderMat != null)
        {
            _cubeSphere.SetFaceElevationTexture(face, faceTex, globalMin, globalMax, _elevationShaderMat);
        }
    }

    static void SafeDestroy(UnityEngine.Object obj)
    {
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
