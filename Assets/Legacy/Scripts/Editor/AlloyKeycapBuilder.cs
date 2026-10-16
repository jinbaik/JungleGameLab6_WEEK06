using System;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class AlloyKeycapBuilder
    {
        private const string ROOT = "Assets/Resource";
        private const string CAPS = ROOT + "/Prefabs/Keycaps";
        private const string OUTPUT = CAPS + "/Alloy";
        private const string MATERIALS = ROOT + "/Materials/Alloy";
        private const string PREVIEW = ".local/AlloyKeycapPreview";

        /// <summary>
        /// 기존 아홉 크기의 키캡에 거친 철합금 재질을 적용한 버리언트를 생성한다.
        /// 원본 텍스트와 입력 및 파괴 구조를 유지하며 적용 키보드를 별도 프리팹 없이 Keyboard 씬에 배치한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Alloy Keycaps")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬을 보호하기 위해 별도 복사 프로젝트의 배치 모드에서 실행합니다.");
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory(OUTPUT);
            Directory.CreateDirectory(MATERIALS);
            Directory.CreateDirectory(PREVIEW);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material alloy = CreateAlloyMaterial();
            string[] sizes = { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u", "1u_Vertical2u" };
            foreach (string size in sizes)
            {
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CAPS + "/Keycap_" + size + ".prefab"));
                try
                {
                    cap.name = "Keycap_" + size + "_Alloy";
                    ApplyAlloy(cap.transform, alloy);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(cap, OUTPUT + "/" + cap.name + ".prefab");
                    if (PrefabUtility.GetPrefabAssetType(saved) != PrefabAssetType.Variant)
                        throw new InvalidOperationException("철합금 키캡 버리언트 저장 실패: " + size);
                }
                finally { UnityEngine.Object.DestroyImmediate(cap); }
            }
            AssetDatabase.SaveAssets();
            RenderPreview(sizes);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject keyboard = BuildDisplay(alloy);
            RenderKeyboardPreview(keyboard);
            AddToKeyboardScene(keyboard);
            File.WriteAllText(PREVIEW + "/BuildReport.txt", "Keycap variants: 9\nShared alloy materials: 1\nNew meshes: 0\nText settings: inherited unchanged\nInput bindings: 87\nHealth components: 87\nKeyboard prefab assets created: 0\n");
            Debug.Log("ALLOY_KEYCAPS_COMPLETE variants=9 keyboardPrefabs=0");
        }

        /// <summary>
        /// 기존 강철 재질에 엇갈린 사선 돌기가 반복되는 철판 무늬와 노멀 및 금속 반사 텍스처를 적용한다.
        /// 생성한 색상과 노멀 및 금속도 텍스처로 공용 Material 에셋을 저장하고 반환한다.
        /// </summary>
        private static Material CreateAlloyMaterial()
        {
            const int resolution = 1024;
            float[] heights = new float[resolution * resolution];
            Color[] albedo = new Color[heights.Length];
            Color[] mask = new Color[heights.Length];
            Color[] normals = new Color[heights.Length];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)resolution;
                    float v = y / (float)resolution;
                    int row = Mathf.FloorToInt(v * 6f);
                    float gridX = u * 6f + (row % 2) * 0.5f;
                    int column = Mathf.FloorToInt(gridX);
                    float localX = gridX - column - 0.5f;
                    float localY = v * 6f - row - 0.5f;
                    float direction = (column + row) % 2 == 0 ? 1f : -1f;
                    float along = (localX + localY * direction) * 0.7071068f;
                    float across = (localY - localX * direction) * 0.7071068f;
                    float ellipse = along * along / (0.34f * 0.34f) + across * across / (0.09f * 0.09f);
                    float ridge = Mathf.Exp(-ellipse * 2f);
                    float broad = Mathf.PerlinNoise(u * 3f + 11f, v * 3f + 17f);
                    int index = y * resolution + x;
                    heights[index] = ridge * 0.8f;
                    float rim = Mathf.Exp(-ellipse * 0.7f) - ridge;
                    float shade = Mathf.Clamp01(0.35f + broad * 0.05f + ridge * 0.6f - rim * 0.35f);
                    albedo[index] = Color.Lerp(new Color(0.26f, 0.28f, 0.3f), new Color(0.78f, 0.8f, 0.82f), shade);
                    mask[index] = new Color(0.98f, 0f, 0f, 0.6f + ridge * 0.08f);
                }
            }
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = heights[y * resolution + (x + 1) % resolution] - heights[y * resolution + (x + resolution - 1) % resolution];
                    float dy = heights[((y + 1) % resolution) * resolution + x] - heights[((y + resolution - 1) % resolution) * resolution + x];
                    Vector3 normal = new Vector3(-dx * 160f, -dy * 160f, 1f).normalized;
                    normals[y * resolution + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f);
                }
            }
            Texture2D baseMap = SaveTexture("AlloySurface", albedo, false, false);
            Texture2D normalMap = SaveTexture("AlloyCrushedNormal", normals, true, false);
            Texture2D metallicMap = SaveTexture("AlloyMetallicSmoothness", mask, false, true);
            string path = MATERIALS + "/KeycapIronAlloy.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/Steel.mat"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = "KeycapIronAlloy";
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_BaseMap", baseMap);
            material.SetTexture("_BumpMap", normalMap);
            material.SetTexture("_MetallicGlossMap", metallicMap);
            material.SetFloat("_Metallic", 0.98f);
            material.SetFloat("_Smoothness", 0.75f);
            material.SetFloat("_BumpScale", 0.65f);
            material.SetTextureScale("_BaseMap", new Vector2(0.55f, 0.55f));
            material.SetTextureScale("_BumpMap", new Vector2(0.55f, 0.55f));
            material.SetTextureScale("_MetallicGlossMap", new Vector2(0.55f, 0.55f));
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 공용 철합금 재질과 표면 텍스처만 다시 생성하고 미리보기를 갱신한다.
        /// 기존 키보드 및 키캡 에셋은 유지하며 새 재질 설정을 참조하는 렌더링 결과를 저장한다.
        /// </summary>
        public static void RefreshSurface()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("별도 복사 프로젝트의 배치 모드에서 실행합니다.");
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material alloy = CreateAlloyMaterial();
            AssetDatabase.SaveAssets();
            GameObject keyboard = BuildDisplay(alloy);
            RenderKeyboardPreview(keyboard);
            Debug.Log("ALLOY_SURFACE_REFRESHED");
        }

        /// <summary>
        /// name과 pixels로 철합금의 색상 또는 노멀 또는 금속도 텍스처를 저장한다.
        /// normal과 linear로 임포트 설정을 구분하고 생성된 Texture2D 에셋을 반환한다.
        /// </summary>
        private static Texture2D SaveTexture(string name, Color[] pixels, bool normal, bool linear)
        {
            Texture2D texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, true);
            texture.SetPixels(pixels);
            texture.Apply();
            string path = MATERIALS + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !linear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        /// <summary>
        /// key의 외피와 내부 벽 및 소켓에 alloy 공용 재질을 적용한다.
        /// TextMesh 렌더러는 제외하며 기존 메시와 텍스트 설정을 유지한 채 재질 오버라이드만 기록한다.
        /// </summary>
        private static void ApplyAlloy(Transform key, Material alloy)
        {
            foreach (MeshRenderer renderer in key.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponent<TextMesh>() != null) continue;
                renderer.sharedMaterial = alloy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        /// <summary>
        /// 기본 키보드의 키캡 메시 재질만 alloy로 변경한 전시 인스턴스를 구성한다.
        /// 87개 입력 및 체력 참조를 확인하고 원본 텍스트 설정을 유지한 인스턴스를 반환한다.
        /// </summary>
        private static GameObject BuildDisplay(Material alloy)
        {
            GameObject keyboard = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Prefabs/Keyboard_TKL_87.prefab"));
            PrefabUtility.UnpackPrefabInstance(keyboard, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            keyboard.name = "Keyboard_TKL_87_Alloy_Display";
            UnityEngine.Object.DestroyImmediate(keyboard.GetComponent<KeyboardPlayableVariant>());
            foreach (KeycapButton button in keyboard.GetComponentsInChildren<KeycapButton>(true))
                ApplyAlloy(button.transform, alloy);
            foreach (MeshRenderer renderer in keyboard.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "CNC_LowerCase" || renderer.name == "Brass_SwitchPlate" || renderer.name.StartsWith("TopBezel_", StringComparison.Ordinal))
                {
                    renderer.sharedMaterial = alloy;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            SerializedProperty bindings = new SerializedObject(keyboard.GetComponent<KeyboardInputController>()).FindProperty("_bindings");
            if (bindings.arraySize != 87 || keyboard.GetComponentsInChildren<KeycapHealth>(true).Length != 87)
                throw new InvalidOperationException("철합금 키보드의 입력 또는 체력 개수가 87개가 아닙니다.");
            for (int index = 0; index < bindings.arraySize; index++)
                if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                    throw new InvalidOperationException("철합금 키보드 입력 대상 누락: " + index);
            return keyboard;
        }

        /// <summary>
        /// keyboard를 Keyboard 씬의 다음 비교 전시 줄로 이동한다.
        /// 기본 키보드의 위치 및 크기를 기준으로 철합금 전시만 추가하거나 교체하고 씬을 저장한다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject keyboard)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Additive);
            GameObject original = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Keyboard_TKL_87") original = root;
                if (root.name == "Keyboard_TKL_87_Alloy_Display") UnityEngine.Object.DestroyImmediate(root);
            }
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyboard, scene);
            keyboard.transform.localScale = original.transform.lossyScale;
            keyboard.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, 72f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// sizes에 해당하는 철합금 키캡을 체크 무늬 바닥 위에 배치하여 표면 요철을 렌더링한다.
        /// 생성된 키캡과 카메라 및 조명을 사용해 PNG를 저장하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void RenderPreview(string[] sizes)
        {
            for (int index = 0; index < sizes.Length; index++)
            {
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT + "/Keycap_" + sizes[index] + "_Alloy.prefab"));
                cap.transform.localScale = Vector3.one;
                cap.transform.position = index == 7 ? new Vector3(3.1f, 0.06f, -3.5f)
                    : index == 8 ? new Vector3(7.6f, 0.06f, -2.9f)
                    : new Vector3(index < 4 ? index * 1.8f : (index - 4) * 2.9f, 0.06f, index < 4 ? 0f : -1.7f);
            }
            Material dark = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/Graphite.mat"));
            Material light = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/Porcelain_PBT.mat"));
            light.SetColor("_BaseColor", new Color(0.29f, 0.39f, 0.44f));
            for (int z = -5; z <= 1; z++)
            {
                for (int x = -1; x <= 9; x++)
                {
                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.transform.position = new Vector3(x, -0.04f, z);
                    tile.transform.localScale = new Vector3(1f, 0.1f, 1f);
                    tile.GetComponent<MeshRenderer>().sharedMaterial = (x + z) % 2 == 0 ? light : dark;
                }
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            Light keyLight = new GameObject("PreviewLight").AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.5f;
            keyLight.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Camera camera = new GameObject("AlloyPreviewCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(12f, 12f, -16f);
            camera.transform.LookAt(new Vector3(3.8f, 0.2f, -1.9f));
            camera.orthographic = true;
            camera.orthographicSize = 5.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            RenderTexture target = new RenderTexture(1600, 1000, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW + "/AlloyKeycaps.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
        /// <summary>
        /// keyboard의 철합금 외피와 본체를 기존 텍스트 설정 그대로 렌더링한다.
        /// 임시 카메라와 조명을 사용하여 전체 키보드 PNG 미리보기를 저장한다.
        /// </summary>
        private static void RenderKeyboardPreview(GameObject keyboard)
        {
            GameObject model = UnityEngine.Object.Instantiate(keyboard);
            model.transform.localScale = Vector3.one;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            Light light = new GameObject("PreviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Camera camera = new GameObject("AlloyKeyboardPreviewCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(24f, 24f, -25f);
            camera.transform.LookAt(new Vector3(9f, 0.4f, 3f));
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            RenderTexture target = new RenderTexture(1600, 1000, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes(PREVIEW + "/AlloyKeyboard.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(model);
        }

    }
}
