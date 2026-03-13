using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(CubeSphere))]
public class LatLonGrid : MonoBehaviour
{
    [Header("Grid Colors")]
    public Color latitudeColor = new Color(1f, 1f, 1f, 0.35f);
    public Color longitudeColor = new Color(1f, 1f, 1f, 0.35f);
    public Color equatorColor = new Color(1f, 0.8f, 0f, 0.6f);
    public Color primeMeridianColor = new Color(1f, 0.2f, 0.2f, 0.6f);
    public float lineWidth = 0.5f;
    [Range(36, 360)]
    public int pointsPerLine = 180;

    [Header("Minor Line Settings")]
    [Range(0.05f, 0.5f)]
    public float minorAlphaBase = 0.15f;
    [Range(0.3f, 1f)]
    public float minorWidthFactor = 0.6f;

    [Header("Label Settings")]
    public Color labelColor = new Color(1f, 0.95f, 0f, 1f);
    [Range(0.1f, 5f)]
    public float labelScale = 1f;

    private CubeSphere _cubeSphere;
    private GameObject _gridParent;
    private int _currentLod = -1;
    private float _gridCreationDist;

    // Cached renderers for fast per-frame updates
    private LineRenderer[] _majorLines;
    private LineRenderer[] _minorLines;
    private Color[] _minorBaseColors;

    struct GridParams
    {
        public float majorSpacing;
        public float minorSpacing; // 0 = no minor lines
        public float labelSpacing;
        public float charSize;
    }

    // LOD 0 = farthest (coarsest), LOD 10 = closest (finest)
    private static readonly GridParams[] LodToGrid = {
        new GridParams { majorSpacing = 30f, minorSpacing = 0f,  labelSpacing = 30f, charSize = 1.2f  }, // LOD 0
        new GridParams { majorSpacing = 30f, minorSpacing = 0f,  labelSpacing = 30f, charSize = 1.1f  }, // LOD 1
        new GridParams { majorSpacing = 30f, minorSpacing = 15f, labelSpacing = 30f, charSize = 0.9f  }, // LOD 2
        new GridParams { majorSpacing = 30f, minorSpacing = 15f, labelSpacing = 30f, charSize = 0.8f  }, // LOD 3
        new GridParams { majorSpacing = 15f, minorSpacing = 5f,  labelSpacing = 30f, charSize = 0.65f }, // LOD 4
        new GridParams { majorSpacing = 15f, minorSpacing = 5f,  labelSpacing = 30f, charSize = 0.55f }, // LOD 5
        new GridParams { majorSpacing = 10f, minorSpacing = 5f,  labelSpacing = 30f, charSize = 0.45f }, // LOD 6
        new GridParams { majorSpacing = 10f, minorSpacing = 5f,  labelSpacing = 30f, charSize = 0.4f  }, // LOD 7
        new GridParams { majorSpacing = 10f, minorSpacing = 2f,  labelSpacing = 30f, charSize = 0.3f  }, // LOD 8
        new GridParams { majorSpacing = 10f, minorSpacing = 2f,  labelSpacing = 15f, charSize = 0.25f }, // LOD 9
        new GridParams { majorSpacing = 5f,  minorSpacing = 1f,  labelSpacing = 15f, charSize = 0.2f  }, // LOD 10
    };

    void OnEnable()
    {
        _cubeSphere = GetComponent<CubeSphere>();
#if UNITY_EDITOR
        SceneView.duringSceneGui += OnSceneGUI;
#endif
        _currentLod = -1;
        CheckLod();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui -= OnSceneGUI;
#endif
        if (_gridParent != null)
            DestroyImmediate(_gridParent);
        _currentLod = -1;
        _majorLines = null;
        _minorLines = null;
        _minorBaseColors = null;
    }

#if UNITY_EDITOR
    void OnSceneGUI(SceneView sv)
    {
        if (this == null || !isActiveAndEnabled) return;
        if (!Application.isPlaying)
        {
            CheckLodWithCamera(sv.camera);
            UpdateVisuals(sv.camera);
        }
    }
#endif

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        _currentLod = -1;
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
                CheckLod();
        };
