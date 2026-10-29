using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

using Game.Tournament;

public static class CheeseKeycapBuilder
{
    private const string MESH_PATH = "Assets/Resource/Meshes/Cheese/Keycap_1u_Cheese.asset";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/Cheese";
    private const string PART_PATH = "Assets/Resource/Tournament/Parts/keycap-cheese.asset";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/Cheese/Keycap_1u_Cheese.prefab";

    /// <summary>기존 1u 키캡 규격으로 치즈 메시, 재질과 부품 SO를 생성한다. 카탈로그 및 재사용 프리팹을 갱신하고 Unity 렌더링 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Cheese Keycap")]
    public static void Build()
    {
        foreach (string directory in new[] { Path.GetDirectoryName(MESH_PATH), MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/cheese-keycap" })
            Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        Material white = CreateMaterial("Cheese_Surface", new Color(1f, 0.72f, 0.065f));
        Material cavity = CreateMaterial("Cheese_Cavity", new Color(0.94f, 0.58f, 0.035f));
        Material[] materials = { white, cavity };
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
        data.FindProperty("_id").stringValue = "keycap-cheese";
        data.FindProperty("_displayName").stringValue = "치즈 키캡";
        data.FindProperty("_price").intValue = 60;
        data.FindProperty("_quantity").intValue = 3;
        data.FindProperty("_material").objectReferenceValue = white;
        data.FindProperty("_materialId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(white)) + ":2100000";
        data.FindProperty("_shellMesh").objectReferenceValue = mesh;
        SetReferences(data.FindProperty("_modelMaterials"), materials);
        data.FindProperty("_oneUnitOnly").boolValue = true;
        if (data.FindProperty("_ability").objectReferenceValue == null)
            data.FindProperty("_ability").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KeycapAbilityDefinition>("Assets/Resource/Tournament/Abilities/keycap-cheese-ability.asset");
        data.ApplyModifiedPropertiesWithoutUndo();
        RegisterPart(part, materials);
        GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keycaps/Keycap_1u.prefab"));
        try
        {
            cap.name = "Keycap_1u_Cheese";
            MeshRenderer shell = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            new KeycapModelAppearance(shell).Apply(part);
            PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_PATH);
        }
        finally { UnityEngine.Object.DestroyImmediate(cap); }
        AssetDatabase.SaveAssets();
        ExportPreview(mesh, materials);
        Debug.Log($"CHEESE_CREATED mesh={MESH_PATH} submeshes={mesh.subMeshCount} vertices={mesh.vertexCount} bounds={mesh.bounds} part={PART_PATH}");
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

    /// <summary>기울어진 윗면과 실제 함몰 구멍을 갖는 둥근 1u 치즈 메시를 반환한다. 밝은 표면과 어두운 구멍 내부를 두 서브메시에 나눈다.</summary>
    private static Mesh CreateMesh()
    {
        CheeseMesh builder = new CheeseMesh();
        for (int face = 0; face < 6; face++) builder.Surface(face);
        builder.CavityInteriors();
        return builder.ToMesh();
    }

    /// <summary>mesh와 materials로 실제 Unity 렌더링 미리보기를 저장한다. 장착 축척으로 인접 일반 키와 비교한 이미지도 생성한다.</summary>
    private static void ExportPreview(Mesh mesh, Material[] materials)
    {
        RenderPreview(mesh, materials, false, "output/cheese-keycap/cheese-unity.png");
        RenderPreview(mesh, materials, true, "output/cheese-keycap/cheese-neighbors-unity.png");
    }

    /// <summary>mesh와 materials를 미리보기 공간에 배치하여 path에 PNG로 저장한다. neighbors이면 1u 간격의 일반 키를 함께 표시하고 임시 자원을 해제한다.</summary>
    private static void RenderPreview(Mesh mesh, Material[] materials, bool neighbors, string path)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject group = new GameObject("CheesePreview");
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
            preview.camera.orthographicSize = neighbors ? 2.2f : 0.72f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 20f;
            preview.camera.transform.position = neighbors ? new Vector3(0.7f, 3.2f, -1.8f) : new Vector3(1.5f, 2.2f, -2.6f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.28f, 0f));
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

    private sealed class CheeseMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int>[] _triangles = { new List<int>(), new List<int>() };
        private readonly Dictionary<(int, int, int), int> _indices = new Dictionary<(int, int, int), int>();
        private readonly Vector4[] _topHoles =
        {
            new Vector4(-0.13f, 0.19f, 0.115f, 0.115f),
            new Vector4(0.15f, 0.005f, 0.095f, 0.095f),
            new Vector4(-0.065f, -0.23f, 0.070f, 0.065f),
            new Vector4(0.22f, 0.29f, 0.048f, 0.045f)
        };
        private readonly Vector4[][] _sideHoles =
        {
            new[] { new Vector4(0.18f, 0.32f, 0.12f, 0.085f), new Vector4(-0.19f, 0.14f, 0.072f, 0.065f) },
            new[] { new Vector4(0.17f, 0.28f, 0.10f, 0.080f), new Vector4(-0.20f, 0.15f, 0.063f, 0.060f) },
            new[] { new Vector4(-0.085f, 0.135f, 0.078f, 0.065f), new Vector4(0.14f, 0.20f, 0.041f, 0.038f) },
            new[] { new Vector4(-0.18f, 0.39f, 0.11f, 0.080f), new Vector4(0.17f, 0.20f, 0.085f, 0.065f) }
        };

        /// <summary>face에 해당하는 외피 격자를 생성한다. 둥근 모서리와 쐐기 높이, 구멍 깊이를 반영하여 정점과 재질별 삼각형을 누적한다.</summary>
        public void Surface(int face)
        {
            int columns = face == 5 ? 32 : 64;
            int rows = face == 0 ? 64 : face == 5 ? 32 : 40;
            int[,] grid = new int[rows + 1, columns + 1];
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    Vector3 point = SurfacePoint(face, x / (float)columns, y / (float)rows, out _);
                    grid[y, x] = AddVertex(point);
                }
            Vector3 normal = face == 0 ? Vector3.up : face == 1 ? Vector3.right : face == 2 ? Vector3.left : face == 3 ? Vector3.back : face == 4 ? Vector3.forward : Vector3.down;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int a = grid[y, x];
                    int b = grid[y, x + 1];
                    int c = grid[y + 1, x + 1];
                    int d = grid[y + 1, x];
                    AddTriangle(a, b, c, normal);
                    AddTriangle(a, c, d, normal);
                }
        }

