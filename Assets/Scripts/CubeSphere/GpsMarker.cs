using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GpsMarker : MonoBehaviour
{
    [Header("GPS Location")]
    public float latitude = 10.8231f;   // Ho Chi Minh City default
    public float longitude = 106.6297f;
    public bool useDeviceGps = false;

    [Header("Marker Appearance")]
    public float markerSize = 4f;
    public Color markerColor = new Color(0.0f, 0.3f, 1f, 1f);
    public float pulseSpeed = 2f;
    public float pulseAmplitude = 0.3f;
    [Tooltip("Desired screen-space size in pixels")]
    public float screenPixelSize = 8f;

    private float _sphereRadius = 100f;
    private GameObject _marker;
    private GameObject _pulse;
    private Material _pulseMat;

    void OnEnable()
    {
        CubeSphere cs = GetComponentInParent<CubeSphere>();
        if (cs != null) _sphereRadius = cs.radius;
        if (_marker == null)
            CreateMarker();
    }

    void OnDisable()
    {
        if (_marker != null)
            DestroyImmediate(_marker);
        _marker = null;
        _pulse = null;
        _pulseMat = null;
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled) return;
            if (_marker != null)
            {
                DestroyImmediate(_marker);
                _marker = null;
                _pulse = null;
                _pulseMat = null;
            }
            CreateMarker();
        };
#endif
    }

    void CleanupOrphanMarkers()
    {
        // Destroy any orphan GPS_Dot children left from previous domain reloads
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == "GPS_Dot")
                DestroyImmediate(child.gameObject);
        }
    }

    void CreateMarker()
    {
        CleanupOrphanMarkers();
        _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _marker.name = "GPS_Dot";
        _marker.hideFlags = HideFlags.DontSave;
        _marker.transform.SetParent(transform, false);
        _marker.transform.localScale = Vector3.one * markerSize;

        Renderer rend = _marker.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material mat = new Material(shader);
        mat.SetColor("_BaseColor", markerColor);
        mat.color = markerColor;
        rend.material = mat;

        Collider col = _marker.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        // Pulse ring
        _pulse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _pulse.name = "GPS_Pulse";
        _pulse.hideFlags = HideFlags.DontSave;
        _pulse.transform.SetParent(_marker.transform, false);
        _pulse.transform.localScale = Vector3.one * 1.5f;

        Renderer pr = _pulse.GetComponent<Renderer>();
        Shader pShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (pShader == null) pShader = Shader.Find("Unlit/Color");
        _pulseMat = new Material(pShader);
        Color pc = markerColor;
        pc.a = 0.4f;
        _pulseMat.SetColor("_BaseColor", pc);
        _pulseMat.color = pc;
        _pulseMat.SetFloat("_Surface", 1);
        _pulseMat.SetOverrideTag("RenderType", "Transparent");
        _pulseMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _pulseMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _pulseMat.SetInt("_ZWrite", 0);
        _pulseMat.renderQueue = 3000;
        pr.material = _pulseMat;

        Collider pc2 = _pulse.GetComponent<Collider>();
        if (pc2 != null) DestroyImmediate(pc2);

        UpdatePosition();

        if (Application.isPlaying && useDeviceGps)
            StartCoroutine(StartGps());
    }

    void Update()
    {
        if (_marker == null) return;

        // Constant screen-size: scale world size so dot stays ~screenPixelSize px
        Camera cam = Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            cam = Camera.current;
            if (cam == null)
                cam = UnityEditor.SceneView.lastActiveSceneView?.camera;
        }
#endif
        if (cam != null)
        {
            float dist = Vector3.Distance(cam.transform.position, _marker.transform.position);
            float frustumHeight = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float pixelRatio = screenPixelSize / Screen.height;
            _marker.transform.localScale = Vector3.one * frustumHeight * pixelRatio;
        }

        // Pulse animation (play mode only)
        if (Application.isPlaying && _pulse != null && _pulseMat != null)
        {
            float scale = 1.5f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmplitude;
            _pulse.transform.localScale = Vector3.one * scale;
            Color c = _pulseMat.color;
            c.a = 0.3f * (1f - Mathf.Abs(Mathf.Sin(Time.time * pulseSpeed)) * 0.5f);
            _pulseMat.color = c;
        }
    }

    public void UpdatePosition()
    {
        if (_marker == null) return;
        Vector3 pos = S2Geometry.LatLonToUnityPosition(latitude, longitude, _sphereRadius * 1.015f);
        _marker.transform.localPosition = pos;
        _marker.transform.up = pos.normalized;
    }

    public void SetLocation(float lat, float lon)
    {
        latitude = lat;
        longitude = lon;
        UpdatePosition();
    }

    IEnumerator StartGps()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (!Input.location.isEnabledByUser) yield break;
        Input.location.Start();

        int wait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && wait > 0)
        {
            yield return new WaitForSeconds(1);
            wait--;
        }

        if (Input.location.status == LocationServiceStatus.Running)
            InvokeRepeating(nameof(PollGps), 0f, 5f);
#else
        yield break;
#endif
    }

    void PollGps()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (Input.location.status == LocationServiceStatus.Running)
            SetLocation(Input.location.lastData.latitude, Input.location.lastData.longitude);
#endif
    }
}
