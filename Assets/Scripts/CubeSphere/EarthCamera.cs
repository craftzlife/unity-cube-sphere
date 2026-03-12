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
    public float maxDistance = 800f;

    [Header("LOD")]
    [Range(0, 10)]
    public int currentLod;

    public event System.Action<int> OnLodChanged;

    private float _yaw = 160f;
    private float _pitch = 15f;
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
        if (gps == null || target == null) return;
        Vector3 gpsLocal = S2Geometry.LatLonToUnityPosition(gps.latitude, gps.longitude, 1f);
        Vector3 dir = (target.rotation * gpsLocal).normalized;
        _pitch = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        _yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
    }

    void ApplyOrbit()
    {
        Vector3 targetPos = target != null ? target.position : Vector3.zero;
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -distance);
        transform.position = targetPos + offset;
        transform.LookAt(targetPos);
    }

    void LateUpdate()
    {
        if (Application.isPlaying)
        {
            if (_mouse == null) { _mouse = Mouse.current; if (_mouse == null) return; }

            if (_mouse.leftButton.isPressed)
            {
                Vector2 delta = _mouse.delta.ReadValue();
                _yaw += delta.x * rotationSpeed * 0.1f;
                _pitch -= delta.y * rotationSpeed * 0.1f;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
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

        int lod = ComputeLod(distance, minDistance, maxDistance);
        if (lod != currentLod)
        {
            currentLod = lod;
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
    public static int ComputeLod(float distance, float minDist = 101f, float maxDist = 800f)
    {
        distance = Mathf.Clamp(distance, minDist, maxDist);
        float t = Mathf.InverseLerp(Mathf.Log(minDist), Mathf.Log(maxDist), Mathf.Log(distance));
        return Mathf.Clamp(Mathf.RoundToInt((1f - t) * 10f), 0, 10);
    }
}
