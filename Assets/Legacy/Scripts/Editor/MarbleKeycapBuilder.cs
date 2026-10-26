using System;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class MarbleKeycapBuilder
    {
        private const string ROOT = "Assets/Resource";
        private const string CAPS = ROOT + "/Prefabs/Keycaps";
        private const string OUTPUT = CAPS + "/Marble";
        private const string MATERIALS = ROOT + "/Materials/Marble";
        private const string PREVIEW = ".local/MarbleKeycapPreview";

        /// <summary>
        /// 기존 크기별 키캡에 공용 흰 대리석 재질을 적용하여 아홉 개 버리언트를 생성한다.
        /// 원본 입력과 파괴 상태를 상속하며 적용 키보드는 별도 프리팹 없이 Keyboard 씬에 배치한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Marble Keycaps")]
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
            Material marble = CreateMarbleMaterial();
            string[] sizes = { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u", "1u_Vertical2u" };
            foreach (string size in sizes)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(CAPS + "/Keycap_" + size + ".prefab");
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    cap.name = "Keycap_" + size + "_Marble";
                    ApplyShell(cap.transform, marble);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(cap, OUTPUT + "/" + cap.name + ".prefab");
                    if (PrefabUtility.GetPrefabAssetType(saved) != PrefabAssetType.Variant)
                        throw new InvalidOperationException("대리석 키캡 버리언트 저장 실패: " + size);
                }
                finally { UnityEngine.Object.DestroyImmediate(cap); }
            }
            AssetDatabase.SaveAssets();
            GameObject keyboard = BuildDisplay(marble);
            RenderPreview(keyboard);
            AddToKeyboardScene(keyboard);
            File.WriteAllText(PREVIEW + "/BuildReport.txt", "Keycap variants: 9\nShared shell materials: 1\nNew meshes: 0\nInput bindings: 87\nHealth components: 87\nKeyboard prefab assets created: 0\n");
            Debug.Log("MARBLE_KEYCAPS_COMPLETE variants=9 keyboardPrefabs=0");
        }

        /// <summary>
        /// 원본 PBT 셰이더에 흰 바탕과 회색 광맥 텍스처를 적용해 유광 대리석 재질을 만든다.
        /// 기존 미세 노멀과 생성한 색상 텍스처를 사용하여 공용 Material 에셋을 저장하고 반환한다.
        /// </summary>
        private static Material CreateMarbleMaterial()
        {
            const int resolution = 1024;
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, true);
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = y / (float)resolution;
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)resolution;
                    float angleU = u * Mathf.PI * 2f;
                    float angleV = v * Mathf.PI * 2f;
                    float cloud = Mathf.PerlinNoise(8f + Mathf.Cos(angleU) * 1.5f + Mathf.Cos(angleV), 11f + Mathf.Sin(angleU) * 1.5f + Mathf.Sin(angleV));
                    float detail = Mathf.PerlinNoise(17f + Mathf.Cos(angleU) * 5f + Mathf.Cos(angleV) * 4f, 23f + Mathf.Sin(angleU) * 5f + Mathf.Sin(angleV) * 4f);
                    float flow = u * 2f + v + cloud * 0.9f + detail * 0.12f + Mathf.Sin(angleV) * 0.18f;
                    float distance = Mathf.Abs(Mathf.Sin(flow * Mathf.PI * 2f));
                    float broadVein = Mathf.Exp(-Mathf.Pow(distance / 0.25f, 2f));
                    float fineVein = Mathf.Exp(-Mathf.Pow(distance / (0.038f + detail * 0.05f), 2f));
                    float branchDistance = Mathf.Abs(Mathf.Sin((u * 3f - v * 2f + cloud * 1.2f) * Mathf.PI * 2f));
                    float branch = Mathf.Exp(-Mathf.Pow(branchDistance / 0.07f, 2f)) * broadVein;
                    Color baseColor = Color.Lerp(new Color(0.86f, 0.88f, 0.90f), new Color(0.98f, 0.975f, 0.96f), cloud);
                    Color pixel = Color.Lerp(baseColor, new Color(0.59f, 0.62f, 0.65f), broadVein * 0.55f);
                    pixel = Color.Lerp(pixel, new Color(0.24f, 0.28f, 0.32f), Mathf.Clamp01(fineVein * 0.85f + branch * 0.35f));
                    pixels[y * resolution + x] = pixel;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            string texturePath = MATERIALS + "/MarbleWhiteVeins.png";
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            string path = MATERIALS + "/KeycapMarbleWhite.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/Porcelain_PBT.mat"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = "KeycapMarbleWhite";
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetTextureScale("_BaseMap", new Vector2(0.75f, 0.75f));
            material.SetFloat("_Smoothness", 0.68f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_BumpScale", 0.025f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// key의 외피 렌더러에 marble 공용 재질을 적용한다.
        /// 원본 메시와 내부 구조는 유지하며 외피 재질 오버라이드만 기록한다.
        /// </summary>
        private static void ApplyShell(Transform key, Material marble)
        {
            MeshRenderer renderer = key.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
            renderer.sharedMaterial = marble;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        /// <summary>
        /// 기본 키보드의 텍스트 설정을 유지하며 marble 외피 재질만 적용한 전시 인스턴스를 구성한다.
        /// 87개 키의 입력 및 체력 참조를 검사하며 별도 키보드 프리팹을 저장하지 않고 반환한다.
        /// </summary>
        private static GameObject BuildDisplay(Material marble)
        {
            GameObject keyboard = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Prefabs/Keyboard_TKL_87.prefab"));
            PrefabUtility.UnpackPrefabInstance(keyboard, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            keyboard.name = "Keyboard_TKL_87_Marble_Display";
            UnityEngine.Object.DestroyImmediate(keyboard.GetComponent<KeyboardPlayableVariant>());
            foreach (KeycapButton button in keyboard.GetComponentsInChildren<KeycapButton>(true))
                ApplyShell(button.transform, marble);
            SerializedProperty bindings = new SerializedObject(keyboard.GetComponent<KeyboardInputController>()).FindProperty("_bindings");
            if (bindings.arraySize != 87 || keyboard.GetComponentsInChildren<KeycapHealth>(true).Length != 87)
                throw new InvalidOperationException("대리석 키보드의 입력 또는 체력 개수가 87개가 아닙니다.");
            for (int index = 0; index < bindings.arraySize; index++)
                if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                    throw new InvalidOperationException("대리석 키보드 입력 대상 누락: " + index);
            AssetDatabase.SaveAssets();
            return keyboard;
        }

        /// <summary>
        /// keyboard를 Keyboard 씬의 다음 비교 전시 줄에 배치한다.
        /// 기본 키보드의 위치와 크기를 기준으로 대리석 전시만 추가하거나 교체하고 씬을 저장한다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject keyboard)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Additive);
            GameObject original = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Keyboard_TKL_87") original = root;
                if (root.name == "Keyboard_TKL_87_Marble_Display") UnityEngine.Object.DestroyImmediate(root);
            }
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyboard, scene);
            keyboard.transform.localScale = original.transform.lossyScale;
            keyboard.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, 54f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// keyboard의 실제 대리석 재질과 각인을 임시 인스턴스로 렌더링한다.
        /// 카메라와 조명을 사용해 PNG를 저장하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void RenderPreview(GameObject keyboard)
        {
            GameObject model = UnityEngine.Object.Instantiate(keyboard);
            model.transform.localScale = Vector3.one;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            Light light = new GameObject("PreviewLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Camera camera = new GameObject("MarblePreviewCamera").AddComponent<Camera>();
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
            File.WriteAllBytes(PREVIEW + "/MarbleKeyboard.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(model);
        }

        /// <summary>
        /// 기존 대리석 전시의 변경된 글자 색상과 각인 재질을 기본 키보드의 설정으로 복원한다.
        /// 키의 가로 위치와 텍스트 계층 경로로 원본을 찾아 외피 및 입력 연결은 유지한 채 씬을 저장한다.
        /// </summary>
        public static void RestoreOriginalText()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("별도 복사 프로젝트의 배치 모드에서 실행합니다.");
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Single);
            GameObject keyboard = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Keyboard_TKL_87_Marble_Display") keyboard = root;
            if (keyboard == null) throw new InvalidOperationException("대리석 전시 키보드가 없습니다.");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Prefabs/Keyboard_TKL_87.prefab");
            KeycapButton[] sourceKeys = source.GetComponentsInChildren<KeycapButton>(true);
            int restored = 0;
            foreach (KeycapButton button in keyboard.GetComponentsInChildren<KeycapButton>(true))
            {
                Transform original = null;
                foreach (KeycapButton sourceButton in sourceKeys)
                {
                    Vector3 offset = sourceButton.transform.localPosition - button.transform.localPosition;
                    if (Mathf.Abs(offset.x) < 0.001f && Mathf.Abs(offset.z) < 0.001f)
                    {
                        original = sourceButton.transform;
                        break;
                    }
                }
                if (original == null) throw new InvalidOperationException("원본 키를 찾지 못했습니다: " + button.name);
                foreach (TextMesh label in button.GetComponentsInChildren<TextMesh>(true))
                {
                    string path = AnimationUtility.CalculateTransformPath(label.transform, button.transform);
                    TextMesh originalLabel = original.Find(path).GetComponent<TextMesh>();
                    label.color = originalLabel.color;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                    MeshRenderer renderer = label.GetComponent<MeshRenderer>();
                    renderer.sharedMaterials = originalLabel.GetComponent<MeshRenderer>().sharedMaterials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    restored++;
                }
            }
            foreach (TextMesh label in keyboard.GetComponentsInChildren<TextMesh>(true))
            {
                if (label.GetComponentInParent<KeycapButton>() != null) continue;
                string path = AnimationUtility.CalculateTransformPath(label.transform, keyboard.transform);
                TextMesh originalLabel = source.transform.Find(path).GetComponent<TextMesh>();
                label.color = originalLabel.color;
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                MeshRenderer renderer = label.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = originalLabel.GetComponent<MeshRenderer>().sharedMaterials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                restored++;
            }
            SerializedObject originalLegends = new SerializedObject(source.GetComponent<KeyboardLegendRenderer>());
            SerializedObject legends = new SerializedObject(keyboard.GetComponent<KeyboardLegendRenderer>());
            legends.FindProperty("_materialTemplate").objectReferenceValue = originalLegends.FindProperty("_materialTemplate").objectReferenceValue;
            legends.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("MARBLE_ORIGINAL_TEXT_RESTORED labels=" + restored);
        }
    }
}
