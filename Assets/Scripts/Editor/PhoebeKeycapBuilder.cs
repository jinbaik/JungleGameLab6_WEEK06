using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;

using Game.Tournament;

public static class PhoebeKeycapBuilder
{
    private const string MESH_PATH = "Assets/Resource/Meshes/Phoebe/Keycap_1u_Phoebe.asset";
    private const string MATERIAL_ROOT = "Assets/Resource/Materials/Phoebe";
    private const string PART_PATH = "Assets/Resource/Tournament/Parts/keycap-Phoebe.asset";
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Keycaps/Phoebe/Keycap_1u_Phoebe.prefab";

    /// <summary>기존 1u 키캡 규격으로 Phoebe 메시, 재질과 부품 SO를 생성한다. 카탈로그 및 재사용 프리팹을 갱신하고 Unity 렌더링 미리보기를 저장한다.</summary>
    [MenuItem("Tools/Keyboard/Build Phoebe Keycap")]
    public static void Build()
    {
        foreach (string directory in new[] { Path.GetDirectoryName(MESH_PATH), MATERIAL_ROOT, Path.GetDirectoryName(PREFAB_PATH), "output/Phoebe-keycap" })
            Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        Material white = CreateMaterial("Phoebe_White", new Color(0.94f, 0.955f, 0.98f));
        Material ink = CreateMaterial("Phoebe_Ink", new Color(0.055f, 0.042f, 0.072f));
        Material hair = CreateMaterial("Phoebe_Blonde", new Color(0.88f, 0.79f, 0.50f));
        Material skin = CreateMaterial("Phoebe_Skin", new Color(1f, 0.86f, 0.77f));
        Material purple = CreateMaterial("Phoebe_Violet", new Color(0.34f, 0.20f, 0.49f));
        Material lavender = CreateMaterial("Phoebe_Lavender", new Color(0.66f, 0.56f, 0.87f));
        Material blue = CreateMaterial("Phoebe_Azure", new Color(0.14f, 0.48f, 0.78f));
        Material gold = CreateMaterial("Phoebe_Gold", new Color(0.96f, 0.78f, 0.39f));
        Material shadow = CreateMaterial("Phoebe_HairShadow", new Color(0.56f, 0.46f, 0.25f));
        Material[] materials = { white, ink, hair, skin, purple, lavender, blue, gold, shadow };
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
        data.FindProperty("_id").stringValue = "keycap-Phoebe";
        data.FindProperty("_displayName").stringValue = "Phoebe 키캡";
        data.FindProperty("_price").intValue = 60;
        data.FindProperty("_quantity").intValue = 3;
        data.FindProperty("_material").objectReferenceValue = white;
        data.FindProperty("_materialId").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(white)) + ":2100000";
        data.FindProperty("_shellMesh").objectReferenceValue = mesh;
        SetReferences(data.FindProperty("_modelMaterials"), materials);
        data.FindProperty("_oneUnitOnly").boolValue = true;
        if (data.FindProperty("_ability").objectReferenceValue == null)
            data.FindProperty("_ability").objectReferenceValue = AssetDatabase.LoadAssetAtPath<KeycapAbilityDefinition>("Assets/Resource/Tournament/Abilities/keycap-Phoebe-ability.asset");
        data.ApplyModifiedPropertiesWithoutUndo();
        RegisterPart(part, materials);
        GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keycaps/Keycap_1u.prefab"));
        try
        {
            cap.name = "Keycap_1u_Phoebe";
            MeshRenderer shell = cap.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            new KeycapModelAppearance(shell).Apply(part);
            PrefabUtility.SaveAsPrefabAsset(cap, PREFAB_PATH);
        }
        finally { UnityEngine.Object.DestroyImmediate(cap); }
        AssetDatabase.SaveAssets();
        ExportPreview(mesh, materials);
        Debug.Log($"PHOEBE_CREATED mesh={MESH_PATH} submeshes={mesh.subMeshCount} vertices={mesh.vertexCount} bounds={mesh.bounds} part={PART_PATH}");
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