        /// <summary>설정된 구멍 안쪽에 둥근 함몰 곡면을 추가한다. 실제 구멍 깊이를 따라 어두운 재질을 배치하여 원형 재질 경계를 부드럽게 만든다.</summary>
        public void CavityInteriors()
        {
            for (int face = 0; face < 5; face++)
                foreach (Vector4 hole in face == 0 ? _topHoles : _sideHoles[face - 1])
                {
                    int[,] grid = new int[9, 41];
                    for (int ring = 0; ring <= 8; ring++)
                        for (int segment = 0; segment <= 40; segment++)
                        {
                            float theta = segment * Mathf.PI * 2f / 40f;
                            float radius = hole.z * 0.72f * ring / 8f;
                            float a = hole.x + Mathf.Cos(theta) * radius;
                            float b = hole.y + Mathf.Sin(theta) * radius;
                            float dent = Bowl(a, b, face == 0 ? _topHoles : _sideHoles[face - 1]);
                            float back = (a + 0.43f) / 0.86f;
                            Vector3 point = face == 0 ? new Vector3(a, Mathf.Lerp(0.30f, 0.68f, (b + 0.43f) / 0.86f) - dent + 0.004f, b)
                                : face == 1 ? new Vector3(0.44f * Mathf.Lerp(0.66f, 1f, back) - dent + 0.004f, b, a)
                                : face == 2 ? new Vector3(-0.44f * Mathf.Lerp(0.66f, 1f, back) + dent - 0.004f, b, a)
                                : face == 3 ? new Vector3(a, b, -0.43f + dent - 0.004f)
                                : new Vector3(a, b, 0.43f - dent + 0.004f);
                            grid[ring, segment] = AddVertex(point);
                        }
                    Vector3 normal = face == 0 ? Vector3.up : face == 1 ? Vector3.right : face == 2 ? Vector3.left : face == 3 ? Vector3.back : Vector3.forward;
                    for (int ring = 0; ring < 8; ring++)
                        for (int segment = 0; segment < 40; segment++)
                        {
                            AddTriangle(grid[ring, segment], grid[ring, segment + 1], grid[ring + 1, segment + 1], normal, 1);
                            AddTriangle(grid[ring, segment], grid[ring + 1, segment + 1], grid[ring + 1, segment], normal, 1);
                        }
                }
        }

