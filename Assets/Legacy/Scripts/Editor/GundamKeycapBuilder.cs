using System;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class GundamKeycapBuilder
    {
        private const string ROOT = "Assets/Resource";
        private const string CAPS = ROOT + "/Prefabs/Keycaps";
        private const string OUTPUT = CAPS + "/Gundam";
        private const string MATERIALS = ROOT + "/Materials/Gundam";
        private const string MESHES = ROOT + "/Meshes/Gundam";
        private const string PREVIEW = ".local/GundamKeycapPreview";

        /// <summary>
        /// 기존 크기별 키캡과 네 가지 장갑 색상을 사용하여 건담풍 키캡 버리언트를 생성한다.
        /// 원본의 파괴 및 입력 컴포넌트를 상속하고 패널 재질, 표식, 전용 UV 메시와 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Gundam Keycaps")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬을 보호하기 위해 별도 복사 프로젝트의 배치 모드에서 실행합니다.");

            Directory.CreateDirectory(OUTPUT);
            Directory.CreateDirectory(MATERIALS);
            Directory.CreateDirectory(MESHES);
            Directory.CreateDirectory(PREVIEW);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string[] themes = { "White", "Blue", "Red", "Yellow" };
            Color[] colors = { new Color(0.87f, 0.90f, 0.92f), new Color(0.07f, 0.23f, 0.64f), new Color(0.72f, 0.10f, 0.13f), new Color(0.98f, 0.66f, 0.08f) };
            string[] sizes = { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u", "1u_Vertical2u" };
            Material[] materials = new Material[themes.Length];
            for (int index = 0; index < themes.Length; index++)
                materials[index] = CreateMaterial(themes[index], colors[index]);

            foreach (string size in sizes)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(CAPS + "/Keycap_" + size + ".prefab");
                Mesh sourceMesh = source.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>().sharedMesh;
                Mesh mesh = CreatePanelMesh(sourceMesh, size);
                for (int index = 0; index < themes.Length; index++)
                {
                    GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    try
                    {
                        cap.name = "Keycap_" + size + "_Gundam_" + themes[index];
                        Transform shell = cap.transform.Find("PBT_SculptedShell");
                        MeshFilter filter = shell.GetComponent<MeshFilter>();
                        MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
                        filter.sharedMesh = mesh;
                        renderer.sharedMaterial = materials[index];
                        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        float width = mesh.bounds.size.x;
                        AddMarking(cap.transform, width > 1.8f ? "RX-78 // GUNDAM" : "78", new Vector3(0f, 0.545f, 0.025f), width > 1.8f ? 0.065f : 0.10f);
                        AddMarking(cap.transform, "E.F.S.F.", new Vector3(0f, 0.545f, -0.19f), 0.031f);
                        AddMarking(cap.transform, "CAUTION", new Vector3(0f, 0.545f, 0.20f), 0.023f);
                        GameObject saved = PrefabUtility.SaveAsPrefabAsset(cap, OUTPUT + "/" + cap.name + ".prefab");
                        if (PrefabUtility.GetPrefabAssetType(saved) != PrefabAssetType.Variant)
                            throw new InvalidOperationException("키캡 버리언트 저장 실패: " + cap.name);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(cap);
                    }
                }
            }
            AssetDatabase.SaveAssets();
            RenderPreview();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject keyboard = BuildKeyboard();
            RenderKeyboardPreview(keyboard);
            AddToKeyboardScene(keyboard);
            File.WriteAllText(PREVIEW + "/BuildReport.txt", "Keycap variants: 36\nSizes: 9\nColors: White, Blue, Red, Yellow\nOriginal input, health and break effects inherited.\n");
            Debug.Log("GUNDAM_KEYCAPS_COMPLETE variants=36");
        }

        /// <summary>
        /// 기본 게임용 키보드의 키 위치와 입력 연결을 유지하며 건담풍 테마를 적용한다.
        /// 생성된 패널 메시와 색상 재질로 키캡을 변경하고 프리팹을 저장하지 않은 전시 인스턴스를 반환한다.
        /// </summary>
        private static GameObject BuildKeyboard()
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Prefabs/Keyboard_TKL_87.prefab"));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            try
            {
                instance.name = "Keyboard_TKL_87_Gundam_Game";
                Material ink = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "/GundamLegend.mat");
                if (ink == null)
                {
                    ink = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/KeyboardGoldLegend.mat"));
                    AssetDatabase.CreateAsset(ink, MATERIALS + "/GundamLegend.mat");
                }
                ink.SetColor("_InkColor", new Color(0.015f, 0.025f, 0.04f));
                EditorUtility.SetDirty(ink);
                foreach (KeycapButton button in instance.GetComponentsInChildren<KeycapButton>(true))
                {
                    Transform key = button.transform;
                    Transform shell = key.Find("PBT_SculptedShell");
                    MeshFilter filter = shell.GetComponent<MeshFilter>();
                    MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
                    string size = filter.sharedMesh.name.Substring("Keycap_".Length);
                    string theme = GetKeyTheme(key.name);
                    filter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESHES + "/Keycap_" + size + "_GundamPanel.asset");
                    renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "/GundamPanel_" + theme + ".mat");
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    foreach (TextMesh label in key.GetComponentsInChildren<TextMesh>(true))
                    {
                        label.color = new Color(0.015f, 0.025f, 0.04f);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                        MeshRenderer labelRenderer = label.GetComponent<MeshRenderer>();
                        labelRenderer.sharedMaterial = ink;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(labelRenderer);
                    }
                    AddMarking(key, "E.F.S.F.", new Vector3(0f, 0.545f, -0.20f), 0.022f);
                }
                SerializedObject legends = new SerializedObject(instance.GetComponent<KeyboardLegendRenderer>());
                legends.FindProperty("_materialTemplate").objectReferenceValue = ink;
                legends.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<KeyboardLegendRenderer>());
                UnityEngine.Object.DestroyImmediate(instance.GetComponent<KeyboardPlayableVariant>());
                SerializedProperty bindings = new SerializedObject(instance.GetComponent<KeyboardInputController>()).FindProperty("_bindings");
                if (bindings.arraySize != 87 || instance.GetComponentsInChildren<KeycapHealth>(true).Length != 87)
                    throw new InvalidOperationException("건담 키보드의 입력 또는 체력 개수가 87개가 아닙니다.");
                for (int index = 0; index < bindings.arraySize; index++)
                    if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                        throw new InvalidOperationException("건담 키보드 입력 대상 누락: " + index);
                AssetDatabase.SaveAssets();
                return instance;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(instance);
                throw;
            }
        }

        /// <summary>
        /// keyName의 기능을 기준으로 참고 이미지의 포인트 색상을 선택한다.
        /// 방향키와 Enter는 빨강, F1부터 F4는 노랑, 보조 키는 파랑이며 나머지는 흰색 이름을 반환한다.
        /// </summary>
        private static string GetKeyTheme(string keyName)
        {
            if (keyName == "Key_Enter" || keyName == "Key_<" || keyName == "Key_>" || keyName == "Key_^" || keyName == "Key_v")
                return "Red";
            if (keyName == "Key_F1" || keyName == "Key_F2" || keyName == "Key_F3" || keyName == "Key_F4" || keyName == "Key_Esc")
                return "Yellow";
            if (keyName == "Key_Shift" || keyName == "Key_Ctrl" || keyName == "Key_Alt" || keyName == "Key_Space" || keyName == "Key_Tab" || keyName == "Key_Backspace" || keyName == "Key_Caps_Lock" || keyName == "Key_Win" || keyName == "Key_Menu")
                return "Blue";
            return "White";
        }

        /// <summary>
        /// keyboard를 기존 Keyboard 씬의 비교 전시 공간에 배치한다.
        /// 기본 키보드의 위치와 크기를 기준으로 새 인스턴스를 추가하고 동일 이름의 이전 전시만 교체한다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject keyboard)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Additive);
            GameObject original = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Keyboard_TKL_87") original = root;
                if (root.name == "Keyboard_TKL_87_Gundam_Game") UnityEngine.Object.DestroyImmediate(root);
            }
            GameObject model = keyboard;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model, scene);
            model.transform.localScale = original.transform.lossyScale;
            model.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, 36f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// keyboard의 실제 키 배색과 각인을 별도의 이미지로 렌더링한다.
        /// 임시 전시 인스턴스와 카메라 및 조명으로 키보드 PNG를 저장하고 렌더 리소스를 해제한다.
        /// </summary>
        private static void RenderKeyboardPreview(GameObject keyboard)
        {
            GameObject model = UnityEngine.Object.Instantiate(keyboard);
            model.transform.localScale = Vector3.one;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string characters = "";
            foreach (TextMesh label in model.GetComponentsInChildren<TextMesh>()) characters += label.text;
            font.RequestCharactersInTexture(characters, 96);
            Material ink = new Material(AssetDatabase.LoadAssetAtPath<Material>(MATERIALS + "/GundamLegend.mat"));
            ink.mainTexture = font.material.mainTexture;
            foreach (TextMesh label in model.GetComponentsInChildren<TextMesh>()) label.GetComponent<MeshRenderer>().sharedMaterial = ink;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.gray;
            Light light = new GameObject("PreviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Camera camera = new GameObject("KeyboardPreviewCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(24f, 24f, -25f);
            camera.transform.LookAt(new Vector3(9f, 0.4f, 3f));
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.047f, 0.065f);
            RenderTexture target = new RenderTexture(1600, 1000, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW + "/GundamKeyboard.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(ink);
        }

        /// <summary>
        /// source의 외형을 유지하며 윗면과 측면을 구분한 UV 메시를 생성한다.
        /// size별 메시 에셋을 저장하고 패널 텍스처를 사용할 Mesh를 반환한다.
        /// </summary>
        private static Mesh CreatePanelMesh(Mesh source, string size)
        {
            Mesh mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = "Keycap_" + size + "_GundamPanel";
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = new Vector2[vertices.Length];
            Bounds bounds = mesh.bounds;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = vertices[index];
                if (normals[index].y > 0.65f)
                    uv[index] = new Vector2(Mathf.InverseLerp(bounds.min.x, bounds.max.x, point.x), 0.4f + 0.6f * Mathf.InverseLerp(bounds.min.z, bounds.max.z, point.z));
                else
                    uv[index] = new Vector2(Mathf.Abs(normals[index].x) > Mathf.Abs(normals[index].z) ? Mathf.InverseLerp(bounds.min.z, bounds.max.z, point.z) : Mathf.InverseLerp(bounds.min.x, bounds.max.x, point.x), 0.32f * Mathf.InverseLerp(bounds.min.y, bounds.max.y, point.y));
            }
            mesh.uv = uv;
            mesh.RecalculateTangents();
            string path = MESHES + "/" + mesh.name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
                AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            return mesh;
        }

        /// <summary>
        /// theme과 color로 장갑 색상 및 검은 패널 라인을 가진 재질을 생성한다.
        /// 원본 PBT 설정을 기반으로 PNG 텍스처와 재질을 저장하고 완성 재질을 반환한다.
        /// </summary>
        private static Material CreateMaterial(string theme, Color color)
        {
            const int resolution = 1024;
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, true);
            Color ink = new Color(0.018f, 0.027f, 0.045f);
            for (int y = 0; y < resolution; y++)
            {
                float v = y / (float)(resolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)(resolution - 1);
                    Color pixel = color;
                    if (v >= 0.4f)
                    {
                        float z = (v - 0.4f) / 0.6f;
                        float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(z, 1f - z));
                        float cornerCut = Mathf.Max(0f, 0.28f - z) + Mathf.Max(0f, z - 0.72f);
                        float border = 0.22f + cornerCut;
                        bool panel = Mathf.Abs(u - border) < 0.006f || Mathf.Abs(u - (1f - border)) < 0.006f;
                        panel |= (u > border && u < 1f - border) && (Mathf.Abs(z - 0.22f) < 0.006f || Mathf.Abs(z - 0.78f) < 0.006f);
                        panel |= (u < 0.12f || u > 0.88f) && Mathf.Abs(z - 0.5f) < 0.006f;
                        if (edge < 0.075f) pixel *= 0.88f;
                        if (panel) pixel = ink;
                        if (z > 0.73f && z < 0.79f && u > 0.18f && u < 0.29f)
                            pixel = theme == "White" ? new Color(0.72f, 0.12f, 0.13f) : ink;
                        if (z > 0.2f && z < 0.33f && u > 0.75f && u < 0.82f && ((int)(z * 150f) % 3 == 0))
                            pixel = ink;
                    }
                    else
                    {
                        float height = v / 0.32f;
                        if (Mathf.Abs(u - 0.16f) < 0.006f || Mathf.Abs(u - 0.84f) < 0.006f || Mathf.Abs(height - 0.25f) < 0.013f)
                            pixel = ink;
                        if (u > 0.32f && u < 0.67f && height > 0.38f && height < 0.65f && (int)(u * 70f) % 3 == 0)
                            pixel = Color.Lerp(color, ink, 0.7f);
                    }
                    texture.SetPixel(x, y, pixel);
                }
            }
            texture.Apply();
            string texturePath = MATERIALS + "/GundamPanel_" + theme + ".png";
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            string materialPath = MATERIALS + "/GundamPanel_" + theme + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/Porcelain_PBT.mat"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.name = "GundamPanel_" + theme;
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetFloat("_Smoothness", 0.3f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// parent의 윗면에 text를 정비 표식으로 추가한다.
        /// position과 characterSize로 각인 위치 및 크기를 설정하고 검은 TextMesh 상태를 저장한다.
        /// </summary>
        private static void AddMarking(Transform parent, string text, Vector3 position, float characterSize)
        {
            GameObject label = new GameObject("GundamMarking_" + text.Replace('/', '_'));
            label.transform.SetParent(parent, false);
            label.transform.localPosition = position;
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh marking = label.AddComponent<TextMesh>();
            marking.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            marking.fontSize = 96;
            marking.characterSize = characterSize / 4f;
            marking.anchor = TextAnchor.MiddleCenter;
            marking.alignment = TextAlignment.Center;
            marking.color = new Color(0.015f, 0.025f, 0.04f);
            marking.text = text;
            label.GetComponent<MeshRenderer>().sharedMaterial = marking.font.material;
            label.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// 생성된 네 색상의 일반 키캡과 긴 키캡을 임시 씬에 배치하여 렌더링한다.
        /// 저장된 버리언트, 카메라와 조명으로 PNG 미리보기를 생성하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void RenderPreview()
        {
            string[] themes = { "White", "Blue", "Red", "Yellow" };
            for (int row = 0; row < themes.Length; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    string size = column == 3 ? "2_25u" : "1u";
                    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT + "/Keycap_" + size + "_Gundam_" + themes[row] + ".prefab");
                    GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    cap.transform.localScale = Vector3.one;
                    cap.transform.position = new Vector3(column * 1.2f + (column == 3 ? 0.6f : 0f), 0f, -row * 1.2f);
                }
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
            Light light = new GameObject("PreviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
            Camera camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(8f, 10f, -12f);
            camera.transform.LookAt(new Vector3(2f, 0.15f, -1.8f));
            camera.orthographic = true;
            camera.orthographicSize = 3.3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.047f, 0.065f);
            RenderTexture target = new RenderTexture(1600, 1200, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW + "/GundamKeycaps.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