    /// <summary>앉은 SD 캐릭터의 머리와 긴 머리카락, 모자 및 의상을 입체 메시로 구성한다. 아홉 재질 슬롯을 갖는 1u Phoebe 키캡 메시를 반환한다.</summary>
    private static Mesh CreateMesh()
    {
        PhoebeMesh builder = new PhoebeMesh();
        builder.Ellipsoid(new Vector3(0f, 0.30f, 0.22f), new Vector3(0.30f, 0.23f, 0.10f), 2, true);
        builder.Ellipsoid(new Vector3(0f, 0.56f, 0.055f), new Vector3(0.325f, 0.255f, 0.225f), 2, true);
        builder.Ellipsoid(new Vector3(0f, 0.535f, -0.035f), new Vector3(0.275f, 0.218f, 0.215f), 3, true);
        foreach (float sign in new[] { -1f, 1f })
        {
            builder.Ellipsoid(new Vector3(sign * 0.265f, 0.325f, 0.07f), new Vector3(0.070f, 0.25f, 0.12f), 2, true);
            builder.Ellipsoid(new Vector3(sign * 0.275f, 0.53f, -0.025f), new Vector3(0.065f, 0.17f, 0.12f), 2, true);
            builder.HeadStroke(new[] { new Vector3(sign * 0.30f, 0.57f, -0.10f), new Vector3(sign * 0.30f, 0.43f, -0.07f), new Vector3(sign * 0.28f, 0.28f, -0.025f) }, 0.004f, 8);
        }
        builder.Fringe();
        foreach (float sign in new[] { -1f, 1f })
        {
            float x = sign * 0.115f;
            builder.Ellipsoid(new Vector3(x, 0.515f, -0.234f), new Vector3(0.077f, 0.084f, 0.015f), 1, true);
            builder.Ellipsoid(new Vector3(x, 0.511f, -0.248f), new Vector3(0.067f, 0.074f, 0.012f), 4, true);
            builder.Ellipsoid(new Vector3(x, 0.482f, -0.260f), new Vector3(0.057f, 0.040f, 0.007f), 5, true);
            builder.Ellipsoid(new Vector3(x - 0.013f, 0.552f, -0.263f), new Vector3(0.023f, 0.015f, 0.007f), 0, true);
            builder.Ellipsoid(new Vector3(x + 0.030f, 0.480f, -0.269f), new Vector3(0.010f, 0.009f, 0.004f), 0, true);
            builder.HeadStroke(new[] { new Vector3(x - 0.062f, 0.607f, -0.205f), new Vector3(x - 0.022f, 0.620f, -0.223f), new Vector3(x + 0.026f, 0.618f, -0.222f), new Vector3(x + 0.063f, 0.601f, -0.205f) }, 0.007f, 1);
        }
        builder.HeadStroke(new[] { new Vector3(-0.018f, 0.412f, -0.215f), new Vector3(-0.008f, 0.405f, -0.219f), new Vector3(0.008f, 0.405f, -0.219f), new Vector3(0.018f, 0.412f, -0.215f) }, 0.004f, 1);
        builder.Cylinder(new Vector3(0f, 0.80f, 0.025f), new Vector2(0.443f, 0.30f), new Vector2(0.430f, 0.288f), 0.035f, 1, true);
        builder.Cylinder(new Vector3(0f, 0.796f, 0.025f), new Vector2(0.435f, 0.293f), new Vector2(0.425f, 0.282f), 0.045f, 0, true);
        builder.Cylinder(new Vector3(0f, 0.84f, 0.06f), new Vector2(0.300f, 0.205f), new Vector2(0.245f, 0.175f), 0.125f, 1, true);
        builder.Cylinder(new Vector3(0f, 0.849f, 0.06f), new Vector2(0.3005f, 0.206f), new Vector2(0.289f, 0.200f), 0.020f, 4, true);
        for (int emblem = -1; emblem <= 1; emblem++)
            builder.HeadStroke(new[] { new Vector3(emblem * 0.10f - 0.035f, 0.905f, -0.132f), new Vector3(emblem * 0.10f, 0.925f, -0.126f), new Vector3(emblem * 0.10f + 0.035f, 0.905f, -0.132f) }, 0.009f, 0);
        builder.Ellipsoid(new Vector3(-0.30f, 0.765f, -0.12f), new Vector3(0.065f, 0.028f, 0.035f), 0, true);
        builder.Ellipsoid(new Vector3(-0.31f, 0.715f, -0.13f), new Vector3(0.026f, 0.060f, 0.026f), 0, true);
        builder.HeadStroke(new[] { new Vector3(0.194f, 0.698f, -0.202f), new Vector3(0.236f, 0.651f, -0.205f) }, 0.016f, 1);
        builder.HeadStroke(new[] { new Vector3(0.192f, 0.659f, -0.209f), new Vector3(0.237f, 0.685f, -0.210f) }, 0.012f, 6);
        builder.Cylinder(new Vector3(0f, 0.105f, 0f), new Vector2(0.205f, 0.16f), new Vector2(0.13f, 0.10f), 0.19f, 0);
        builder.Ellipsoid(new Vector3(0f, 0.265f, -0.018f), new Vector3(0.132f, 0.135f, 0.106f), 1);
        builder.Polygon(new[] { new Vector3(-0.07f, 0.34f, -0.13f), new Vector3(0.07f, 0.34f, -0.13f), new Vector3(0.045f, 0.22f, -0.145f), new Vector3(0f, 0.19f, -0.15f), new Vector3(-0.045f, 0.22f, -0.145f) }, 0.012f, 0);
        builder.Polygon(new[] { new Vector3(-0.045f, 0.338f, -0.16f), new Vector3(0.028f, 0.338f, -0.16f), new Vector3(0.030f, 0.14f, -0.17f), new Vector3(-0.012f, 0.16f, -0.18f), new Vector3(-0.047f, 0.13f, -0.17f) }, 0.012f, 6);
        builder.Stroke(new[] { new Vector3(0.06f, 0.29f, -0.137f), new Vector3(0.085f, 0.25f, -0.14f), new Vector3(0.15f, 0.255f, -0.12f) }, 0.018f, 4);
        builder.Stroke(new[] { new Vector3(0.07f, 0.30f, -0.16f), new Vector3(0.07f, 0.255f, -0.16f) }, 0.005f, 7);
        builder.Stroke(new[] { new Vector3(0.055f, 0.278f, -0.16f), new Vector3(0.085f, 0.278f, -0.16f) }, 0.005f, 7);
        foreach (float sign in new[] { -1f, 1f })
        {
            builder.Ellipsoid(new Vector3(sign * 0.172f, 0.225f, -0.008f), new Vector3(0.062f, 0.138f, 0.078f), 0);
            builder.Cylinder(new Vector3(sign * 0.18f, 0.105f, -0.012f), new Vector2(0.060f, 0.076f), new Vector2(0.060f, 0.076f), 0.027f, 1);
            builder.Ellipsoid(new Vector3(sign * 0.184f, 0.083f, -0.033f), new Vector3(0.050f, 0.037f, 0.057f), 1);
            builder.Ellipsoid(new Vector3(sign * 0.105f, 0.074f, -0.205f), new Vector3(0.068f, 0.066f, 0.135f), 0);
            builder.Ellipsoid(new Vector3(sign * 0.108f, 0.065f, -0.30f), new Vector3(0.079f, 0.064f, 0.090f), 1);
            builder.Stroke(new[] { new Vector3(sign * 0.105f - 0.055f, 0.129f, -0.23f), new Vector3(sign * 0.105f, 0.140f, -0.23f), new Vector3(sign * 0.105f + 0.055f, 0.129f, -0.23f) }, 0.006f, 1);
        }
        builder.Ellipsoid(new Vector3(0.154f, 0.17f, -0.14f), new Vector3(0.020f, 0.022f, 0.012f), 7);
        return builder.ToMesh();
    }

