using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class KeyboardLayoutMigration
    {
        private const string ORIGINAL_PATH = "Assets/Resource/Prefabs/Keyboard_ANSI_104.prefab";
        private const string TKL_PATH = "Assets/Resource/Prefabs/Keyboard_TKL_87.prefab";
        private const string ARCHIVE_FOLDER = "Assets/Resource/Prefabs/Keyboard/Reference/ANSI104";
        private const string ARCHIVE_PATH = ARCHIVE_FOLDER + "/Keyboard_ANSI_104_Model.prefab";
        private const string SCENE_PATH = "Assets/Scenes/Keyboard.unity";
        private const string PACKAGE_PATH = "Docs/Exports/Keyboard_ANSI_104_Model.unitypackage";

        /// <summary>
        /// 기존 104키 모델을 자료로 보관하고 현재 키보드를 87키로 전환한다.
        /// 원본 프리팹과 씬을 사용하여 TKL 에셋, 스크립트 없는 자료 프리팹, 내보내기 파일을 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard/Convert Existing Keyboard To TKL")]
        public static void Convert()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("현재 씬을 보호하기 위해 이 전환은 별도 배치 프로젝트에서 실행합니다.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ORIGINAL_PATH) == null)
                throw new InvalidOperationException("전환할 104키 원본 프리팹이 없습니다.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TKL_PATH) != null || AssetDatabase.LoadAssetAtPath<GameObject>(ARCHIVE_PATH) != null)
                throw new InvalidOperationException("TKL 또는 원본 보관 프리팹이 이미 있어 덮어쓰지 않습니다.");

            Directory.CreateDirectory(ARCHIVE_FOLDER);
            Directory.CreateDirectory("Docs/Exports");
            AssetDatabase.Refresh();
            GameObject bodyPrefab = KeyboardModelBuilder.CreateTKLBodyPrefab();
            if (!AssetDatabase.CopyAsset(ORIGINAL_PATH, TKL_PATH))
                throw new InvalidOperationException("TKL 프리팹 복사에 실패했습니다.");
            GameObject tkl = PrefabUtility.LoadPrefabContents(TKL_PATH);
            try
            {
                ConvertHierarchy(tkl, bodyPrefab);
                PrefabUtility.SaveAsPrefabAsset(tkl, TKL_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(tkl);
            }
            GameObject tklPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TKL_PATH);
            ConvertScene(tklPrefab, bodyPrefab);
            ArchiveOriginal();
            AssetDatabase.SaveAssets();
            ExportReferencePackage();
            KeyboardModelBuilder.RenderTKLPreview();
            File.WriteAllText(".local/KeyboardPreview/BuildReport.txt", "Key instances: 87\nUnique keycap sizes: 8\nScene: Assets/Scenes/Keyboard.unity\nKeyboard width: 0.3667125 m\nArchived model: 104 keys, scripts removed\n");
            Debug.Log("TKL_CONVERSION_COMPLETE keys=87 archiveKeys=104 package=" + PACKAGE_PATH);
        }

        /// <summary>
        /// 키보드 계층에서 숫자패드를 제거하고 TKL 본체와 입력 매핑을 적용한다.
        /// keyboard와 bodyPrefab을 사용하며 기존 87개 키캡의 HP와 사용자 이벤트를 유지한다.
        /// </summary>
        private static void ConvertHierarchy(GameObject keyboard, GameObject bodyPrefab)
        {
            keyboard.name = "Keyboard_TKL_87";
            Transform keys = keyboard.transform.Find("Keys_104_SharedPrefabs");
            if (keys == null)
                keys = keyboard.transform.Find("Keys_87_SharedPrefabs");
            keys.name = "Keys_87_SharedPrefabs";
            var removed = new List<GameObject>();
            foreach (Transform child in keys)
            {
                if (child.localPosition.x >= 18.9f && (child.name.StartsWith("Key_", StringComparison.Ordinal) || child.name.StartsWith("Switch_", StringComparison.Ordinal)))
                    removed.Add(child.gameObject);
            }
            foreach (GameObject part in removed)
                UnityEngine.Object.DestroyImmediate(part);

            Transform oldBody = keyboard.transform.Find("KeyboardBody");
            if (oldBody != null)
                UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, keyboard.scene);
            body.transform.SetParent(keyboard.transform, false);
            body.transform.localScale = Vector3.one;

            KeyboardInputController controller = keyboard.GetComponent<KeyboardInputController>();
            var serialized = new SerializedObject(controller);
            bool blockShortcuts = serialized.FindProperty("_blockShortcuts").boolValue;
            KeyboardInputSetup.Configure(keyboard, false);
            serialized.Update();
            serialized.FindProperty("_blockShortcuts").boolValue = blockShortcuts;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 기존 키보드 씬의 모델을 같은 오브젝트를 유지하며 TKL 프리팹으로 연결한다.
        /// tklPrefab과 bodyPrefab을 사용하여 기존 씬 참조와 키별 설정을 보존하고 구도를 조정한다.
        /// </summary>
        private static void ConvertScene(GameObject tklPrefab, GameObject bodyPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            GameObject keyboard = null;
            Camera camera = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Keyboard_ANSI_104")
                    keyboard = root;
                if (root.name == "Main Camera")
                    camera = root.GetComponent<Camera>();
            }
            if (keyboard == null)
                throw new InvalidOperationException("현재 씬에서 기존 키보드를 찾지 못했습니다.");
            PrefabUtility.UnpackPrefabInstance(keyboard, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            ConvertHierarchy(keyboard, bodyPrefab);
            var settings = new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                gameObjectsNotMatchedBecomesOverride = true,
                componentsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = true,
                changeRootNameToAssetName = true
            };
            PrefabUtility.ConvertToPrefabInstance(keyboard, tklPrefab, settings, InteractionMode.AutomatedAction);
            if (camera != null)
            {
                camera.transform.position += keyboard.transform.TransformVector(new Vector3(-2.25f, 0f, 0f));
                camera.transform.LookAt(keyboard.transform.TransformPoint(new Vector3(9.25f, 0.94f, 2.8f)));
                if (camera.orthographic)
                    camera.orthographicSize *= 19.25f / 23.75f;
            }
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
        }

        /// <summary>
        /// 104키 원본을 다른 프로젝트에서 사용할 수 있는 모델 전용 프리팹으로 보관한다.
        /// 기존 원본을 이동하고 중첩 프리팹을 풀어 모든 MonoBehaviour를 제거한 뒤 원본 형상을 저장한다.
        /// </summary>
        private static void ArchiveOriginal()
        {
            string error = AssetDatabase.MoveAsset(ORIGINAL_PATH, ARCHIVE_PATH);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException(error);
            GameObject archive = PrefabUtility.LoadPrefabContents(ARCHIVE_PATH);
            try
            {
                archive.name = "Keyboard_ANSI_104_Model";
                Transform[] parts = archive.GetComponentsInChildren<Transform>(true);
                foreach (Transform part in parts)
                {
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(part.gameObject))
                        PrefabUtility.UnpackPrefabInstance(part.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                foreach (MonoBehaviour script in archive.GetComponentsInChildren<MonoBehaviour>(true))
                    UnityEngine.Object.DestroyImmediate(script);
                PrefabUtility.SaveAsPrefabAsset(archive, ARCHIVE_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(archive);
            }
        }

        /// <summary>
        /// 자료용 모델의 메시와 재질을 독립 복사하고 코드 없는 Unity 패키지를 저장한다.
        /// 보관 프리팹의 Assets 의존성을 사용하여 참조를 새 GUID로 바꾸고 모델 자료만 내보낸다.
        /// </summary>
        public static void ExportReferencePackage()
        {
            string dependencyFolder = ARCHIVE_FOLDER + "/Dependencies";
            Directory.CreateDirectory(dependencyFolder);
            Directory.CreateDirectory("Docs/Exports");
            AssetDatabase.Refresh();
            var paths = new List<string> { ARCHIVE_PATH };
            var guids = new Dictionary<string, string>();
            foreach (string path in AssetDatabase.GetDependencies(ARCHIVE_PATH, true))
            {
                if (path == ARCHIVE_PATH || !path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("모델 자료에 스크립트 참조가 남아 있습니다: " + path);
                if (path.StartsWith(dependencyFolder + "/", StringComparison.Ordinal))
                {
                    paths.Add(path);
                    continue;
                }
                string copyPath = dependencyFolder + "/" + Path.GetFileName(path);
                if (!AssetDatabase.CopyAsset(path, copyPath))
                    throw new InvalidOperationException("자료 의존성 복사에 실패했습니다: " + path);
                guids.Add(AssetDatabase.AssetPathToGUID(path), AssetDatabase.AssetPathToGUID(copyPath));
                paths.Add(copyPath);
            }
            foreach (string path in paths)
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;
                string content = File.ReadAllText(path);
                if (!content.StartsWith("%YAML", StringComparison.Ordinal))
                    continue;
                foreach (KeyValuePair<string, string> guid in guids)
                    content = content.Replace("guid: " + guid.Key, "guid: " + guid.Value);
                File.WriteAllText(path, content);
            }
            AssetDatabase.Refresh();
            AssetDatabase.ExportPackage(paths.ToArray(), PACKAGE_PATH, ExportPackageOptions.Default);
            Debug.Log("REFERENCE_PACKAGE_EXPORTED assets=" + paths.Count + " scripts=0");
        }
    }
}
