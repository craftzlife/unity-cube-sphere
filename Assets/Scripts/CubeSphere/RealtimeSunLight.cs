using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class RealtimeSunLight : MonoBehaviour
{
    [Header("Custom Time Override")]
    public bool useCustomTime;
    [Range(1, 12)] public int month = 6;
    [Range(1, 31)] public int day = 21;
    [Range(0, 23)] public int hour = 12;
    [Range(0, 59)] public int minute = 0;

    void Update()
    {
        System.DateTime utc;
        if (useCustomTime)
            utc = new System.DateTime(2026, month, day, hour, minute, 0, System.DateTimeKind.Utc);
        else
            utc = System.DateTime.UtcNow;

        int dayOfYear = utc.DayOfYear;
        float utcHours = utc.Hour + utc.Minute / 60f + utc.Second / 3600f;

        // Solar declination (degrees)
        float declination = 23.44f * Mathf.Sin(2f * Mathf.PI / 365f * (dayOfYear - 81));

        // Subsolar longitude: sun is over lon=0 at UTC noon
        float subsolarLon = -15f * (utcHours - 12f);

        // Sun direction in Unity world space
        Vector3 sunDir = S2Geometry.LatLonToUnityPosition(declination, subsolarLon, 1f).normalized;

        // Light shines from the sun toward the origin
        transform.rotation = Quaternion.LookRotation(-sunDir);
    }
}