    /// <summary>point를 머리 중심에서 위로 향하도록 회전하여 반환한다. 얼굴과 모자의 특징이 내려다보는 카메라에 드러나도록 한다.</summary>
    private static Vector3 HeadPoint(Vector3 point)
    {
        Vector3 pivot = new Vector3(0f, 0.54f, 0f);
        return pivot + Quaternion.Euler(24f, 0f, 0f) * (point - pivot);
    }

    /// <summary>mesh와 materials로 실제 Unity 렌더링 미리보기를 저장한다. 장착 축척으로 인접 일반 키와 비교한 이미지도 생성한다.</summary>
    private static void ExportPreview(Mesh mesh, Material[] materials)
    {
        RenderPreview(mesh, materials, false, "output/Phoebe-keycap/phoebe-front-unity.png", true);
        RenderPreview(mesh, materials, false, "output/Phoebe-keycap/phoebe-game-unity.png");
        RenderPreview(mesh, materials, true, "output/Phoebe-keycap/phoebe-neighbors-unity.png");
    }

    /// <summary>mesh와 materials를 미리보기 공간에 배치하여 path에 PNG로 저장한다. neighbors이면 1u 간격의 일반 키를 함께 표시하고 임시 자원을 해제한다.</summary>
    private static void RenderPreview(Mesh mesh, Material[] materials, bool neighbors, string path, bool frontal = false)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        GameObject group = new GameObject("PhoebePreview");
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
            preview.camera.orthographicSize = neighbors ? 2.2f : 0.68f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 20f;
            preview.camera.transform.position = frontal ? new Vector3(0f, 0.88f, -3f) : new Vector3(0.6f, 3.2f, -2.2f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.48f, 0f));
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

    private sealed class PhoebeMesh
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int>[] _triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

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

        /// <summary>center와 radius로 타원체를 material 서브메시에 추가한다. head이면 머리 각도를 적용하며 머리카락과 눈, 몸체의 실제 입체 형상을 구성한다.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radius, int material, bool head = false)
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
                    if (head) { a = HeadPoint(a); b = HeadPoint(b); c = HeadPoint(c); d = HeadPoint(d); }
                    Vector3 outward = (a + b + c + d) * 0.25f - (head ? HeadPoint(center) : center);
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

        /// <summary>이마 앞쪽에 일자로 떨어지는 곡면 앞머리를 추가한다. 머리 기울기와 얼굴 곡률을 따라 금발 외피와 짧은 머릿결을 구성한다.</summary>
        public void Fringe()
        {
            for (int segment = 0; segment < 20; segment++)
            {
                float x0 = -0.255f + segment * 0.51f / 20f;
                float x1 = -0.255f + (segment + 1) * 0.51f / 20f;
                Vector3 a = FringePoint(x0, false);
                Vector3 b = FringePoint(x1, false);
                Vector3 c = FringePoint(x1, true);
                Vector3 d = FringePoint(x0, true);
                Vector3 offset = Quaternion.Euler(24f, 0f, 0f) * Vector3.forward * 0.09f;
                Vector3 outward = Quaternion.Euler(24f, 0f, 0f) * Vector3.back;
                Triangle(a, b, c, 2, outward);
                Triangle(a, c, d, 2, outward);
                Triangle(a + offset, c + offset, b + offset, 2, -outward);
                Triangle(a + offset, d + offset, c + offset, 2, -outward);
                Triangle(a, a + offset, b + offset, 2, Vector3.down);
                Triangle(a, b + offset, b, 2, Vector3.down);
                Triangle(d, c, c + offset, 2, Vector3.up);
                Triangle(d, c + offset, d + offset, 2, Vector3.up);
                if (segment == 0) { Triangle(a, d, d + offset, 2, Vector3.left); Triangle(a, d + offset, a + offset, 2, Vector3.left); }
                if (segment == 19) { Triangle(b, b + offset, c + offset, 2, Vector3.right); Triangle(b, c + offset, c, 2, Vector3.right); }
            }
            foreach (float x in new[] { -0.18f, -0.09f, 0.085f, 0.17f })
            {
                float z = -0.238f + 0.105f * x * x / (0.255f * 0.255f);
                HeadStroke(new[] { new Vector3(x, 0.702f, z), new Vector3(x - 0.006f, 0.65f, z - 0.004f), new Vector3(x - 0.009f, 0.598f, z) }, 0.0023f, 8);
            }
        }

        /// <summary>x와 upper로 앞머리의 위아래 가장자리 좌표를 반환한다. 이마 곡률과 작은 끝단 굴곡을 반영하여 머리 좌표를 회전한다.</summary>
        private Vector3 FringePoint(float x, bool upper)
        {
            float ratio = x / 0.255f;
            float y = upper ? 0.755f - 0.06f * ratio * ratio : 0.588f + 0.004f * Mathf.Cos(x * 80f);
            float z = -0.235f + 0.105f * ratio * ratio;
            return HeadPoint(new Vector3(x, y, z));
        }

        /// <summary>center와 타원 단면, height를 사용하여 원통 또는 원뿔대를 material 슬롯에 추가한다. head이면 머리 기울기를 적용하며 모자와 의상의 입체 형상을 만든다.</summary>
        public void Cylinder(Vector3 center, Vector2 bottom, Vector2 top, float height, int material, bool head = false)
        {
            for (int side = 0; side < 48; side++)
            {
                float a = side * Mathf.PI * 2f / 48f;
                float b = (side + 1) * Mathf.PI * 2f / 48f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * bottom.x, 0f, Mathf.Sin(a) * bottom.y);
                Vector3 q = center + new Vector3(Mathf.Cos(b) * bottom.x, 0f, Mathf.Sin(b) * bottom.y);
                Vector3 r = center + new Vector3(Mathf.Cos(b) * top.x, height, Mathf.Sin(b) * top.y);
                Vector3 t = center + new Vector3(Mathf.Cos(a) * top.x, height, Mathf.Sin(a) * top.y);
                Vector3 down = center;
                Vector3 up = center + Vector3.up * height;
                if (head) { p = HeadPoint(p); q = HeadPoint(q); r = HeadPoint(r); t = HeadPoint(t); down = HeadPoint(down); up = HeadPoint(up); }
                Vector3 outward = (p + q + r + t) * 0.25f - (down + up) * 0.5f;
                Triangle(p, q, r, material, outward);
                Triangle(p, r, t, material, outward);
                Triangle(p, down, q, material, down - up);
                Triangle(t, r, up, material, up - down);
            }
        }

        /// <summary>points를 depth 두께로 돌출하여 material 슬롯에 추가한다. 넥타이와 의상 앞판의 입체 다각형을 구성한다.</summary>
        public void Polygon(Vector3[] points, float depth, int material)
        {
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in points) center += point / points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % points.Length];
                Vector3 offset = Vector3.forward * depth;
                Triangle(center, a, b, material, Vector3.back);
                Triangle(center + offset, b + offset, a + offset, material, Vector3.forward);
                Triangle(a, a + offset, b + offset, material, (a + b) * 0.5f - center);
                Triangle(a, b + offset, b, material, (a + b) * 0.5f - center);
            }
        }

        /// <summary>머리 좌표의 points에 기울기를 적용하고 radius 두께의 material 입체 선을 추가한다. 눈썹과 장식, 머리카락 결을 표현한다.</summary>
        public void HeadStroke(Vector3[] points, float radius, int material)
        {
            Vector3[] rotated = new Vector3[points.Length];
            for (int index = 0; index < points.Length; index++) rotated[index] = HeadPoint(points[index]);
            Stroke(rotated, radius, material);
        }

        /// <summary>points를 radius 두께의 입체 선으로 연결하여 material 서브메시에 추가한다. 의상과 얼굴의 작은 윤곽을 형성한다.</summary>
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
            Mesh mesh = new Mesh { name = "Keycap_1u_Phoebe", indexFormat = IndexFormat.UInt32 };
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            Dictionary<(int, int, int, int), int> shared = new Dictionary<(int, int, int, int), int>();
            mesh.subMeshCount = _triangles.Length;
            List<int>[] indices = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
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