#endif
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        CheckLodWithCamera(cam);
        UpdateVisuals(cam);
    }

    void CheckLod()
    {
        Camera cam = GetActiveCamera();
        if (cam != null)
            CheckLodWithCamera(cam);
    }

    void CheckLodWithCamera(Camera cam)
    {
        if (cam == null) return;
        if (_cubeSphere == null) _cubeSphere = GetComponent<CubeSphere>();

        float dist = Vector3.Distance(cam.transform.position, transform.position);

        // Use EarthCamera LOD in play mode, compute from distance in editor
        int newLod;
        if (Application.isPlaying && EarthCamera.Instance != null)
            newLod = EarthCamera.Instance.currentLod;
        else
            newLod = EarthCamera.ComputeLod(dist);

        if (newLod != _currentLod)
        {
            _currentLod = newLod;
            _gridCreationDist = dist;
            RebuildGrid(LodToGrid[newLod]);
        }
    }

    /// <summary>Called externally (e.g. after radius change). Forces LOD re-evaluation.</summary>
    public void GenerateGrid()
    {
        _currentLod = -1;
        CheckLod();
    }

    Camera GetActiveCamera()
    {
        if (Application.isPlaying)
            return Camera.main;
#if UNITY_EDITOR
        var sv = SceneView.lastActiveSceneView;
        if (sv != null) return sv.camera;
#endif
        return Camera.main;
    }

    void RebuildGrid(GridParams gp)
    {
        if (_gridParent != null)
            DestroyImmediate(_gridParent);

        _gridParent = new GameObject("LatLonGrid");
        _gridParent.transform.SetParent(transform, false);
        _gridParent.hideFlags = HideFlags.DontSave;

        float r = _cubeSphere != null ? _cubeSphere.radius : 100f;
        float lineR = r * 1.005f;
        float labelR = r * 1.04f;

        var majorList = new System.Collections.Generic.List<LineRenderer>();
        var minorList = new System.Collections.Generic.List<LineRenderer>();
        var minorColorList = new System.Collections.Generic.List<Color>();

        // --- Major latitude lines ---
        for (float lat = -90f + gp.majorSpacing; lat < 90f; lat += gp.majorSpacing)
        {
            bool isEquator = Mathf.Abs(lat) < 0.01f;
            Color col = isEquator ? equatorColor : latitudeColor;
            float w = isEquator ? lineWidth * 2.5f : lineWidth;
            majorList.Add(CreateLatitudeLine(lat, lineR, col, w));
        }

        // --- Major longitude lines ---
        for (float lon = -180f; lon < 180f; lon += gp.majorSpacing)
        {
            bool isPrime = Mathf.Abs(lon) < 0.01f;
            Color col = isPrime ? primeMeridianColor : longitudeColor;
            float w = isPrime ? lineWidth * 2.5f : lineWidth;
            majorList.Add(CreateLongitudeLine(lon, lineR, col, w));
        }

        // --- Minor latitude lines (skip positions that coincide with major) ---
        if (gp.minorSpacing > 0f)
        {
            for (float lat = -90f + gp.minorSpacing; lat < 90f; lat += gp.minorSpacing)
            {
                if (IsMajorPosition(lat, gp.majorSpacing)) continue;
                Color col = latitudeColor;
                col.a *= minorAlphaBase;
                minorColorList.Add(col);
                minorList.Add(CreateLatitudeLine(lat, lineR, col, lineWidth * minorWidthFactor));
            }

            // --- Minor longitude lines ---
            for (float lon = -180f; lon < 180f; lon += gp.minorSpacing)
            {
                if (IsMajorPosition(lon, gp.majorSpacing)) continue;
                Color col = longitudeColor;
                col.a *= minorAlphaBase;
                minorColorList.Add(col);
                minorList.Add(CreateLongitudeLine(lon, lineR, col, lineWidth * minorWidthFactor));
            }
        }

        _majorLines = majorList.ToArray();
        _minorLines = minorList.ToArray();
        _minorBaseColors = minorColorList.ToArray();

        // --- Labels at major intersections only ---
        GameObject labelsGo = new GameObject("Labels");
        labelsGo.transform.SetParent(_gridParent.transform, false);

        for (float lat = -90f + gp.labelSpacing; lat < 90f; lat += gp.labelSpacing)
        {
            string latText = Mathf.Abs(lat) < 0.01f
                ? "0°"
                : $"{Mathf.Abs(lat):F0}°{(lat > 0 ? "N" : "S")}";

            for (float lon = -180f; lon < 180f; lon += gp.labelSpacing)
            {
                string lonText;
                if (Mathf.Abs(lon) < 0.01f) lonText = "0°";
                else if (lon > 0) lonText = $"{lon:F0}°E";
                else lonText = $"{-lon:F0}°W";

                string text = $"{latText}\n{lonText}";
                Vector3 pos = S2Geometry.LatLonToUnityPosition(lat, lon, labelR);
                CreateLabel(labelsGo.transform, text, pos, gp.charSize);
            }
        }
    }

    static bool IsMajorPosition(float value, float majorSpacing)
    {
        float remainder = Mathf.Abs(value % majorSpacing);
        return remainder < 0.01f || (majorSpacing - remainder) < 0.01f;
    }

    void UpdateVisuals(Camera cam)
    {
        if (_gridParent == null || cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 sphereCenter = transform.position;
        Vector3 camDir = (camPos - sphereCenter).normalized;
        float distToCenter = Vector3.Distance(camPos, sphereCenter);

        float referenceDist = _gridCreationDist > 0f ? _gridCreationDist : distToCenter;
        float lineScale = distToCenter / referenceDist;

        // Scale major lines
        if (_majorLines != null)
        {
            for (int i = 0; i < _majorLines.Length; i++)
            {
                if (_majorLines[i] != null)
                    _majorLines[i].widthMultiplier = lineScale;
            }
        }

        // Scale and fade minor lines based on lodProgress
        if (_minorLines != null && _minorBaseColors != null)
        {
            float progress = 0.5f;
            if (Application.isPlaying && EarthCamera.Instance != null)
                progress = EarthCamera.Instance.lodProgress;

            // lodProgress near 0 → 0.3× base alpha; near 1 → 1.0× base alpha
            float alphaMultiplier = Mathf.Lerp(0.3f, 1f, progress);

            for (int i = 0; i < _minorLines.Length; i++)
            {
                if (_minorLines[i] == null) continue;
                _minorLines[i].widthMultiplier = lineScale;

                Color c = _minorBaseColors[i];
                c.a *= alphaMultiplier;
                _minorLines[i].startColor = c;
                _minorLines[i].endColor = c;
            }
        }

        // Labels
        Transform labels = _gridParent.transform.Find("Labels");
        if (labels == null) return;

        Quaternion rot = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

        // FOV-based scaling: keep labels at a consistent apparent size across FOV changes
        const float referenceFov = 60f;
        float fovFactor = Mathf.Tan(referenceFov * 0.5f * Mathf.Deg2Rad)
                        / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        foreach (Transform child in labels)
        {
            Vector3 labelDir = (child.position - sphereCenter).normalized;
            float dot = Vector3.Dot(labelDir, camDir);
            bool visible = dot > 0.2f;
            child.gameObject.SetActive(visible);
            if (visible)
            {
                child.rotation = rot;
                float distToLabel = Vector3.Distance(camPos, child.position);
                // Constant screen size: cancels perspective so labels don't grow/shrink with zoom
                float baseScale = (distToLabel / referenceDist) * labelScale * fovFactor;
                // Depth boost: labels facing the camera appear larger than those near the edges
                float depthFactor = Mathf.Lerp(0.7f, 1.3f, Mathf.InverseLerp(0.2f, 1f, dot));
                child.localScale = Vector3.one * baseScale * depthFactor;
            }
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            SceneView.RepaintAll();
#endif
    }

    // ----------------------------------------------------------------
    // Grid line helpers
    // ----------------------------------------------------------------

    LineRenderer CreateLatitudeLine(float lat, float radius, Color color, float width)
    {
        LineRenderer lr = CreateLineRenderer($"Lat_{lat:F0}", color, width);
        lr.positionCount = pointsPerLine + 1;
        lr.loop = true;

        for (int i = 0; i <= pointsPerLine; i++)
        {
            float lon = -180f + 360f * i / pointsPerLine;
            lr.SetPosition(i, S2Geometry.LatLonToUnityPosition(lat, lon, radius));
        }
        return lr;
    }

    LineRenderer CreateLongitudeLine(float lon, float radius, Color color, float width)
    {
        LineRenderer lr = CreateLineRenderer($"Lon_{lon:F0}", color, width);
        lr.positionCount = pointsPerLine + 1;
        lr.loop = false;

        for (int i = 0; i <= pointsPerLine; i++)
        {
            float lat = -90f + 180f * i / pointsPerLine;
            lr.SetPosition(i, S2Geometry.LatLonToUnityPosition(lat, lon, radius));
        }
        return lr;
    }

    LineRenderer CreateLineRenderer(string name, Color color, float width)
    {
        GameObject lineObj = new GameObject(name);
        lineObj.transform.SetParent(_gridParent.transform, false);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.useWorldSpace = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    // ----------------------------------------------------------------
    // Label helper
    // ----------------------------------------------------------------

    void CreateLabel(Transform parent, string text, Vector3 localPos, float charSize)
    {
        GameObject go = new GameObject(text);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = charSize;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = labelColor;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }
}
