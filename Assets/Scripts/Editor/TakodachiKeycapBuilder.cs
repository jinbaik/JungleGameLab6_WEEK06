using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

using Game.Tournament;

public static class TakodachiKeycapBuilder
{
    private const string MESH_PATH = "Assets/Resource/Meshes/Takodachi/Keycap_1u_Takodachi.asset";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/Takodachi";
    private const string PART_PATH = "Assets/Resource/Tournament/Parts/keycap-takodachi.asset";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/Takodachi/Keycap_1u_Takodachi.prefab";

    /// <summary>기존 1u 키캡 규격으로 타코다치 메시, 재질과 부품 SO를 생성한다. 카탈로그 및 재사용 프리팹을 갱신하고 Unity 렌더링 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Takodachi Keycap")]
    public static void Build()
    {
        foreach (string directory in new[] { Path.GetDirectoryName(MESH_PATH), MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/takodachi-keycap" })
            Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        Material white = CreateMaterial("Takodachi_Body", new Color(0.235f, 0.185f, 0.355f));
        Material ink = CreateMaterial("Takodachi_Ink", new Color(0.012f, 0.016f, 0.024f));
        Material eyeLight = CreateMaterial("Takodachi_EyeLight", new Color(0.95f, 0.98f, 1f));
        Material orange = CreateMaterial("Takodachi_Orange", new Color(1f, 0.43f, 0.065f));
        orange.SetFloat("_Smoothness", 0.48f);
        Material[] materials = { white, ink, eyeLight, orange };
        Mesh mesh = CreateMesh();
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
        if (existing == null) AssetDatabase.CreateAsset(mesh, MESH_PATH);
        else
        {
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            existing.normals = mesh.normals;
            existing.tangents = mesh.tangents;
            existing.uv = mesh.uv;
            existing.subMeshCount = mesh.subMeshCount;
            for (int index = 0; index < mesh.subMeshCount; index++) existing.SetTriangles(mesh.GetTriangles(index), index);
            existing.bounds = mesh.bounds;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            mesh = existing;
        }
        KeycapDefinition part = AssetDatabase.LoadAssetAtPath<KeycapDefinition>(PART_PATH);
        if (part == null)
        {
            part = ScriptableObject.CreateInstance<KeycapDefinition>();
            AssetDatabase.CreateAsset(part, PART_PATH);
        }
        SerializedObject data = new SerializedObject(part);
        data.FindProperty("_id").stringValue = "keycap-takodachi";
        data.FindProperty("_displayName").stringValue = "타코다치 키캡";
        data.FindProperty("_price").intValue = 60;
        data.FindProperty("_quantity").intValue = 3;
        data.FindProperty("_material").objectReferenceValue = white;
        data.FindProperty("_materialId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(white)) + ":2100000";
        data.FindProperty("_shellMesh").objectReferenceValue = mesh;
        SetReferences(data.FindProperty("_modelMaterials"), materials);
        data.FindProperty("_oneUnitOnly").boolValue = true;
        if (data.FindProperty("_ability").objectReferenceValue == null)
            data.FindProperty("_ability").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KeycapAbilityDefinition>("Assets/Resource/Tournament/Abilities/keycap-takodachi-ability.asset");
        data.ApplyModifiedPropertiesWithoutUndo();
        RegisterPart(part, materials);
        GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keycaps/Keycap_1u.prefab"));
        try
        {
            cap.name = "Keycap_1u_Takodachi";
            MeshRenderer shell = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            new KeycapModelAppearance(shell).Apply(part);
            PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_PATH);
        }
        finally { UnityEngine.Object.DestroyImmediate(cap); }
        AssetDatabase.SaveAssets();
        ExportPreview(mesh, materials);
        Debug.Log($"TAKODACHI_CREATED mesh={MESH_PATH} submeshes={mesh.subMeshCount} vertices={mesh.vertexCount} bounds={mesh.bounds} part={PART_PATH}");
    }

    /// <summary>name과 color로 URP 재질을 생성하거나 갱신한다. 기존 에셋 GUID를 유지하며 해당 재질을 반환한다.</summary>
    private static Material CreateMaterial(string name, Color color)
    {
        string path = MATERIAL_ROOT + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.name = name;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.24f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>property 배열에 values의 Unity 오브젝트를 순서대로 저장한다. 메시 슬롯과 재질 참조의 순서를 유지한다.</summary>
    private static void SetReferences(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++) property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }

    /// <summary>property 배열에 value가 없으면 추가한다. 기존 카탈로그 항목 및 순서는 유지한다.</summary>
    private static void AppendReference(SerializedProperty property, UnityEngine.Object value)
    {
        for (int index = 0; index < property.arraySize; index++)
            if (property.GetArrayElementAtIndex(index).objectReferenceValue == value) return;
        int last = property.arraySize++;
        property.GetArrayElementAtIndex(last).objectReferenceValue = value;
    }

    /// <summary>part와 materials를 기존 부품 및 재질 카탈로그에 등록한다. 고유 재질 ID와 모델 복원용 SO 연결을 함께 저장한다.</summary>
    private static void RegisterPart(KeycapDefinition part, Material[] materials)
    {
        SerializedObject parts = new SerializedObject(AssetDatabase.LoadAssetAtPath<KeyboardPartCatalog>("Assets/Resource/Tournament/KeyboardPartCatalog.asset"));
        AppendReference(parts.FindProperty("_parts"), part);
        parts.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject catalog = new SerializedObject(AssetDatabase.LoadAssetAtPath<KeyboardMaterialCatalog>("Assets/Resource/Tournament/KeyboardMaterialCatalog.asset"));
        SerializedProperty registered = catalog.FindProperty("_materials");
        SerializedProperty ids = catalog.FindProperty("_ids");
        foreach (Material material in materials)
        {
            int before = registered.arraySize;
            AppendReference(registered, material);
            if (registered.arraySize == before) continue;
            ids.arraySize = registered.arraySize;
            ids.GetArrayElementAtIndex(before).stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)) + ":2100000";
        }
        AppendReference(catalog.FindProperty("_keycapDefinitions"), part);
        catalog.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>둥근 보라색 몸통과 짧은 돌출부, 물결 촉수, 입체 얼굴 및 주황 고리를 결합한다. 네 재질 슬롯을 갖는 1u 타코다치 메시를 반환한다.</summary>
    private static Mesh CreateMesh()
    {
        TakodachiMesh builder = new TakodachiMesh();
        builder.Body();
        foreach (float sign in new[] { -1f, 1f })
        {
            builder.Ellipsoid(new Vector3(sign * 0.330f, 0.520f, 0f), new Vector3(0.107f, 0.066f, 0.073f), 0, sign * 28f);
            float eyeX = sign * 0.13f;
            builder.Stroke(new[] { FacePoint(eyeX - 0.046f, 0.430f + sign * 0.008f), FacePoint(eyeX, 0.430f), FacePoint(eyeX + 0.046f, 0.430f - sign * 0.008f) }, 0.0135f, 1);
            for (int light = 0; light < 2; light++)
            {
                Vector3 point = FacePoint(eyeX - 0.013f + light * 0.024f, 0.432f);
                point.z -= 0.013f;
                builder.Ellipsoid(point, new Vector3(0.0055f, 0.0055f, 0.0045f), 2);
            }
            for (int cheek = 0; cheek < 3; cheek++)
            {
                float x = sign * (0.15f + cheek * 0.022f);
                float y = 0.365f - cheek * 0.002f;
                builder.Stroke(new[] { FacePoint(x - sign * 0.0015f, y + 0.008f), FacePoint(x + sign * 0.0015f, y - 0.005f) }, 0.0038f, 3);
            }
        }
        Vector3[] mouth = new Vector3[17];
        for (int point = 0; point < mouth.Length; point++)
        {
            float x = -0.07f + point * 0.14f / (mouth.Length - 1);
            mouth[point] = FacePoint(x, 0.355f + 0.008f * Mathf.Cos(x / 0.07f * Mathf.PI * 2f));
        }
        builder.Stroke(mouth, 0.0065f, 1);
        builder.Ellipsoid(new Vector3(0f, 0.637f, 0.012f), new Vector3(0.105f, 0.025f, 0.105f), 0);
        builder.Torus(new Vector3(0f, 0.690f, 0.012f), 0.135f, 0.025f, 3);
        return builder.ToMesh();
    }

    /// <summary>height에 대응하는 둥근 몸통 반경을 반환한다. 통통한 하부에서 머리 꼭대기까지 이어지는 곡면을 정의한다.</summary>
    private static float BodyRadius(float height)
    {
        float progress = Mathf.Clamp01(height / 0.65f);
        return 0.35f * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(progress, 2.6f)), 0.45f);
    }

    /// <summary>x와 y를 몸통 앞면의 곡면 좌표로 변환하여 반환한다. 얼굴을 위쪽에 배치하여 내려다보는 카메라에서도 눈과 입이 보이도록 한다.</summary>
    private static Vector3 FacePoint(float x, float y)
    {
        float radius = BodyRadius(y);
        float z = -Mathf.Sqrt(Mathf.Max(0f, radius * radius - x * x));
        return new Vector3(x, y, z - 0.006f);
    }

    /// <summary>mesh와 materials로 실제 Unity 렌더링 미리보기를 저장한다. 장착 축척으로 인접 일반 키와 비교한 이미지도 생성한다.</summary>
    private static void ExportPreview(Mesh mesh, Material[] materials)
    {
        RenderPreview(mesh, materials, false, "output/takodachi-keycap/takodachi-front-unity.png", true);
        RenderPreview(mesh, materials, false, "output/takodachi-keycap/takodachi-game-unity.png");
        RenderPreview(mesh, materials, true, "output/takodachi-keycap/takodachi-neighbors-unity.png");
    }

    /// <summary>mesh와 materials를 미리보기 공간에 배치하여 path에 PNG로 저장한다. neighbors이면 1u 간격의 일반 키를 함께 표시하고 임시 자원을 해제한다.</summary>
    private static void RenderPreview(Mesh mesh, Material[] materials, bool neighbors, string path, bool frontal = false)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject group = new GameObject("TakodachiPreview");
        try
        {
            for (int z = neighbors ? -1 : 0; z <= (neighbors ? 1 : 0); z++)
                for (int x = neighbors ? -1 : 0; x <= (neighbors ? 1 : 0); x++)
                {
                    GameObject key = new GameObject("Keycap", typeof(MeshFilter), typeof(MeshRenderer));
                    key.transform.SetParent(group.transform, false);
                    key.transform.localPosition = new Vector3(x, 0f, z);
                    bool cat = x == 0 && z == 0;
                    key.GetComponent<MeshFilter>().sharedMesh = cat ? mesh : AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resource/Meshes/Keycap_1u.asset");
                    key.GetComponent<MeshRenderer>().sharedMaterials = cat ? materials : new[] { AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Porcelain_PBT.mat") };
                }
            preview.AddSingleGO(group);
            preview.camera.orthographic = true;
            preview.camera.orthographicSize = neighbors ? 2.2f : 0.58f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 20f;
            preview.camera.transform.position = frontal ? new Vector3(0f, 0.66f, -3f) : new Vector3(0.6f, 3.2f, -2.2f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.35f, 0f));
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.075f, 0.095f, 0.135f);
            preview.lights[0].intensity = 1.3f;
            preview.lights[0].transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            preview.lights[1].intensity = 0.8f;
            preview.lights[1].transform.rotation = Quaternion.Euler(35f, 145f, 0f);
            preview.ambientColor = new Color(0.42f, 0.45f, 0.52f);
            preview.BeginStaticPreview(new Rect(0f, 0f, 1000f, 1000f));
            preview.Render(true);
            Texture2D image = preview.EndStaticPreview();
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally { preview.Cleanup(); }
    }

    private sealed class TakodachiMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int>[] _triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

        /// <summary>a, b, c를 material 서브메시에 추가한다. outward 기준으로 삼각형 방향을 맞추고 위치 기반 UV를 기록한다.</summary>
        private void Triangle(Vector3 a, Vector3 b, Vector3 c, int material, Vector3 outward)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.000000000000001f) return;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
            foreach (Vector3 point in new[] { a, b, c })
            {
                _triangles[material].Add(_vertices.Count);
                _vertices.Add(point);
                _uv.Add(new Vector2(point.x + 0.5f, point.z + 0.5f));
            }
        }

        /// <summary>둥근 몸통을 높이별 링으로 연결하여 생성한다. 바닥 가장자리의 여섯 물결을 짧은 촉수처럼 이어 붙이고 상하단을 닫는다.</summary>
        public void Body()
        {
            for (int ring = 0; ring < 32; ring++)
                for (int side = 0; side < 64; side++)
                {
                    Vector3 a = BodyPoint(ring, side);
                    Vector3 b = BodyPoint(ring, side + 1);
                    Vector3 c = BodyPoint(ring + 1, side + 1);
                    Vector3 d = BodyPoint(ring + 1, side);
                    Vector3 outward = (a + b + c + d) * 0.25f - new Vector3(0f, 0.25f, 0f);
                    Triangle(a, b, c, 0, outward);
                    Triangle(a, c, d, 0, outward);
                }
        }

        /// <summary>ring과 side의 위치에서 몸통의 곡면 좌표를 반환한다. 아래쪽 링에만 물결 변형을 적용하여 짧고 둥근 촉수 실루엣을 만든다.</summary>
        private Vector3 BodyPoint(int ring, int side)
        {
            float angle = side * Mathf.PI * 2f / 64f;
            float wave = Mathf.Cos(angle * 6f);
            if (ring == 0) return new Vector3(0f, 0.025f, 0f);
            float height = ring == 1 ? 0.005f + 0.015f * (1f - wave)
                : ring == 2 ? 0.017f + 0.018f * (1f - wave)
                : 0.075f + (ring - 3f) / 29f * 0.575f;
            float radius = ring == 1 ? 0.25f * (1f + 0.067f * wave)
                : ring == 2 ? 0.34f * (1f + 0.067f * wave)
                : BodyRadius(height) * (1f + 0.067f * wave * Mathf.Exp(-height * 18f));
            return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        }

        /// <summary>center와 radius, tube 크기로 실제 구멍이 뚫린 고리를 material 슬롯에 추가한다. 수평으로 놓인 주황 고리의 표면과 안쪽 곡면을 연결한다.</summary>
        public void Torus(Vector3 center, float radius, float tube, int material)
        {
            for (int segment = 0; segment < 64; segment++)
                for (int side = 0; side < 16; side++)
                {
                    Vector3 a = TorusPoint(center, radius, tube, segment, side);
                    Vector3 b = TorusPoint(center, radius, tube, segment + 1, side);
                    Vector3 c = TorusPoint(center, radius, tube, segment + 1, side + 1);
                    Vector3 d = TorusPoint(center, radius, tube, segment, side + 1);
                    float angle = (segment + 0.5f) * Mathf.PI * 2f / 64f;
                    Vector3 axis = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Vector3 outward = (a + b + c + d) * 0.25f - axis;
                    Triangle(a, b, c, material, outward);
                    Triangle(a, c, d, material, outward);
                }
        }

        /// <summary>center, radius와 tube 및 분할 인덱스로 고리의 표면 좌표를 반환한다. 타원체를 겹치지 않고 연속된 도넛 곡면을 계산한다.</summary>
        private Vector3 TorusPoint(Vector3 center, float radius, float tube, int segment, int side)
        {
            float a = segment * Mathf.PI * 2f / 64f;
            float b = side * Mathf.PI * 2f / 16f;
            return center + new Vector3(Mathf.Cos(a) * (radius + Mathf.Cos(b) * tube), Mathf.Sin(b) * tube, Mathf.Sin(a) * (radius + Mathf.Cos(b) * tube));
        }

        /// <summary>center와 radius 및 rotation으로 회전한 타원체를 material 서브메시에 추가한다. 머리 돌출부와 눈빛의 실제 입체 형상을 구성한다.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radius, int material, float rotation = 0f)
        {
            const int LATITUDES = 10;
            const int LONGITUDES = 20;
            for (int latitude = 0; latitude < LATITUDES; latitude++)
                for (int longitude = 0; longitude < LONGITUDES; longitude++)
                {
                    Vector3 a = SpherePoint(center, radius, latitude, longitude, LATITUDES, LONGITUDES);
                    Vector3 b = SpherePoint(center, radius, latitude, longitude + 1, LATITUDES, LONGITUDES);
                    Vector3 c = SpherePoint(center, radius, latitude + 1, longitude + 1, LATITUDES, LONGITUDES);
                    Vector3 d = SpherePoint(center, radius, latitude + 1, longitude, LATITUDES, LONGITUDES);
                    Quaternion orientation = Quaternion.Euler(0f, 0f, rotation);
                    a = center + orientation * (a - center);
                    b = center + orientation * (b - center);
                    c = center + orientation * (c - center);
                    d = center + orientation * (d - center);
                    Vector3 outward = (a + b + c + d) * 0.25f - center;
                    Triangle(a, b, c, material, outward);
                    Triangle(a, c, d, material, outward);
                }
        }

        /// <summary>center, radius와 위도 및 경도를 사용하여 타원체의 표면 좌표를 반환한다. latitudes와 longitudes는 각도 분할 수이다.</summary>
        private Vector3 SpherePoint(Vector3 center, Vector3 radius, int latitude, int longitude, int latitudes, int longitudes)
        {
            float phi = latitude * Mathf.PI / latitudes;
            float theta = longitude * Mathf.PI * 2f / longitudes;
            return center + Vector3.Scale(radius, new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
        }

        /// <summary>points를 radius 두께의 입체 선으로 연결하여 material 서브메시에 추가한다. 눈과 입, 주황색 볼 표시의 작은 윤곽을 형성한다.</summary>
        public void Stroke(Vector3[] points, float radius, int material)
        {
            foreach (Vector3 point in points) Ellipsoid(point, Vector3.one * radius, material);
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector3 a = points[index];
                Vector3 b = points[index + 1];
                Vector3 direction = (b - a).normalized;
                Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(direction, reference).normalized * radius;
                Vector3 up = Vector3.Cross(direction, right).normalized * radius;
                for (int side = 0; side < 8; side++)
                {
                    float theta = side * Mathf.PI * 0.25f;
                    float next = (side + 1) * Mathf.PI * 0.25f;
                    Vector3 p = right * Mathf.Cos(theta) + up * Mathf.Sin(theta);
                    Vector3 q = right * Mathf.Cos(next) + up * Mathf.Sin(next);
                    Triangle(a + p, b + p, b + q, material, p + q);
                    Triangle(a + p, b + q, a + q, material, p + q);
                }
            }
        }

        /// <summary>누적한 정점과 서브메시로 Unity 메시를 반환한다. 법선과 탄젠트, 경계를 계산하고 읽기 가능한 메시를 유지한다.</summary>
        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh { name = "Keycap_1u_Takodachi", indexFormat = IndexFormat.UInt32 };
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            Dictionary<(int, int, int, int), int> shared = new Dictionary<(int, int, int, int), int>();
            mesh.subMeshCount = _triangles.Length;
            List<int>[] indices = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            for (int material = 0; material < _triangles.Length; material++)
                foreach (int original in _triangles[material])
                {
                    Vector3 point = _vertices[original];
                    var key = (Mathf.RoundToInt(point.x * 1000000f), Mathf.RoundToInt(point.y * 1000000f), Mathf.RoundToInt(point.z * 1000000f), material);
                    if (!shared.TryGetValue(key, out int index))
                    {
                        index = vertices.Count;
                        shared.Add(key, index);
                        vertices.Add(point);
                        uv.Add(_uv[original]);
                    }
                    indices[material].Add(index);
                }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            for (int index = 0; index < indices.Length; index++) mesh.SetTriangles(indices[index], index);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
