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

    [Header("Label Settings")]
    public Color labelColor = new Color(1f, 0.95f, 0f, 1f);

    private CubeSphere _cubeSphere;
    private GameObject _gridParent;
    private int _currentLod = -1;
    private float _gridCreationDist;

    struct GridParams
    {
        public float gridSpacing;
        public float labelSpacing;
        public float charSize;
    }

    // LOD 0 = farthest (coarsest), LOD 10 = closest (finest)
    private static readonly GridParams[] LodToGrid = {
        new GridParams { gridSpacing = 30f, labelSpacing = 30f, charSize = 1.2f  }, // LOD 0
        new GridParams { gridSpacing = 30f, labelSpacing = 30f, charSize = 1.1f  }, // LOD 1
        new GridParams { gridSpacing = 15f, labelSpacing = 15f, charSize = 0.9f  }, // LOD 2
        new GridParams { gridSpacing = 15f, labelSpacing = 15f, charSize = 0.8f  }, // LOD 3
        new GridParams { gridSpacing = 10f, labelSpacing = 10f, charSize = 0.65f }, // LOD 4
        new GridParams { gridSpacing = 10f, labelSpacing = 10f, charSize = 0.55f }, // LOD 5
        new GridParams { gridSpacing = 5f,  labelSpacing = 10f, charSize = 0.45f }, // LOD 6
        new GridParams { gridSpacing = 5f,  labelSpacing = 5f,  charSize = 0.4f  }, // LOD 7
        new GridParams { gridSpacing = 2f,  labelSpacing = 5f,  charSize = 0.3f  }, // LOD 8
        new GridParams { gridSpacing = 2f,  labelSpacing = 2f,  charSize = 0.25f }, // LOD 9
        new GridParams { gridSpacing = 1f,  labelSpacing = 2f,  charSize = 0.2f  }, // LOD 10
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

        // --- Latitude lines ---
        for (float lat = -90f + gp.gridSpacing; lat < 90f; lat += gp.gridSpacing)
        {
            bool isEquator = Mathf.Abs(lat) < 0.01f;
            Color col = isEquator ? equatorColor : latitudeColor;
            float w = isEquator ? lineWidth * 2.5f : lineWidth;
            CreateLatitudeLine(lat, lineR, col, w);
        }

        // --- Longitude lines ---
        for (float lon = -180f; lon < 180f; lon += gp.gridSpacing)
        {
            bool isPrime = Mathf.Abs(lon) < 0.01f;
            Color col = isPrime ? primeMeridianColor : longitudeColor;
            float w = isPrime ? lineWidth * 2.5f : lineWidth;
            CreateLongitudeLine(lon, lineR, col, w);
        }

        // --- Labels at every label intersection ---
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

    void UpdateVisuals(Camera cam)
    {
        if (_gridParent == null || cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 sphereCenter = transform.position;
        Vector3 camDir = (camPos - sphereCenter).normalized;
        float distToCenter = Vector3.Distance(camPos, sphereCenter);

        float referenceDist = _gridCreationDist > 0f ? _gridCreationDist : distToCenter;
        float lineScale = distToCenter / referenceDist;

        foreach (Transform child in _gridParent.transform)
        {
            if (child.name == "Labels") continue;
            LineRenderer lr = child.GetComponent<LineRenderer>();
            if (lr != null)
                lr.widthMultiplier = lineScale;
        }

        Transform labels = _gridParent.transform.Find("Labels");
        if (labels == null) return;

        Quaternion rot = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

        foreach (Transform child in labels)
        {
            Vector3 labelDir = (child.position - sphereCenter).normalized;
            bool visible = Vector3.Dot(labelDir, camDir) > 0.2f;
            child.gameObject.SetActive(visible);
            if (visible)
            {
                child.rotation = rot;
                float distToLabel = Vector3.Distance(camPos, child.position);
                float scale = distToLabel / referenceDist;
                child.localScale = Vector3.one * scale;
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

    void CreateLatitudeLine(float lat, float radius, Color color, float width)
    {
        LineRenderer lr = CreateLineRenderer($"Lat_{lat:F0}", color, width);
        lr.positionCount = pointsPerLine + 1;
        lr.loop = true;

        for (int i = 0; i <= pointsPerLine; i++)
        {
            float lon = -180f + 360f * i / pointsPerLine;
            lr.SetPosition(i, S2Geometry.LatLonToUnityPosition(lat, lon, radius));
        }
    }

    void CreateLongitudeLine(float lon, float radius, Color color, float width)
    {
        LineRenderer lr = CreateLineRenderer($"Lon_{lon:F0}", color, width);
        lr.positionCount = pointsPerLine + 1;
        lr.loop = false;

        for (int i = 0; i <= pointsPerLine; i++)
        {
            float lat = -90f + 180f * i / pointsPerLine;
            lr.SetPosition(i, S2Geometry.LatLonToUnityPosition(lat, lon, radius));
        }
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
