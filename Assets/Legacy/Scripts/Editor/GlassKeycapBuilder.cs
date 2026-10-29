using System;
using System.IO;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class GlassKeycapBuilder
    {
        private const string ROOT = "Assets/Resource";
        private const string CAPS = ROOT + "/Prefabs/Keycaps";
        private const string OUTPUT = CAPS + "/Glass";
        private const string MATERIALS = ROOT + "/Materials/Glass";
        private const string PREVIEW = ".local/GlassKeycapPreview";

        /// <summary>
        /// 기존 아홉 크기의 키캡에 투명 유리 재질을 적용한 버리언트를 생성한다.
        /// 원본 텍스트와 입력 및 파괴 구조를 유지하며 적용 키보드를 별도 프리팹 없이 Keyboard 씬에 배치한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Glass Keycaps")]
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
            Material glass = CreateGlassMaterial();
            string[] sizes = { "1u", "1_25u", "1_5u", "1_75u", "2u", "2_25u", "2_75u", "6_25u", "1u_Vertical2u" };
            foreach (string size in sizes)
            {
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CAPS + "/Keycap_" + size + ".prefab"));
                try
                {
                    cap.name = "Keycap_" + size + "_Glass";
                    ApplyGlass(cap.transform, glass);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(cap, OUTPUT + "/" + cap.name + ".prefab");
                    if (PrefabUtility.GetPrefabAssetType(saved) != PrefabAssetType.Variant)
                        throw new InvalidOperationException("유리 키캡 버리언트 저장 실패: " + size);
                }
                finally { UnityEngine.Object.DestroyImmediate(cap); }
            }
            AssetDatabase.SaveAssets();
            RenderPreview(sizes);
            GameObject keyboard = BuildDisplay(glass);
            AddToKeyboardScene(keyboard);
            File.WriteAllText(PREVIEW + "/BuildReport.txt", "Keycap variants: 9\nShared glass materials: 1\nNew meshes: 0\nText settings: inherited unchanged\nInput bindings: 87\nHealth components: 87\nKeyboard prefab assets created: 0\n");
            Debug.Log("GLASS_KEYCAPS_COMPLETE variants=9 keyboardPrefabs=0");
        }

        /// <summary>
        /// 전용 유리 셰이더를 사용해 옅은 청록빛과 투명도 및 가장자리 반사를 가진 공용 재질을 만든다.
        /// 색상과 불투명도 및 매끄러움을 Material 에셋에 저장하고 반환한다.
        /// </summary>
        private static Material CreateGlassMaterial()
        {
            string path = MATERIALS + "/KeycapGlassClear.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Keyboard/KeycapGlass"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = "KeycapGlassClear";
            material.SetColor("_Tint", new Color(0.65f, 0.88f, 0.95f));
            material.SetFloat("_Opacity", 0.12f);
            material.SetFloat("_EdgeOpacity", 0.7f);
            material.SetFloat("_Smoothness", 0.92f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// key의 외피와 내부 벽 및 소켓에 glass 공용 재질을 적용한다.
        /// TextMesh 렌더러는 제외하며 기존 메시와 텍스트 설정을 유지한 채 재질 오버라이드만 기록한다.
        /// </summary>
        private static void ApplyGlass(Transform key, Material glass)
        {
            foreach (MeshRenderer renderer in key.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponent<TextMesh>() != null) continue;
                renderer.sharedMaterial = glass;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        /// <summary>
        /// 기본 키보드의 키캡 메시 재질만 glass로 변경한 전시 인스턴스를 구성한다.
        /// 87개 입력 및 체력 참조를 확인하고 원본 텍스트 설정을 유지한 인스턴스를 반환한다.
        /// </summary>
        private static GameObject BuildDisplay(Material glass)
        {
            GameObject keyboard = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Prefabs/Keyboard_TKL_87.prefab"));
            PrefabUtility.UnpackPrefabInstance(keyboard, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            keyboard.name = "Keyboard_TKL_87_Glass_Display";
            UnityEngine.Object.DestroyImmediate(keyboard.GetComponent<KeyboardPlayableVariant>());
            foreach (KeycapButton button in keyboard.GetComponentsInChildren<KeycapButton>(true))
                ApplyGlass(button.transform, glass);
            SerializedProperty bindings = new SerializedObject(keyboard.GetComponent<KeyboardInputController>()).FindProperty("_bindings");
            if (bindings.arraySize != 87 || keyboard.GetComponentsInChildren<KeycapHealth>(true).Length != 87)
                throw new InvalidOperationException("유리 키보드의 입력 또는 체력 개수가 87개가 아닙니다.");
            for (int index = 0; index < bindings.arraySize; index++)
                if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                    throw new InvalidOperationException("유리 키보드 입력 대상 누락: " + index);
            return keyboard;
        }

        /// <summary>
        /// keyboard를 Keyboard 씬의 다음 비교 전시 줄로 이동한다.
        /// 기본 키보드의 위치 및 크기를 기준으로 유리 전시만 추가하거나 교체하고 씬을 저장한다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject keyboard)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Keyboard.unity", OpenSceneMode.Additive);
            GameObject original = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Keyboard_TKL_87") original = root;
                if (root.name == "Keyboard_TKL_87_Glass_Display") UnityEngine.Object.DestroyImmediate(root);
            }
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keyboard, scene);
            keyboard.transform.localScale = original.transform.lossyScale;
            keyboard.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, 63f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// sizes에 해당하는 유리 키캡을 체크 무늬 바닥 위에 배치하여 투명도를 렌더링한다.
        /// 생성된 키캡과 카메라 및 조명을 사용해 PNG를 저장하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void RenderPreview(string[] sizes)
        {
            for (int index = 0; index < sizes.Length; index++)
            {
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT + "/Keycap_" + sizes[index] + "_Glass.prefab"));
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
            Camera camera = new GameObject("GlassPreviewCamera").AddComponent<Camera>();
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
            File.WriteAllBytes(PREVIEW + "/GlassKeycaps.png", image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
