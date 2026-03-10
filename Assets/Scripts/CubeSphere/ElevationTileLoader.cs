using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(CubeSphere))]
public class ElevationTileLoader : MonoBehaviour
{
    [Header("Server")]
    public string serverUrl = "http://localhost:8000";

    [Header("LOD")]
    [Tooltip("Which LOD level to load. Higher = more tiles, more detail.")]
    public int targetLod = 2;

    private CubeSphere _cubeSphere;
    private TileManifest _manifest;
    private Material _elevationShaderMat;
    private Coroutine _loadCoroutine;
    private bool _isLoading;

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
#if UNITY_EDITOR
        EditorApplication.update -= EditorPumpUpdate;
#endif
    }

#if UNITY_EDITOR
    void EditorPumpUpdate()
    {
        if (!Application.isPlaying && _isLoading)
            EditorApplication.QueuePlayerLoopUpdate();
    }
#endif

    IEnumerator LoadManifestAndTiles()
    {
        _isLoading = true;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorApplication.update += EditorPumpUpdate;
#endif

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

        // Apply elevation shader to ALL 6 faces first (consistent ocean look)
        ApplyOceanToAllFaces();

        // Then load and composite tile data for faces that have it
        yield return LoadBestLodPerFace();

        FinishLoading();
    }

    void FinishLoading()
    {
        _isLoading = false;
        _loadCoroutine = null;
#if UNITY_EDITOR
        EditorApplication.update -= EditorPumpUpdate;
#endif
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
        // Group tiles by face
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

            // Find best LOD that's <= targetLod
            int bestLod = 0;
            foreach (var t in faceTiles)
            {
                if (t.level <= targetLod && t.level > bestLod)
                    bestLod = t.level;
            }

            // Collect tiles at best LOD
            List<TileInfo> lodTiles = new List<TileInfo>();
            foreach (var t in faceTiles)
            {
                if (t.level == bestLod)
                    lodTiles.Add(t);
            }

            Debug.Log($"Face {face}: loading {lodTiles.Count} tiles at LOD {bestLod}");
            yield return LoadAndCompositeFace(face, bestLod, lodTiles);
        }
    }

    IEnumerator LoadAndCompositeFace(int face, int lod, List<TileInfo> tiles)
    {
        int tileRes = _manifest.resolution; // 256
        int gridSize = 1 << lod; // 2^lod tiles per axis
        int faceRes = gridSize * tileRes;

        // Cap texture size to 4096
        int maxFaceRes = 4096;
        float scale = 1f;
        if (faceRes > maxFaceRes)
        {
            scale = (float)maxFaceRes / faceRes;
            faceRes = maxFaceRes;
        }

        // Create face composite texture
        Texture2D faceTex = new Texture2D(faceRes, faceRes, TextureFormat.RFloat, false);
        faceTex.filterMode = FilterMode.Bilinear;
        faceTex.wrapMode = TextureWrapMode.Clamp;

        // Fill with zero (ocean)
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

                // Copy tile pixels into composite at the correct position
                int destX = Mathf.RoundToInt(tile.ix * tileRes * scale);
                int destY = Mathf.RoundToInt(tile.iy * tileRes * scale);
                int destW = Mathf.RoundToInt(tileRes * scale);
                int destH = Mathf.RoundToInt(tileRes * scale);

                // Clamp to texture bounds
                destW = Mathf.Min(destW, faceRes - destX);
                destH = Mathf.Min(destH, faceRes - destY);

                if (destW > 0 && destH > 0)
                {
                    // If we need to resize tile, use GetPixelBilinear
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
