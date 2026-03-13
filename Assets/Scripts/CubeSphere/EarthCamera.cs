using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class EarthCamera : MonoBehaviour
{
    public static EarthCamera Instance { get; private set; }

    [Header("Orbit")]
    public Transform target;
    public float distance = 300f;
    public float rotationSpeed = 5f;
    public float zoomSpeed = 50f;
    public float minDistance = 101f;
    public float maxDistance = 300f;

    [Header("LOD")]
    [Range(0, 10)]
    public int currentLod;

    /// <summary>0→1 progress within the current LOD band (for smooth minor line fading).</summary>
    public float lodProgress { get; private set; }

    public event System.Action<int> OnLodChanged;

    private Quaternion _orbitRotation = Quaternion.Euler(15f, 160f, 0f);
    private Mouse _mouse;
    private int _previousLod = -1;
    private Camera _cam;

    void Awake()
    {
        Instance = this;
        _mouse = Mouse.current;
        _cam = GetComponent<Camera>();
    }

    void OnEnable()
    {
        Instance = this;
        ApplyOrbit();
    }

    void Start()
    {
        // Deferred to Start so CubeSphere.OnEnable has already set target.rotation
        InitializeFromGps();
        ApplyOrbit();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void InitializeFromGps()
    {
        var gps = FindAnyObjectByType<GpsMarker>();
        if (gps == null || target == null)
        {
            Debug.LogWarning($"[EarthCamera] GPS init skipped: gps={gps != null}, target={target != null}");
            return;
        }
        Vector3 gpsLocal = S2Geometry.LatLonToUnityPosition(gps.latitude, gps.longitude, 1f);
        Vector3 dir = (target.rotation * gpsLocal).normalized;
        float pitch = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
        _orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void ApplyOrbit()
    {
        Vector3 targetPos = target != null ? target.position : Vector3.zero;
        Vector3 offset = _orbitRotation * new Vector3(0, 0, -distance);
        transform.position = targetPos + offset;
        transform.LookAt(targetPos, _orbitRotation * Vector3.up);
    }

    void LateUpdate()
    {
        if (Application.isPlaying)
        {
            if (_mouse == null) { _mouse = Mouse.current; if (_mouse == null) return; }

            if (_mouse.leftButton.isPressed)
            {
                Vector2 delta = _mouse.delta.ReadValue();
                float ratio = distance / maxDistance;
                float distanceFactor = ratio * ratio * ratio;
                float speed = rotationSpeed * 0.1f * distanceFactor;

                // Rotate in screen space: drag direction always matches visual movement
                Quaternion yawDelta   = Quaternion.AngleAxis( delta.x * speed, transform.up);
                Quaternion pitchDelta = Quaternion.AngleAxis(-delta.y * speed, transform.right);
                _orbitRotation = pitchDelta * yawDelta * _orbitRotation;
            }

            float scroll = _mouse.scroll.ReadValue().y;
            distance -= scroll * zoomSpeed * 0.01f;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        ApplyOrbit();

        // Dynamic clip planes based on orbit distance (globe radius = 100)
        float surfaceDist = distance - 100f;
        _cam.nearClipPlane = Mathf.Max(0.1f, surfaceDist * 0.1f);
        _cam.farClipPlane  = distance + 150f;

        // FOV-aware effective distance: narrow FOV → larger effective distance → coarser LOD
        const float refHalfFov = 30f; // reference half-FOV in degrees (60° total)
        float effectiveDistance = distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                          / Mathf.Tan(refHalfFov * Mathf.Deg2Rad);

        float progress;
        int lod = ComputeLod(effectiveDistance, minDistance, maxDistance, out progress);
        lodProgress = progress;
        if (lod != currentLod)
        {
            currentLod = lod;
            Debug.Log($"[EarthCamera] LOD {currentLod} | dist={distance:F1} effDist={effectiveDistance:F1} fov={_cam.fieldOfView:F1} progress={progress:F2}");
            if (currentLod != _previousLod)
            {
                _previousLod = currentLod;
                OnLodChanged?.Invoke(currentLod);
            }
        }
    }

    /// <summary>
    /// Compute LOD [0,10] from distance using logarithmic mapping.
    /// LOD 10 = closest, LOD 0 = farthest.
    /// </summary>
    public static int ComputeLod(float distance, float minDist = 101f, float maxDist = 5000f)
    {
        float progress;
        return ComputeLod(distance, minDist, maxDist, out progress);
    }

    /// <summary>
    /// Compute LOD [0,10] with fractional progress within the current band.
    /// </summary>
    public static int ComputeLod(float distance, float minDist, float maxDist, out float progress)
    {
        distance = Mathf.Clamp(distance, minDist, maxDist);
        float t = Mathf.InverseLerp(Mathf.Log(minDist), Mathf.Log(maxDist), Mathf.Log(distance));
        float continuous = (1f - t) * 10f;
        int lod = Mathf.Clamp(Mathf.RoundToInt(continuous), 0, 10);
        // progress = fractional position within the LOD band (0 = just entered, 1 = about to advance)
        progress = Mathf.Clamp01(continuous - (lod - 0.5f));
        return lod;
    }
}
