using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

using Game.Tournament;

public static class BongoCatKeycapBuilder
{
    private const string MESH_PATH = "Assets/Resource/Meshes/BongoCat/Keycap_1u_BongoCat.asset";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/BongoCat";
    private const string PART_PATH = "Assets/Resource/Tournament/Parts/keycap-bongocat.asset";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/BongoCat/Keycap_1u_BongoCat.prefab";

    /// <summary>기존 1u 키캡 규격으로 봉고캣 메시, 재질과 부품 SO를 생성한다. 카탈로그 및 재사용 프리팹을 갱신하고 Unity 렌더링 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Bongo Cat Keycap")]
    public static void Build()
    {
        foreach (string directory in new[] { Path.GetDirectoryName(MESH_PATH), MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/bongocat-keycap" })
            Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        Material white = CreateMaterial("BongoCat_White", new Color(0.94f, 0.955f, 0.98f));
        Material black = CreateMaterial("BongoCat_Ink", new Color(0.012f, 0.017f, 0.026f));
        Material pink = CreateMaterial("BongoCat_Pink", new Color(1f, 0.48f, 0.61f));
        Material[] materials = { white, black, pink };
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
        data.FindProperty("_id").stringValue = "keycap-bongocat";
        data.FindProperty("_displayName").stringValue = "봉고캣 키캡";
        data.FindProperty("_price").intValue = 60;
        data.FindProperty("_quantity").intValue = 3;
        data.FindProperty("_material").objectReferenceValue = white;
        data.FindProperty("_materialId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(white)) + ":2100000";
        data.FindProperty("_shellMesh").objectReferenceValue = mesh;
        SetReferences(data.FindProperty("_modelMaterials"), materials);
        data.FindProperty("_oneUnitOnly").boolValue = true;
        if (data.FindProperty("_ability").objectReferenceValue == null)
            data.FindProperty("_ability").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KeycapAbilityDefinition>("Assets/Resource/Tournament/Abilities/keycap-bongocat-ability.asset");
        data.ApplyModifiedPropertiesWithoutUndo();
        RegisterPart(part, materials);
        GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keycaps/Keycap_1u.prefab"));
        try
        {
            cap.name = "Keycap_1u_BongoCat";
            MeshRenderer shell = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            new KeycapModelAppearance(shell).Apply(part);
            PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_PATH);
        }
        finally { UnityEngine.Object.DestroyImmediate(cap); }
        AssetDatabase.SaveAssets();
        ExportPreview(mesh, materials);
        Debug.Log($"BONGO_CAT_CREATED mesh={MESH_PATH} submeshes={mesh.subMeshCount} vertices={mesh.vertexCount} bounds={mesh.bounds} part={PART_PATH}");
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

    /// <summary>흰 몸통과 앞발, 삼각 귀 및 입체 얼굴을 결합한 1u 메시를 반환한다. 흰색, 검정, 분홍 순서의 세 서브메시로 구성한다.</summary>
    private static Mesh CreateMesh()
    {
        CatMesh builder = new CatMesh();
        builder.Body();
        foreach (float sign in new[] { -1f, 1f })
        {
            builder.Ear(sign);
            Vector3 paw = sign < 0f ? new Vector3(-0.345f, 0.35f, 0.085f) : new Vector3(0.285f, 0.35f, -0.185f);
            builder.Ellipsoid(paw, new Vector3(0.087f, 0.10f, 0.115f), 0);
            Vector3[] pawOutline = new Vector3[11];
            for (int point = 0; point < pawOutline.Length; point++)
            {
                float angle = -0.25f * Mathf.PI + point * 1.5f * Mathf.PI / (pawOutline.Length - 1);
                pawOutline[point] = paw + new Vector3(Mathf.Cos(angle) * 0.067f, 0.065f, Mathf.Sin(angle) * 0.085f);
            }
            builder.Stroke(pawOutline, 0.0045f, 1);
            builder.Ellipsoid(paw + new Vector3(0f, 0.102f, -0.005f), new Vector3(0.025f, 0.009f, 0.032f), 2);
            for (int toe = 0; toe < 3; toe++)
                builder.Ellipsoid(paw + new Vector3((toe - 1) * 0.026f, 0.096f, 0.05f + (toe == 1 ? 0.014f : 0f)), new Vector3(0.01f, 0.007f, 0.013f), 2);
            Vector3 eye = sign < 0f ? FacePoint(-0.145f, 0.055f) : FacePoint(0.13f, -0.08f);
            builder.Ellipsoid(eye, new Vector3(0.022f, 0.012f, 0.024f), 1);
        }
        builder.Stroke(new[] { FacePoint(-0.075f, 0.016f), FacePoint(-0.068f, -0.004f), FacePoint(-0.047f, -0.012f), FacePoint(-0.026f, 0.002f), FacePoint(-0.025f, -0.022f), FacePoint(-0.005f, -0.027f), FacePoint(0.012f, -0.012f) }, 0.007f, 1);
        return builder.ToMesh();
    }

    /// <summary>x와 z를 몸통의 윗면 곡면에 투영한 얼굴 좌표로 반환한다. 눈과 입이 위쪽 카메라에 보이도록 표면 위에 배치한다.</summary>
    private static Vector3 FacePoint(float x, float z)
    {
        float y = 0.10f + 0.36f * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (0.44f * 0.44f) - (z + 0.055f) * (z + 0.055f) / (0.33f * 0.33f)));
        return new Vector3(x, y + 0.012f, z);
    }

    /// <summary>mesh와 materials로 실제 Unity 렌더링 미리보기를 저장한다. 장착 축척으로 인접 일반 키와 비교한 이미지도 생성한다.</summary>
    private static void ExportPreview(Mesh mesh, Material[] materials)
    {
        RenderPreview(mesh, materials, false, "output/bongocat-keycap/bongocat-unity.png");
        RenderPreview(mesh, materials, true, "output/bongocat-keycap/bongocat-neighbors-unity.png");
    }

    /// <summary>mesh와 materials를 미리보기 공간에 배치하여 path에 PNG로 저장한다. neighbors이면 1u 간격의 일반 키를 함께 표시하고 임시 자원을 해제한다.</summary>
    private static void RenderPreview(Mesh mesh, Material[] materials, bool neighbors, string path)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject group = new GameObject("BongoCatPreview");
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
            preview.camera.transform.position = neighbors ? new Vector3(0.7f, 3.2f, -1.8f) : new Vector3(0f, 3.6f, -1.2f);
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

    private sealed class CatMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int>[] _triangles = { new List<int>(), new List<int>(), new List<int>() };

        /// <summary>a, b, c를 material 서브메시에 추가한다. outward 기준으로 삼각형 방향을 맞추고 위치 기반 UV를 기록한다.</summary>
        private void Triangle(Vector3 a, Vector3 b, Vector3 c, int material, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
            foreach (Vector3 point in new[] { a, b, c })
            {
                _triangles[material].Add(_vertices.Count);
                _vertices.Add(point);
                _uv.Add(new Vector2(point.x + 0.5f, point.z + 0.5f));
            }
        }

        /// <summary>좌표 원점에서 장착되는 속이 빈 둥근 몸통 외피를 추가한다. 1u 폭 안에 몸체와 얼굴을 위한 낮은 돔을 구성한다.</summary>
        public void Body()
        {
            const int SIDES = 64;
            const int RINGS = 16;
            for (int ring = 0; ring < RINGS; ring++)
                for (int side = 0; side < SIDES; side++)
                {
                    Vector3 a = BodyPoint(ring, side, RINGS, SIDES);
                    Vector3 b = BodyPoint(ring, side + 1, RINGS, SIDES);
                    Vector3 c = BodyPoint(ring + 1, side + 1, RINGS, SIDES);
                    Vector3 d = BodyPoint(ring + 1, side, RINGS, SIDES);
                    Vector3 outward = (a + b + c + d) * 0.25f - new Vector3(0f, 0.12f, 0f);
                    Triangle(a, b, c, 0, outward);
                    Triangle(a, c, d, 0, outward);
                }
        }

        /// <summary>ring과 side를 몸통 단면의 좌표로 변환한다. rings와 sides를 해상도로 사용하며 하단은 기존 소켓을 위한 개방 구조로 유지한다.</summary>
        private Vector3 BodyPoint(int ring, int side, int rings, int sides)
        {
            float angle = side * Mathf.PI * 2f / sides;
            if (ring == 0) return new Vector3(Mathf.Cos(angle) * 0.445f, 0f, Mathf.Sin(angle) * 0.34f - 0.055f);
            float latitude = (ring - 1f) / (rings - 1f) * Mathf.PI * 0.5f;
            return new Vector3(Mathf.Cos(angle) * Mathf.Cos(latitude) * 0.44f,
                0.10f + Mathf.Sin(latitude) * 0.36f, Mathf.Sin(angle) * Mathf.Cos(latitude) * 0.33f - 0.055f);
        }

        /// <summary>center와 radius로 타원체를 material 서브메시에 추가한다. 앞발과 눈, 볼의 실제 입체 형상을 구성한다.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radius, int material)
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

        /// <summary>sign에 따라 참고 이미지의 위쪽 귀와 오른쪽 귀를 추가한다. 흰색 삼각 입체와 검은 테두리를 몸통 외곽에 연결한다.</summary>
        public void Ear(float sign)
        {
            Vector3 a = sign < 0f ? new Vector3(-0.15f, 0.30f, 0.21f) : new Vector3(0.28f, 0.27f, 0.11f);
            Vector3 b = sign < 0f ? new Vector3(0.015f, 0.31f, 0.23f) : new Vector3(0.40f, 0.22f, -0.025f);
            Vector3 c = sign < 0f ? new Vector3(-0.065f, 0.355f, 0.405f) : new Vector3(0.435f, 0.325f, 0.245f);
            Vector3 offset = Vector3.down * 0.11f;
            Triangle(a, b, c, 0, Vector3.up);
            Triangle(a + offset, c + offset, b + offset, 0, Vector3.down);
            Triangle(a, c, c + offset, 0, Vector3.left);
            Triangle(a, c + offset, a + offset, 0, Vector3.left);
            Triangle(b, b + offset, c + offset, 0, Vector3.right);
            Triangle(b, c + offset, c, 0, Vector3.right);
            Stroke(new[] { a, Vector3.Lerp(a, c, 0.80f), c, Vector3.Lerp(c, b, 0.20f), b }, 0.006f, 1);
        }

        /// <summary>points를 radius 두께의 입체 선으로 연결하여 material 서브메시에 추가한다. 입과 발끝의 작은 윤곽을 형성한다.</summary>
        public void Stroke(Vector3[] points, float radius, int material)
        {
            foreach (Vector3 point in points) Ellipsoid(point, Vector3.one * radius, material);
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector3 a = points[index];
                Vector3 b = points[index + 1];
                Vector3 direction = (b - a).normalized;
                Vector3 right = Vector3.Cross(direction, Vector3.up).normalized * radius;
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
            Mesh mesh = new Mesh { name = "Keycap_1u_BongoCat", indexFormat = IndexFormat.UInt32 };
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            Dictionary<(int, int, int, int), int> shared = new Dictionary<(int, int, int, int), int>();
            mesh.subMeshCount = _triangles.Length;
            List<int>[] indices = { new List<int>(), new List<int>(), new List<int>() };
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
