using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

using Game.Tournament;

public static class BishopKeycapBuilder
{
    private const string MESH_PATH = "Assets/Resource/Meshes/Bishop/Keycap_1u_Bishop.asset";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/Bishop";
    private const string PART_PATH = "Assets/Resource/Tournament/Parts/keycap-Bishop.asset";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/Bishop/Keycap_1u_Bishop.prefab";

    /// <summary>기존 1u 키캡 규격으로 비숍 메시, 재질과 부품 SO를 생성한다. 카탈로그 및 재사용 프리팹을 갱신하고 Unity 렌더링 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Bishop Keycap")]
    public static void Build()
    {
        foreach (string directory in new[] { Path.GetDirectoryName(MESH_PATH), MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/bishop-keycap" })
            Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        Material material = CreateMaterial("Bishop_GlossBlack", new Color(0.028f, 0.032f, 0.040f));
        Material[] materials = { material };
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
        data.FindProperty("_id").stringValue = "keycap-Bishop";
        data.FindProperty("_displayName").stringValue = "비숍 키캡";
        data.FindProperty("_price").intValue = 60;
        data.FindProperty("_quantity").intValue = 3;
        data.FindProperty("_material").objectReferenceValue = material;
        data.FindProperty("_materialId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)) + ":2100000";
        data.FindProperty("_shellMesh").objectReferenceValue = mesh;
        SetReferences(data.FindProperty("_modelMaterials"), materials);
        data.FindProperty("_oneUnitOnly").boolValue = true;
        if (data.FindProperty("_ability").objectReferenceValue == null)
            data.FindProperty("_ability").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KeycapAbilityDefinition>("Assets/Resource/Tournament/Abilities/keycap-Bishop-ability.asset");
        data.ApplyModifiedPropertiesWithoutUndo();
        RegisterPart(part, materials);
        GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keycaps/Keycap_1u.prefab"));
        try
        {
            cap.name = "Keycap_1u_Bishop";
            MeshRenderer shell = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            new KeycapModelAppearance(shell).Apply(part);
            PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_PATH);
        }
        finally { UnityEngine.Object.DestroyImmediate(cap); }
        AssetDatabase.SaveAssets();
        ExportPreview(mesh, materials);
        Debug.Log($"BISHOP_CREATED mesh={MESH_PATH} submeshes={mesh.subMeshCount} vertices={mesh.vertexCount} bounds={mesh.bounds} part={PART_PATH}");
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
        material.SetFloat("_Smoothness", 0.86f);
        material.SetFloat("_Metallic", 0.12f);
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

    /// <summary>층진 받침과 목 장식, 물방울 머리와 실제 사선 절개를 결합한다. 유광 검은색 재질 한 개를 사용하는 1u 비숍 메시를 반환한다.</summary>
    private static Mesh CreateMesh()
    {
        BishopMesh builder = new BishopMesh();
        builder.Body();
        builder.Head();
        return builder.ToMesh();
    }

    /// <summary>mesh와 materials로 실제 Unity 렌더링 미리보기를 저장한다. 장착 축척으로 인접 일반 키와 비교한 이미지도 생성한다.</summary>
    private static void ExportPreview(Mesh mesh, Material[] materials)
    {
        RenderPreview(mesh, materials, false, "output/bishop-keycap/bishop-front-unity.png", true);
        RenderPreview(mesh, materials, false, "output/bishop-keycap/bishop-game-unity.png");
        RenderPreview(mesh, materials, true, "output/bishop-keycap/bishop-neighbors-unity.png");
    }

    /// <summary>mesh와 materials를 미리보기 공간에 배치하여 path에 PNG로 저장한다. neighbors이면 1u 간격의 일반 키를 함께 표시하고 임시 자원을 해제한다.</summary>
    private static void RenderPreview(Mesh mesh, Material[] materials, bool neighbors, string path, bool frontal = false)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject group = new GameObject("BishopPreview");
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
            preview.camera.orthographicSize = neighbors ? 2.2f : 0.64f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 20f;
            preview.camera.transform.position = frontal ? new Vector3(0f, 0.74f, -3f) : new Vector3(0.6f, 3.2f, -2.2f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.45f, 0f));
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

    private sealed class BishopMesh
    {
        private const float CUT_FLOOR = 0.665f;
        private const float CUT_HALF_WIDTH = 0.017f;
        private const float CUT_SLOPE = 0.65f;
        private const float CUT_CENTER_HEIGHT = 0.73f;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private readonly Dictionary<(int, int, int, int), int> _indices = new Dictionary<(int, int, int, int), int>();
        private readonly Vector2[] _bodyProfile =
        {
            new Vector2(0f, 0f), new Vector2(0.33f, 0f),
            new Vector2(0.37f, 0.005f), new Vector2(0.40f, 0.017f),
            new Vector2(0.411f, 0.030f), new Vector2(0.414f, 0.044f),
            new Vector2(0.408f, 0.058f), new Vector2(0.38f, 0.069f),
            new Vector2(0.377f, 0.076f), new Vector2(0.399f, 0.084f),
            new Vector2(0.401f, 0.100f), new Vector2(0.389f, 0.115f),
            new Vector2(0.36f, 0.130f), new Vector2(0.33f, 0.150f),
            new Vector2(0.294f, 0.169f), new Vector2(0.27f, 0.180f),
            new Vector2(0.27f, 0.193f), new Vector2(0.247f, 0.200f),
            new Vector2(0.218f, 0.220f), new Vector2(0.185f, 0.255f),
            new Vector2(0.155f, 0.300f), new Vector2(0.135f, 0.350f),
            new Vector2(0.122f, 0.400f), new Vector2(0.119f, 0.430f),
            new Vector2(0.150f, 0.442f), new Vector2(0.20f, 0.450f),
            new Vector2(0.255f, 0.460f), new Vector2(0.26f, 0.470f),
            new Vector2(0.25f, 0.482f), new Vector2(0.20f, 0.490f),
            new Vector2(0.16f, 0.495f), new Vector2(0.16f, 0.507f),
            new Vector2(0.181f, 0.516f), new Vector2(0.183f, 0.528f),
            new Vector2(0.17f, 0.540f), new Vector2(0.13f, 0.547f),
            new Vector2(0.116f, 0.555f), new Vector2(0.105f, 0.566f),
            new Vector2(0.13f, 0.575f), new Vector2(0.136f, 0.588f),
            new Vector2(0.12f, 0.600f), new Vector2(0f, 0.600f)
        };
        private readonly Vector2[] _headProfile =
        {
            new Vector2(0f, 0.585f), new Vector2(0.095f, 0.585f),
            new Vector2(0.120f, 0.597f), new Vector2(0.147f, 0.620f),
            new Vector2(0.168f, 0.645f), new Vector2(0.177f, 0.680f),
            new Vector2(0.166f, 0.710f), new Vector2(0.146f, 0.740f),
            new Vector2(0.120f, 0.770f), new Vector2(0.092f, 0.800f),
            new Vector2(0.066f, 0.830f), new Vector2(0.042f, 0.850f),
            new Vector2(0.025f, 0.865f), new Vector2(0.013f, 0.875f),
            new Vector2(0f, 0.880f)
        };

        /// <summary>정의된 반경과 높이의 단면을 회전하여 몸체를 추가한다. 둥근 받침과 잘록한 몸통, 층진 목 장식을 닫힌 곡면으로 구성한다.</summary>
        public void Body()
        {
            Lathe(_bodyProfile, false);
        }

        /// <summary>물방울 머리에 사선 절개와 내부 벽을 생성한다. 머리 위 작은 구형 장식까지 실제 메시로 연결한다.</summary>
        public void Head()
        {
            Lathe(_headProfile, true);
            CutWall(-CUT_HALF_WIDTH, true, 1);
            CutWall(CUT_HALF_WIDTH, false, 2);
            CutFloor();
            Vector2[] finial = new Vector2[17];
            for (int ring = 0; ring < finial.Length; ring++)
            {
                float angle = Mathf.PI * (1f - ring / 16f);
                finial[ring] = new Vector2(Mathf.Sin(angle) * 0.035f, 0.9f + Mathf.Cos(angle) * 0.025f);
            }
            Lathe(finial, false);
        }

        /// <summary>profile 단면을 회전하여 곡면 삼각형을 추가한다. cut이면 머리 사선 홈의 부피에 들어간 면을 잘라낸다.</summary>
        private void Lathe(Vector2[] profile, bool cut)
        {
            for (int ring = 0; ring < profile.Length - 1; ring++)
                for (int segment = 0; segment < 96; segment++)
                {
                    float a = segment * Mathf.PI * 2f / 96f;
                    float b = (segment + 1) * Mathf.PI * 2f / 96f;
                    Vector2 delta = profile[ring + 1] - profile[ring];
                    float middle = (a + b) * 0.5f;
                    Vector3 normal = new Vector3(Mathf.Cos(middle) * delta.y, -delta.x, Mathf.Sin(middle) * delta.y);
                    Vector3 p = Point(profile[ring], a);
                    Vector3 q = Point(profile[ring], b);
                    Vector3 r = Point(profile[ring + 1], b);
                    Vector3 t = Point(profile[ring + 1], a);
                    if (cut) { CutTriangle(p, q, r, normal); CutTriangle(p, r, t, normal); }
                    else { Triangle(p, q, r, normal); Triangle(p, r, t, normal); }
                }
        }

        /// <summary>a, b, c로 구성된 머리 면에서 사선 홈의 부피를 제거한다. normal로 원래 방향을 유지하며 홈 바깥쪽과 바닥 아래쪽 면만 저장한다.</summary>
        private void CutTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            List<Vector3> source = new List<Vector3> { a, b, c };
            Vector3 axis = new Vector3(1f, CUT_SLOPE, 0f);
            float lower = CUT_SLOPE * CUT_CENTER_HEIGHT - CUT_HALF_WIDTH;
            float upper = CUT_SLOPE * CUT_CENTER_HEIGHT + CUT_HALF_WIDTH;
            Polygon(Clip(source, -axis, -lower), normal);
            List<Vector3> remainder = Clip(source, axis, lower);
            Polygon(Clip(remainder, axis, upper), normal);
            Polygon(Clip(Clip(remainder, -axis, -upper), Vector3.down, -CUT_FLOOR), normal);
        }

        /// <summary>points를 axis와 distance가 정의하는 평면의 양수 영역으로 잘라 반환한다. 교차점은 선형 보간하여 실제 절개 경계를 만든다.</summary>
        private List<Vector3> Clip(List<Vector3> points, Vector3 axis, float distance)
        {
            List<Vector3> output = new List<Vector3>();
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % points.Count];
                float da = Vector3.Dot(axis, a) - distance;
                float db = Vector3.Dot(axis, b) - distance;
                bool insideA = da >= -0.0000001f;
                bool insideB = db >= -0.0000001f;
                if (insideA) output.Add(a);
                if (insideA != insideB) output.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
            }
            return output;
        }

