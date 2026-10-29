using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

using Game.Tournament;

public static class RookKeycapBuilder
{
    private const string MESH_PATH = "Assets/Resource/Meshes/Rook/Keycap_1u_Rook.asset";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/Rook";
    private const string PART_PATH = "Assets/Resource/Tournament/Parts/keycap-rook.asset";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/Rook/Keycap_1u_Rook.prefab";

    /// <summary>기존 1u 키캡 규격으로 룩 메시, 재질과 부품 SO를 생성한다. 카탈로그 및 재사용 프리팹을 갱신하고 Unity 렌더링 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Rook Keycap")]
    public static void Build()
    {
        foreach (string directory in new[] { Path.GetDirectoryName(MESH_PATH), MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/rook-keycap" })
            Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        Material material = CreateMaterial("Rook_GlossBlack", new Color(0.028f, 0.032f, 0.040f));
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
        data.FindProperty("_id").stringValue = "keycap-rook";
        data.FindProperty("_displayName").stringValue = "룩 키캡";
        data.FindProperty("_price").intValue = 60;
        data.FindProperty("_quantity").intValue = 3;
        data.FindProperty("_material").objectReferenceValue = material;
        data.FindProperty("_materialId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)) + ":2100000";
        data.FindProperty("_shellMesh").objectReferenceValue = mesh;
        SetReferences(data.FindProperty("_modelMaterials"), materials);
        data.FindProperty("_oneUnitOnly").boolValue = true;
        if (data.FindProperty("_ability").objectReferenceValue == null)
            data.FindProperty("_ability").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KeycapAbilityDefinition>("Assets/Resource/Tournament/Abilities/keycap-rook-ability.asset");
        data.ApplyModifiedPropertiesWithoutUndo();
        RegisterPart(part, materials);
        GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keycaps/Keycap_1u.prefab"));
        try
        {
            cap.name = "Keycap_1u_Rook";
            MeshRenderer shell = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            new KeycapModelAppearance(shell).Apply(part);
            PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_PATH);
        }
        finally { UnityEngine.Object.DestroyImmediate(cap); }
        AssetDatabase.SaveAssets();
        ExportPreview(mesh, materials);
        Debug.Log($"ROOK_CREATED mesh={MESH_PATH} submeshes={mesh.subMeshCount} vertices={mesh.vertexCount} bounds={mesh.bounds} part={PART_PATH}");
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

    /// <summary>층진 원형 받침과 잘록한 몸통, 안쪽이 파인 머리 및 여섯 성벽을 결합한다. 유광 검은색 재질 한 개를 사용하는 1u 룩 메시를 반환한다.</summary>
    private static Mesh CreateMesh()
    {
        RookMesh builder = new RookMesh();
        builder.Body();
        builder.Battlements();
        return builder.ToMesh();
    }

    /// <summary>mesh와 materials로 실제 Unity 렌더링 미리보기를 저장한다. 장착 축척으로 인접 일반 키와 비교한 이미지도 생성한다.</summary>
    private static void ExportPreview(Mesh mesh, Material[] materials)
    {
        RenderPreview(mesh, materials, false, "output/rook-keycap/rook-front-unity.png", true);
        RenderPreview(mesh, materials, false, "output/rook-keycap/rook-game-unity.png");
        RenderPreview(mesh, materials, true, "output/rook-keycap/rook-neighbors-unity.png");
    }

    /// <summary>mesh와 materials를 미리보기 공간에 배치하여 path에 PNG로 저장한다. neighbors이면 1u 간격의 일반 키를 함께 표시하고 임시 자원을 해제한다.</summary>
    private static void RenderPreview(Mesh mesh, Material[] materials, bool neighbors, string path, bool frontal = false)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject group = new GameObject("RookPreview");
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
            preview.camera.orthographicSize = neighbors ? 2.2f : 0.60f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 20f;
            preview.camera.transform.position = frontal ? new Vector3(0f, 0.74f, -3f) : new Vector3(0.6f, 3.2f, -2.2f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.40f, 0f));
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

    private sealed class RookMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private readonly Dictionary<(int, int, int, int), int> _indices = new Dictionary<(int, int, int, int), int>();
        private readonly Vector2[] _profile =
        {
            new Vector2(0f, 0f), new Vector2(0.34f, 0f),
            new Vector2(0.382f, 0.006f), new Vector2(0.410f, 0.020f),
            new Vector2(0.422f, 0.037f), new Vector2(0.422f, 0.052f),
            new Vector2(0.415f, 0.065f), new Vector2(0.387f, 0.074f),
            new Vector2(0.382f, 0.080f), new Vector2(0.405f, 0.088f),
            new Vector2(0.409f, 0.105f), new Vector2(0.402f, 0.120f),
            new Vector2(0.379f, 0.134f), new Vector2(0.345f, 0.154f),
            new Vector2(0.321f, 0.174f), new Vector2(0.300f, 0.188f),
            new Vector2(0.283f, 0.195f), new Vector2(0.285f, 0.204f),
            new Vector2(0.285f, 0.216f), new Vector2(0.272f, 0.225f),
            new Vector2(0.248f, 0.235f), new Vector2(0.224f, 0.255f),
            new Vector2(0.205f, 0.285f), new Vector2(0.185f, 0.325f),
            new Vector2(0.169f, 0.370f), new Vector2(0.155f, 0.420f),
            new Vector2(0.146f, 0.475f), new Vector2(0.143f, 0.530f),
            new Vector2(0.146f, 0.565f), new Vector2(0.158f, 0.590f),
            new Vector2(0.190f, 0.607f), new Vector2(0.226f, 0.620f),
            new Vector2(0.254f, 0.635f), new Vector2(0.274f, 0.653f),
            new Vector2(0.282f, 0.674f), new Vector2(0.282f, 0.695f),
            new Vector2(0.274f, 0.710f), new Vector2(0.257f, 0.719f),
            new Vector2(0.183f, 0.719f), new Vector2(0.173f, 0.710f),
            new Vector2(0.173f, 0.651f), new Vector2(0.156f, 0.638f),
            new Vector2(0f, 0.638f)
        };

        /// <summary>정의된 반경과 높이의 단면을 회전하여 몸체를 추가한다. 층진 받침과 곡선 몸통, 오목한 중앙을 닫힌 곡면으로 구성한다.</summary>
        public void Body()
        {
            for (int ring = 0; ring < _profile.Length - 1; ring++)
                for (int segment = 0; segment < 96; segment++)
                {
                    float a = segment * Mathf.PI * 2f / 96f;
                    float b = (segment + 1) * Mathf.PI * 2f / 96f;
                    SweepQuad(_profile[ring], _profile[ring + 1], a, b);
                }
        }

        /// <summary>머리의 원형 테두리에 여섯 성벽을 추가한다. 간격을 둔 사각 홈과 안쪽 벽, 모서리를 다듬은 윗면을 실제 입체 형상으로 구성한다.</summary>
        public void Battlements()
        {
            Vector2[] section =
            {
                new Vector2(0.179f, 0.707f), new Vector2(0.274f, 0.707f),
                new Vector2(0.282f, 0.715f), new Vector2(0.282f, 0.810f),
                new Vector2(0.274f, 0.821f), new Vector2(0.181f, 0.821f),
                new Vector2(0.173f, 0.813f), new Vector2(0.173f, 0.715f)
            };
            for (int tower = 0; tower < 6; tower++)
            {
                float center = (tower + 0.5f) * Mathf.PI / 3f;
                float start = center - Mathf.PI / 3f * 0.335f;
                float end = center + Mathf.PI / 3f * 0.335f;
                for (int segment = 0; segment < 16; segment++)
                {
                    float a = Mathf.Lerp(start, end, segment / 16f);
                    float b = Mathf.Lerp(start, end, (segment + 1) / 16f);
                    for (int edge = 0; edge < section.Length; edge++)
                        SweepQuad(section[edge], section[(edge + 1) % section.Length], a, b);
                }
                Cap(section, start, true, tower * 2 + 1);
                Cap(section, end, false, tower * 2 + 2);
            }
        }

        /// <summary>from과 to 단면 좌표를 a, b 각도로 연결한다. 단면 진행 방향으로 외측 법선을 계산하여 오목한 중앙의 안쪽 벽도 올바르게 표시한다.</summary>
        private void SweepQuad(Vector2 from, Vector2 to, float a, float b)
        {
            float middle = (a + b) * 0.5f;
            Vector2 delta = to - from;
            Vector3 normal = new Vector3(Mathf.Cos(middle) * delta.y, -delta.x, Mathf.Sin(middle) * delta.y);
            Vector3 p = Point(from, a);
            Vector3 q = Point(from, b);
            Vector3 r = Point(to, b);
            Vector3 s = Point(to, a);
            Triangle(p, q, r, normal);
            Triangle(p, r, s, normal);
        }

        /// <summary>section의 angle 방향 끝단을 닫는다. start와 group으로 좌우 방향 및 독립 법선 그룹을 지정하여 홈의 직각 윤곽을 유지한다.</summary>
        private void Cap(Vector2[] section, float angle, bool start, int group)
        {
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in section) center += point / section.Length;
            Vector3 normal = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (start ? -1f : 1f);
            for (int edge = 0; edge < section.Length; edge++)
                Triangle(Point(center, angle), Point(section[edge], angle), Point(section[(edge + 1) % section.Length], angle), normal, group);
        }

        /// <summary>profile의 반경과 높이를 angle 방향의 3차원 좌표로 변환하여 반환한다. 회전축의 위치는 기존 키캡 피벗을 유지한다.</summary>
        private Vector3 Point(Vector2 profile, float angle)
        {
            return new Vector3(Mathf.Cos(angle) * profile.x, profile.y, Mathf.Sin(angle) * profile.x);
        }

        /// <summary>a, b, c 좌표를 normal 방향의 삼각형으로 추가한다. group이 같은 정점은 공유하여 곡면을 매끄럽게 연결하고 홈 끝단은 분리한다.</summary>
        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, int group = 0)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 0.000000000000001f) return;
            if (Vector3.Dot(cross, normal) < 0f) (b, c) = (c, b);
            _triangles.Add(Vertex(a, group));
            _triangles.Add(Vertex(b, group));
            _triangles.Add(Vertex(c, group));
        }

        /// <summary>point와 group을 사용하여 정점을 등록하고 인덱스를 반환한다. 위치 기반 UV와 법선 공유 범위를 함께 저장한다.</summary>
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

        /// <summary>누적한 정점과 삼각형으로 단일 재질의 룩 메시를 반환한다. 법선과 탄젠트, 경계를 계산하고 사진 투영용 읽기 가능한 데이터를 유지한다.</summary>
        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh { name = "Keycap_1u_Rook", indexFormat = IndexFormat.UInt32 };
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