        /// <summary>face와 u, v 좌표를 치즈 표면 위치로 변환한다. 윗면 기울기와 앞쪽이 좁은 폭을 적용하고 dent에 실제 함몰 깊이를 반환한다.</summary>
        private Vector3 SurfacePoint(int face, float u, float v, out float dent)
        {
            float a = u * 2f - 1f;
            float b = v * 2f - 1f;
            Vector3 point = face == 0 ? new Vector3(a * 0.44f, 0.25f, b * 0.43f)
                : face == 1 ? new Vector3(0.44f, b * 0.25f, a * 0.43f)
                : face == 2 ? new Vector3(-0.44f, b * 0.25f, a * 0.43f)
                : face == 3 ? new Vector3(a * 0.44f, b * 0.25f, -0.43f)
                : face == 4 ? new Vector3(a * 0.44f, b * 0.25f, 0.43f)
                : new Vector3(a * 0.44f, -0.25f, b * 0.43f);
            Vector3 core = new Vector3(Mathf.Clamp(point.x, -0.405f, 0.405f), Mathf.Clamp(point.y, -0.215f, 0.215f), Mathf.Clamp(point.z, -0.395f, 0.395f));
            point = core + (point - core).normalized * 0.035f;
            float back = (point.z + 0.43f) / 0.86f;
            point.x *= Mathf.Lerp(0.66f, 1f, back);
            point.y = (point.y + 0.25f) / 0.5f * Mathf.Lerp(0.30f, 0.68f, back);
            dent = 0f;
            if (face == 0) { dent = Bowl(point.x, point.z, _topHoles); point.y -= dent; }
            else if (face == 1 || face == 2)
            {
                dent = Bowl(point.z, point.y, _sideHoles[face - 1]);
                point.x += face == 1 ? -dent : dent;
            }
            else if (face == 3 || face == 4)
            {
                dent = Bowl(point.x, point.y, _sideHoles[face - 1]);
                point.z += face == 3 ? dent : -dent;
            }
            return point;
        }

        /// <summary>a, b를 holes의 중심 및 반경과 비교하여 둥근 구멍의 함몰 깊이를 반환한다. 가장자리에서 깊이 변화가 부드럽게 이어지도록 한다.</summary>
        private float Bowl(float a, float b, Vector4[] holes)
        {
            float depth = 0f;
            foreach (Vector4 hole in holes)
            {
                float radiusSquared = ((a - hole.x) * (a - hole.x) + (b - hole.y) * (b - hole.y)) / (hole.z * hole.z);
                depth = Mathf.Max(depth, hole.w * Mathf.Pow(Mathf.Max(0f, 1f - radiusSquared), 1.5f));
            }
            return depth;
        }

        /// <summary>point의 위치를 정점 목록에 등록하고 인덱스를 반환한다. 겹치는 경계 좌표를 공유하여 모서리와 재질 경계의 법선을 연결한다.</summary>
        private int AddVertex(Vector3 point)
        {
            var key = (Mathf.RoundToInt(point.x * 1000000f), Mathf.RoundToInt(point.y * 1000000f), Mathf.RoundToInt(point.z * 1000000f));
            if (_indices.TryGetValue(key, out int index)) return index;
            index = _vertices.Count;
            _indices.Add(key, index);
            _vertices.Add(point);
            _uv.Add(new Vector2(point.x + 0.5f, point.z + 0.5f));
            return index;
        }

        /// <summary>a, b, c 정점으로 normal 방향의 삼각형을 추가한다. material에 해당하는 표면 또는 구멍 내부 슬롯에 저장한다.</summary>
        private void AddTriangle(int a, int b, int c, Vector3 normal, int material = 0)
        {
            Vector3 cross = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
            if (cross.sqrMagnitude < 0.000000000000001f) return;
            if (Vector3.Dot(cross, normal) < 0f) (b, c) = (c, b);
            List<int> target = _triangles[material];
            target.Add(a); target.Add(b); target.Add(c);
        }

        /// <summary>누적된 외피 정점과 두 재질 슬롯으로 Unity 메시를 반환한다. 읽기 가능한 메시의 법선, 탄젠트 및 경계를 계산한다.</summary>
        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh { name = "Keycap_1u_Cheese", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uv);
            mesh.subMeshCount = 2;
            for (int index = 0; index < _triangles.Length; index++) mesh.SetTriangles(_triangles[index], index);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