        /// <summary>points의 다각형을 normal 방향의 삼각형으로 나누어 저장한다. 잘라낸 외피를 기존 머리 곡면에 연결한다.</summary>
        private void Polygon(List<Vector3> points, Vector3 normal)
        {
            for (int i = 1; i < points.Count - 1; i++) Triangle(points[0], points[i], points[i + 1], normal);
        }

        /// <summary>offset 위치의 사선 홈 내부 벽을 생성한다. left와 group으로 안쪽 방향 및 법선 분리를 지정하고 머리 외피와 만나는 끝까지 닫는다.</summary>
        private void CutWall(float offset, bool left, int group)
        {
            float low = CUT_FLOOR;
            float high = 0.88f;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float y = (low + high) * 0.5f;
                float x = offset - CUT_SLOPE * (y - CUT_CENTER_HEIGHT);
                if (Radius(y) >= Mathf.Abs(x)) low = y;
                else high = y;
            }
            float top = low;
            Vector3 normal = new Vector3(1f, CUT_SLOPE, 0f) * (left ? 1f : -1f);
            for (int step = 0; step < 96; step++)
            {
                float y0 = Mathf.Lerp(CUT_FLOOR, top, step / 96f);
                float y1 = Mathf.Lerp(CUT_FLOOR, top, (step + 1) / 96f);
                float x0 = offset - CUT_SLOPE * (y0 - CUT_CENTER_HEIGHT);
                float x1 = offset - CUT_SLOPE * (y1 - CUT_CENTER_HEIGHT);
                float z0 = Mathf.Sqrt(Mathf.Max(0f, Radius(y0) * Radius(y0) - x0 * x0));
                float z1 = Mathf.Sqrt(Mathf.Max(0f, Radius(y1) * Radius(y1) - x1 * x1));
                Triangle(new Vector3(x0, y0, -z0), new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), normal, group);
                Triangle(new Vector3(x0, y0, -z0), new Vector3(x1, y1, z1), new Vector3(x1, y1, -z1), normal, group);
            }
        }

        /// <summary>사선 절개의 아래쪽에 실제 바닥 면을 생성한다. 머리 반경과 홈 폭을 사용하여 홈 양쪽 내부 벽 사이를 연결한다.</summary>
        private void CutFloor()
        {
            float radius = Radius(CUT_FLOOR);
            for (int step = 0; step < 32; step++)
            {
                float a = Mathf.Lerp(-CUT_HALF_WIDTH, CUT_HALF_WIDTH, step / 32f) - CUT_SLOPE * (CUT_FLOOR - CUT_CENTER_HEIGHT);
                float b = Mathf.Lerp(-CUT_HALF_WIDTH, CUT_HALF_WIDTH, (step + 1) / 32f) - CUT_SLOPE * (CUT_FLOOR - CUT_CENTER_HEIGHT);
                float za = Mathf.Sqrt(radius * radius - a * a);
                float zb = Mathf.Sqrt(radius * radius - b * b);
                Triangle(new Vector3(a, CUT_FLOOR, -za), new Vector3(a, CUT_FLOOR, za), new Vector3(b, CUT_FLOOR, zb), Vector3.up, 3);
                Triangle(new Vector3(a, CUT_FLOOR, -za), new Vector3(b, CUT_FLOOR, zb), new Vector3(b, CUT_FLOOR, -zb), Vector3.up, 3);
            }
        }

        /// <summary>height의 머리 반경을 단면에서 보간하여 반환한다. 사선 홈의 내부 벽과 바닥을 머리 외곽에 맞추는 데 사용한다.</summary>
        private float Radius(float height)
        {
            for (int i = 1; i < _headProfile.Length; i++)
                if (height <= _headProfile[i].y && _headProfile[i].y > _headProfile[i - 1].y)
                    return Mathf.Lerp(_headProfile[i - 1].x, _headProfile[i].x, (height - _headProfile[i - 1].y) / (_headProfile[i].y - _headProfile[i - 1].y));
            return 0f;
        }

        /// <summary>profile의 반경과 높이를 angle 방향의 좌표로 변환하여 반환한다. 회전축은 기존 키캡 피벗과 일치시킨다.</summary>
        private Vector3 Point(Vector2 profile, float angle)
        {
            return new Vector3(Mathf.Cos(angle) * profile.x, profile.y, Mathf.Sin(angle) * profile.x);
        }

        /// <summary>a, b, c 좌표를 normal 방향의 삼각형으로 저장한다. group으로 절개 안쪽의 평면 법선을 외피 곡면과 분리한다.</summary>
        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, int group = 0)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 0.000000000000001f) return;
            if (Vector3.Dot(cross, normal) < 0f) (b, c) = (c, b);
            _triangles.Add(Vertex(a, group));
            _triangles.Add(Vertex(b, group));
            _triangles.Add(Vertex(c, group));
        }

        /// <summary>point와 group으로 정점을 등록하고 인덱스를 반환한다. 곡면 법선과 사진 투영용 UV를 함께 구성한다.</summary>
        private int Vertex(Vector3 point, int group)
        {
            var key = (Mathf.RoundToInt(point.x * 1000000f), Mathf.RoundToInt(point.y * 1000000f), Mathf.RoundToInt(point.z * 1000000f), group);
            if (_indices.TryGetValue(key, out int index)) return index;
            index = _vertices.Count;
            _indices.Add(key, index);
            _vertices.Add(point);
            _uv.Add(new Vector2(point.x + 0.5f, point.z + 0.5f));
            return index;
        }

        /// <summary>저장된 정점과 삼각형으로 유광 검정 재질용 비숍 메시를 반환한다. 법선과 탄젠트, 경계를 갱신하고 읽기 가능한 데이터를 유지한다.</summary>
        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh { name = "Keycap_1u_Bishop", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
