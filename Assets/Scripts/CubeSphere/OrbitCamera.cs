using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 300f;
    public float rotationSpeed = 5f;
    public float zoomSpeed = 50f;
    public float minDistance = 110f;
    public float maxDistance = 800f;

    private float _yaw = 160f;
    private float _pitch = 15f;
    private Mouse _mouse;

    void Awake()
    {
        _mouse = Mouse.current;
    }

    void LateUpdate()
    {
        if (_mouse == null) { _mouse = Mouse.current; if (_mouse == null) return; }

        if (_mouse.rightButton.isPressed || _mouse.middleButton.isPressed)
        {
            Vector2 delta = _mouse.delta.ReadValue();
            _yaw += delta.x * rotationSpeed * 0.1f;
            _pitch -= delta.y * rotationSpeed * 0.1f;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
        }

        float scroll = _mouse.scroll.ReadValue().y;
        distance -= scroll * zoomSpeed * 0.01f;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Vector3 targetPos = target != null ? target.position : Vector3.zero;
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -distance);

        transform.position = targetPos + offset;
        transform.LookAt(targetPos);
    }
}
