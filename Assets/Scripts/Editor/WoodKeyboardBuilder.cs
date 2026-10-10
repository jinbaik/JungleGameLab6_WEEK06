using System;
using System.IO;
using System.Linq;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class WoodKeyboardBuilder
    {
        private const string ROOT = "Assets/Resource/";
        private const string GAME_PATH = ROOT + "Prefabs/Keyboard_TKL_87_Wood_Game.prefab";
        private const string BACKGROUND_PATH = ROOT + "Prefabs/Keyboard_TKL_87_Wood_Background.prefab";


        /// <summary>
        /// 87키 원목 게임용과 배경용 프리팹을 만들고 키보드 씬에 추가한다.
        /// 기존 원목 키캡 및 경량 메시를 재사용하고 게임용 각인 깊이 검사 설정과 집계 및 미리보기를 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Build Wood Keyboard Pair")]
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("별도 복사 프로젝트의 숨김 배치에서 실행합니다.");
            Directory.CreateDirectory(".local/WoodKeyboardPreview");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject game = BuildGame();
            GameObject background = BuildBackground();
            AddToKeyboardScene(game, background);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderPreview(game, background);
            AssetDatabase.SaveAssets();
            Debug.Log("WOOD_KEYBOARD_PAIR_COMPLETE");
        }

        /// <summary>
        /// 기존 게임용 모델의 키캡을 원목 크기별 버리언트로 연결한다.
        /// 키 계층과 사용자 설정을 보존하며 본체 외부 재질 및 각인 색을 변경하고 새 게임용 프리팹을 반환한다.
        /// </summary>
        private static GameObject BuildGame()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keyboard_TKL_87.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Material wood = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeycapWoodWalnut.mat");
            var settings = new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                gameObjectsNotMatchedBecomesOverride = true,
                componentsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = true,
                changeRootNameToAssetName = false
            };
            int count = 0;
            foreach (KeycapButton button in instance.GetComponentsInChildren<KeycapButton>(true))
            {
                GameObject key = button.gameObject;
                MeshRenderer shell = key.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                string size = shell.GetComponent<MeshFilter>().sharedMesh.name.Substring("Keycap_".Length);
                GameObject cap = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keycaps/Wood/Keycap_" + size + "_Wood.prefab");
                if (cap == null) throw new InvalidOperationException("원목 키캡 없음: " + size);
                string keyName = key.name;
                Vector3 position = key.transform.localPosition;
                Quaternion rotation = key.transform.localRotation;
                Vector3 scale = key.transform.localScale;
                if (PrefabUtility.IsPartOfPrefabInstance(key))
                    PrefabUtility.UnpackPrefabInstance(key, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                PrefabUtility.ConvertToPrefabInstance(key, cap, settings, InteractionMode.AutomatedAction);
                key.name = keyName;
                key.transform.SetLocalPositionAndRotation(position, rotation);
                key.transform.localScale = scale;
                shell = key.transform.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                shell.sharedMaterial = wood;
                PrefabUtility.RecordPrefabInstancePropertyModifications(shell);
                foreach (TextMesh label in key.GetComponentsInChildren<TextMesh>(true))
                    label.color = new Color(0.94f, 0.89f, 0.77f);
                count++;
            }
            if (count != 87) throw new InvalidOperationException("키캡 개수 오류: " + count);
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "CNC_LowerCase" || renderer.name.StartsWith("TopBezel_", StringComparison.Ordinal))
                    renderer.sharedMaterial = wood;
            var controller = new SerializedObject(instance.GetComponent<KeyboardInputController>());
            SerializedProperty bindings = controller.FindProperty("_bindings");
            if (bindings.arraySize != 87) throw new InvalidOperationException("바인딩 개수 오류");
            for (int index = 0; index < bindings.arraySize; index++)
                if (bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue == null)
                    throw new InvalidOperationException("누락된 바인딩: " + index);
            instance.name = "Keyboard_TKL_87_Wood_Game";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, GAME_PATH);
            File.WriteAllText(".local/WoodKeyboardPreview/GameReport.txt", Report(instance) + "\nWood variant keycaps: " + count);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>
        /// 기존 배경용의 경량 메시를 유지한 원목 재질 버리언트를 생성한다.
        /// 공유 메시와 원목 재질을 사용하며 입력 및 물리 컴포넌트를 제거하고 배경용 프리팹을 반환한다.
        /// </summary>
        private static GameObject BuildBackground()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "Prefabs/Keyboard_TKL_87_Background.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            Material wood = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeycapWoodWalnut.mat");
            Material oldInk = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeyboardBackgroundLegend.mat");
            Material ink = new Material(Shader.Find("Keyboard/LegendDepth")) { name = "KeyboardWoodBackgroundLegend" };
            ink.mainTexture = oldInk.mainTexture;
            ink.SetFloat("_UseUniformColor", 1f);
            ink.SetColor("_InkColor", new Color(0.94f, 0.89f, 0.77f));
            ink = SaveMaterial(ink, ROOT + "Materials/KeyboardWoodBackgroundLegend.mat");
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                string material = renderer.sharedMaterial.name;
                if (material.EndsWith("_PBT", StringComparison.Ordinal) || material == "AnodizedAluminum")
                    renderer.sharedMaterial = wood;
                else if (material == "KeyboardBackgroundLegend")
                    renderer.sharedMaterial = ink;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
            instance.name = "Keyboard_TKL_87_Wood_Background";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, BACKGROUND_PATH);
            File.WriteAllText(".local/WoodKeyboardPreview/BackgroundReport.txt", Report(instance));
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        /// <summary>
        /// material을 path에 저장하고 기존 에셋이 있으면 GUID를 유지하여 갱신한다.
        /// 저장된 공유 재질을 반환한다.
        /// </summary>
        private static Material SaveMaterial(Material material, string path)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) { AssetDatabase.CreateAsset(material, path); return material; }
            EditorUtility.CopySerialized(material, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(material);
            return existing;
        }

        /// <summary>
        /// 키보드 씬의 기존 모델을 기준으로 게임용과 배경용 원목 모델을 추가한다.
        /// 두 프리팹을 기존 모델 뒤와 옆에 배치하고 다른 씬이나 기존 카메라 설정은 변경하지 않는다.
        /// </summary>
        private static void AddToKeyboardScene(GameObject game, GameObject background)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Keyboard.unity", OpenSceneMode.Single);
            GameObject original = scene.GetRootGameObjects().First(root => root.GetComponent<KeyboardInputController>() != null);
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Keyboard_TKL_87_Wood_Game" || root.name == "Keyboard_TKL_87_Wood_Background")
                    UnityEngine.Object.DestroyImmediate(root);
            GameObject gameInstance = (GameObject)PrefabUtility.InstantiatePrefab(game, scene);
            gameInstance.transform.localScale = original.transform.lossyScale;
            gameInstance.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(0f, 0f, 9f)), original.transform.rotation);
            GameObject backgroundInstance = (GameObject)PrefabUtility.InstantiatePrefab(background, scene);
            backgroundInstance.transform.localScale = original.transform.lossyScale;
            backgroundInstance.transform.SetPositionAndRotation(original.transform.TransformPoint(new Vector3(22f, 0f, 9f)), original.transform.rotation);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// model의 활성 렌더러와 메시 및 그림자 수치를 집계한다.
        /// 전체 배치 버텍스, 삼각형, 렌더러와 그림자 투사 수를 문자열로 반환한다.
        /// </summary>
        private static string Report(GameObject model)
        {
            int vertices = 0, triangles = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            return "Vertices: " + vertices + "\nTriangles: " + triangles + "\nActive renderers: " + renderers.Length +
                "\nShadow casters: " + renderers.Count(renderer => renderer.shadowCastingMode != ShadowCastingMode.Off) +
                "\nRuntime scripts: " + model.GetComponentsInChildren<MonoBehaviour>(true).Length +
                "\nColliders: " + model.GetComponentsInChildren<Collider>(true).Length;
        }

        /// <summary>
        /// 두 원목 프리팹을 임시 씬에서 렌더링하고 글씨 차폐 구도를 저장한다.
        /// game과 background의 실제 메시 및 각인 재질을 사용하여 개인 미리보기 이미지를 생성한다.
        /// </summary>
        private static void RenderPreview(GameObject game, GameObject background)
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(game);
            TextMesh[] labels = model.GetComponentsInChildren<TextMesh>(true);
            Font font = labels[0].font;
            font.RequestCharactersInTexture(string.Concat(labels.Select(label => label.text)), 96);
            Material previewInk = new Material(AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/KeyboardPlayableLegend.mat"));
            previewInk.mainTexture = font.material.mainTexture;
            foreach (TextMesh label in labels)
                label.GetComponent<MeshRenderer>().sharedMaterial = previewInk;
            model.transform.localScale = Vector3.one * 0.15f;
            GameObject backdrop = (GameObject)PrefabUtility.InstantiatePrefab(background);
            backdrop.transform.localScale = model.transform.localScale;
            backdrop.transform.position = new Vector3(0f, 0f, 1.7f);
            Camera camera = new GameObject("WoodPreview").AddComponent<Camera>();
            camera.transform.position = new Vector3(4f, 4.5f, -4.5f);
            camera.transform.LookAt(new Vector3(1.4f, 0.2f, 1.15f));
            camera.orthographic = true;
            camera.orthographicSize = 2.15f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.08f);
            RenderSettings.ambientLight = Color.gray;
            Light light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            SaveImage(camera, "WoodPair.png");
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0.6f, 0.65f, 0.3f);
            wall.transform.localScale = new Vector3(1.4f, 1.7f, 1.3f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "Materials/Graphite.mat");
            SaveImage(camera, "WoodOcclusion.png");
        }

        /// <summary>
        /// camera 구도를 렌더 텍스처에 그려 name 이미지로 저장한다.
        /// 개인 미리보기 폴더에 PNG를 생성하고 임시 렌더 리소스를 해제한다.
        /// </summary>
        private static void SaveImage(Camera camera, string name)
        {
            RenderTexture target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(".local/WoodKeyboardPreview/" + name, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}

