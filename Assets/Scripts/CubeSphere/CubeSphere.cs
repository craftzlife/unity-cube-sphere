using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CubeSphere : MonoBehaviour
{
    public static Quaternion EarthRotation { get; private set; } = Quaternion.identity;

    [Header("Sphere Settings")]
    public float radius = 100f;
    [Range(4, 128)]
    public int faceResolution = 64;

    private GameObject[] _faceObjects = new GameObject[6];
    private MeshFilter[] _faceMeshFilters = new MeshFilter[6];
    private MeshRenderer[] _faceRenderers = new MeshRenderer[6];

    private float _lastRadius;
    private int _lastResolution;

    private static readonly string[] FaceNames = {
        "Face0_PosX", "Face1_PosY", "Face2_PosZ_North",
        "Face3_NegX", "Face4_NegY", "Face5_NegZ_South"
    };

    void OnEnable()
    {
        // DontSave objects vanish on domain reload, so always regenerate if missing
        if (_faceObjects[0] == null)
            GenerateCubeSphere();

        _lastRadius = radius;
        _lastResolution = faceResolution;

        var utc = System.DateTime.UtcNow;

        // Axial tilt (23.44°) with seasonal axis
        float phi = 2f * Mathf.PI / 365f * (utc.DayOfYear - 81);
        float alpha = Mathf.PI * 0.5f - phi;
        Vector3 tiltAxis = new Vector3(Mathf.Sin(alpha), 0f, -Mathf.Cos(alpha));
        Quaternion tilt = Quaternion.AngleAxis(23.44f, tiltAxis);

        // Time-of-day rotation: Earth rotates 15°/hour, lon 0° faces the sun at UTC noon
        float utcHours = utc.Hour + utc.Minute / 60f + utc.Second / 3600f;
        float hourAngle = 15f * (utcHours - 12f);
        Quaternion timeRot = Quaternion.AngleAxis(-hourAngle, Vector3.up);

        EarthRotation = tilt * timeRot;
        transform.rotation = EarthRotation;
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        if (Mathf.Abs(radius - _lastRadius) > 0.01f || faceResolution != _lastResolution)
        {
            _lastRadius = radius;
            _lastResolution = faceResolution;
#if UNITY_EDITOR
            EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                    GenerateCubeSphere();
            };
#endif
        }
    }

    public void GenerateCubeSphere()
    {
        // Destroy tracked faces
        for (int i = 0; i < 6; i++)
        {
            if (_faceObjects[i] != null)
                DestroyImmediate(_faceObjects[i]);
            _faceObjects[i] = null;
        }

        // Destroy ALL orphaned face children (duplicates from previous sessions)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            string childName = transform.GetChild(i).name;
            for (int f = 0; f < 6; f++)
            {
                if (childName == FaceNames[f])
                {
                    DestroyImmediate(transform.GetChild(i).gameObject);
                    break;
                }
            }
        }

        for (int face = 0; face < 6; face++)
            CreateFace(face);
    }

    void CreateFace(int face)
    {
        GameObject faceObj = new GameObject(FaceNames[face]);
        faceObj.hideFlags = HideFlags.DontSave;
        faceObj.transform.SetParent(transform, false);

        MeshFilter mf = faceObj.AddComponent<MeshFilter>();
        MeshRenderer mr = faceObj.AddComponent<MeshRenderer>();

        mf.mesh = GenerateFaceMesh(face);
        mr.material = CreateDefaultMaterial();

        _faceObjects[face] = faceObj;
        _faceMeshFilters[face] = mf;
        _faceRenderers[face] = mr;
    }

    Mesh GenerateFaceMesh(int face)
    {
        int res = faceResolution;
        int vertCount = (res + 1) * (res + 1);

        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[res * res * 6];

        for (int j = 0; j <= res; j++)
        {
            float t = (float)j / res;
            float v = S2Geometry.STToUV(t);

            for (int i = 0; i <= res; i++)
            {
                float s = (float)i / res;
                float u = S2Geometry.STToUV(s);

                Vector3 cubePoint = S2Geometry.FaceUVToXYZ(face, u, v);
                Vector3 sphereDir = cubePoint.normalized;

                int idx = j * (res + 1) + i;
                vertices[idx] = S2Geometry.GeoToUnity(sphereDir * radius);
                normals[idx] = S2Geometry.GeoToUnity(sphereDir);
                uvs[idx] = new Vector2(s, t);
            }
        }

        int tri = 0;
        for (int j = 0; j < res; j++)
        {
            for (int i = 0; i < res; i++)
            {
                int a = j * (res + 1) + i;
                int b = a + 1;
                int c = (j + 1) * (res + 1) + i;
                int d = c + 1;

                triangles[tri++] = a; triangles[tri++] = c; triangles[tri++] = b;
                triangles[tri++] = b; triangles[tri++] = c; triangles[tri++] = d;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"CubeSphereFace_{face}";
        if (vertCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = new Color(0.15f, 0.3f, 0.55f, 1f);
        return mat;
    }

    public void SetFaceElevationTexture(int face, Texture2D texture, float elevMin, float elevMax, Material elevationMat)
    {
        if (face < 0 || face >= 6 || _faceRenderers[face] == null) return;

        Material mat = new Material(elevationMat);
        mat.SetTexture("_MainTex", texture);
        mat.SetFloat("_ElevMin", elevMin);
        mat.SetFloat("_ElevMax", elevMax);
        mat.SetFloat("_ElevScale", 1f);
        _faceRenderers[face].material = mat;
    }

    public void SetFaceMaterial(int face, Material mat)
    {
        if (face < 0 || face >= 6 || _faceRenderers[face] == null) return;
        _faceRenderers[face].material = mat;
    }

    public MeshRenderer GetFaceRenderer(int face)
    {
        if (face < 0 || face >= 6) return null;
        return _faceRenderers[face];
    }

    public GameObject GetFaceObject(int face)
    {
        if (face < 0 || face >= 6) return null;
        return _faceObjects[face];
    }
}
