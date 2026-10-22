using System;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.UI;

namespace KeyboardModeling.Editor
{
    public static class EnemyTypingSetup
    {
        private const string ARM_PREFAB_PATH = "Assets/Resource/Prefabs/KeyboardTypingArm.prefab";
        private const string ENEMY_PREFAB_PATH = "Assets/Resource/Prefabs/EnemyTyping.prefab";
        private const string ENCOUNTER_PREFAB_PATH = "Assets/Resource/Prefabs/EnemyTypingEncounter.prefab";
        private const string PREVIEW_SCENE_PATH = "Assets/Legacy/Scene/EnemyTypingPreview.unity";
        private const string KEYBOARD_PREFAB_PATH = "Assets/Resource/Prefabs/Keyboard_TKL_87.prefab";
        private const float KEY_PITCH = 0.01905f;
        private const float ARM_REAR_OFFSET = 0.1196f;
        private const float ARM_READY_YAW = 270f;
        private const float ELBOW_BACK_OFFSET = 0.005f;

        /// <summary>
        /// 기존 기계팔과 키보드 에셋으로 적 프리팹과 확인용 씬을 생성한다.
        /// 지정된 에셋 경로를 사용하고 현재 열린 씬을 보존하며 기존 결과물은 덮어쓰지 않는다.
        /// </summary>
        [MenuItem("Tools/Keyboard Arm/Create Enemy Typing Setup")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating enemy assets.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ENEMY_PREFAB_PATH) != null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(ENCOUNTER_PREFAB_PATH) != null ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(PREVIEW_SCENE_PATH) != null)
                throw new InvalidOperationException("Existing enemy assets are preserved. Configure them in the Inspector.");

            GameObject keyboardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KEYBOARD_PREFAB_PATH);
            Scene authoringScene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject enemyPrefab = CreateEnemyPrefab(authoringScene, keyboardPrefab);
                CreateEncounterPrefab(authoringScene, keyboardPrefab, enemyPrefab);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(authoringScene);
            }
            CreatePreviewScene();
            AssetDatabase.SaveAssets();
            Debug.Log("ENEMY_TYPING_SETUP_COMPLETE");
        }

        /// <summary>
        /// 기존 적 라운드 프리팹과 확인용 씬에 키보드 스코어와 콤보를 연결한다.
        /// 지정된 에셋을 갱신하고 기존 HUD가 없으면 공통 HUD를 생성하며 현재 열린 씬을 보존한다.
        /// </summary>
        [MenuItem("Tools/Keyboard Arm/Connect Enemy Score Combo")]
        public static void ConfigureScoreCombo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before connecting enemy scores.");
            Scene previewScene = SceneManager.GetSceneByPath(PREVIEW_SCENE_PATH);
            if (previewScene.IsValid() && previewScene.isDirty)
                throw new InvalidOperationException("Save preview scene changes before connecting enemy scores.");

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(ENCOUNTER_PREFAB_PATH);
            try
            {
                ConfigureScoreBinding(prefabRoot, prefabRoot.GetComponent<EnemyKeyboardProvider>(),
                    prefabRoot.GetComponent<EnemyRoundController>());
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, ENCOUNTER_PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            Scene previousScene = SceneManager.GetActiveScene();
            bool openedPreview = !previewScene.IsValid() || !previewScene.isLoaded;
            if (openedPreview)
                previewScene = EditorSceneManager.OpenScene(PREVIEW_SCENE_PATH, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(previewScene);
                GameObject encounter = Array.Find(previewScene.GetRootGameObjects(),
                    root => root.GetComponent<EnemyRoundController>() != null);
                if (encounter.GetComponent<KeyboardScoreComboHud>() == null)
                    KeyboardScoreComboSceneSetup.CreateHud(encounter.GetComponent<KeyboardScoreCombo>());
                EditorSceneManager.SaveScene(previewScene);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                SceneManager.SetActiveScene(previousScene);
                if (openedPreview)
                    EditorSceneManager.CloseScene(previewScene, true);
            }
        }

        /// <summary>
        /// scene과 keyboardPrefab의 배율로 적의 공격 프리팹을 생성한다.
        /// 원본 팔을 중첩 프리팹으로 유지하고 적 컨트롤러 참조와 일시정지 시간 기준을 저장한다.
        /// </summary>
        private static GameObject CreateEnemyPrefab(Scene scene, GameObject keyboardPrefab)
        {
            GameObject root = new GameObject("EnemyTyping");
            SceneManager.MoveGameObjectToScene(root, scene);
            GameObject arm = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(ARM_PREFAB_PATH), scene);
            arm.transform.SetParent(root.transform, false);
            float scale = keyboardPrefab.transform.localScale.x / KEY_PITCH;
            arm.transform.localScale = Vector3.one * scale;
            ConfigureArmPose(arm);
            KeyboardTypingArmController motion = arm.GetComponent<KeyboardTypingArmController>();
            SerializedObject motionSettings = new SerializedObject(motion);
            motionSettings.FindProperty("_useUnscaledTime").boolValue = false;
            motionSettings.ApplyModifiedPropertiesWithoutUndo();
            EnemyTypingController enemy = root.AddComponent<EnemyTypingController>();
            SetReference(enemy, "_arm", motion);
            return PrefabUtility.SaveAsPrefabAsset(root, ENEMY_PREFAB_PATH);
        }

        /// <summary>
        /// scene, keyboardPrefab과 enemyPrefab으로 라운드 프리팹을 생성한다.
        /// 키보드 배치 기준과 공급·라운드 참조를 연결하고 재사용 가능한 프리팹을 반환한다.
        /// </summary>
        private static GameObject CreateEncounterPrefab(Scene scene, GameObject keyboardPrefab, GameObject enemyPrefab)
        {
            GameObject root = new GameObject("EnemyTypingEncounter");
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform placement = new GameObject("KeyboardPlacement").transform;
            placement.SetParent(root.transform, false);
            placement.localPosition = -Vector3.Scale(new Vector3(9.25f, 0f, 2.8f), keyboardPrefab.transform.localScale);
            GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, scene);
            enemy.transform.SetParent(root.transform, false);
            EnemyKeyboardProvider provider = root.AddComponent<EnemyKeyboardProvider>();
            SetReference(provider, "_keyboardPrefab", keyboardPrefab);
            SetReference(provider, "_fragmentsPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resource/Prefabs/KeyboardFragment/Keyboard_TKL_87_Fragments.prefab"));
            SetReference(provider, "_placement", placement);
            EnemyRoundController round = root.AddComponent<EnemyRoundController>();
            SetReference(round, "_enemy", enemy.GetComponent<EnemyTypingController>());
            SetReference(round, "_provider", provider);
            ConfigureScoreBinding(root, provider, round);
            return PrefabUtility.SaveAsPrefabAsset(root, ENCOUNTER_PREFAB_PATH);
        }

        /// <summary>
        /// 새 프리팹을 관찰할 카메라와 작업대가 있는 확인용 씬을 생성한다.
        /// 기본 프리팹 경로를 사용해 자동 시작을 설정하고 씬 저장 후 기존 활성 씬으로 돌아간다.
        /// </summary>
        private static void CreatePreviewScene()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                GameObject encounter = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(ENCOUNTER_PREFAB_PATH), scene);
                SerializedObject settings = new SerializedObject(encounter.GetComponent<EnemyRoundController>());
                settings.FindProperty("_startOnPlay").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                KeyboardScoreComboSceneSetup.CreateHud(encounter.GetComponent<KeyboardScoreCombo>());
                GameObject desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                desk.name = "EnemyWorkbench";
                desk.transform.position = new Vector3(0f, -0.15f, 0.3f);
                desk.transform.localScale = new Vector3(4f, 0.2f, 2.7f);
                desk.GetComponent<MeshRenderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Desk.mat");
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (!root.TryGetComponent(out Camera camera))
                        continue;
                    camera.transform.position = new Vector3(0f, 3.2f, -4.5f);
                    camera.transform.LookAt(new Vector3(0f, 0.65f, 0.3f));
                    camera.fieldOfView = 48f;
                    camera.nearClipPlane = 0.01f;
                }
                EditorSceneManager.SaveScene(scene, PREVIEW_SCENE_PATH);
            }
            finally
            {
                SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// 기존 적 프리팹의 팔에 후방 팔꿈치 제약과 준비 자세를 설정한다.
        /// 원본 팔의 중첩 연결을 유지하며 적 프리팹의 배치와 관절 설정만 저장한다.
        /// </summary>
        [MenuItem("Tools/Keyboard Arm/Configure Rear Elbow Pose")]
        public static void ConfigureRearElbowPose()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before configuring elbow constraints.");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(ENEMY_PREFAB_PATH);
            try
            {
                ConfigureArmPose(prefabRoot.GetComponentInChildren<KeyboardTypingArmController>(true).gameObject);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, ENEMY_PREFAB_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// arm의 배율로 후방 타건이 가능한 위치와 베이스 준비 회전을 설정한다.
        /// 팔꿈치 후방 제약과 최소 여유 거리를 저장하여 첫 타건부터 후방 자세를 사용하게 한다.
        /// </summary>
        private static void ConfigureArmPose(GameObject arm)
        {
            Vector3 position = arm.transform.localPosition;
            position.z = ARM_REAR_OFFSET * arm.transform.localScale.x;
            arm.transform.localPosition = position;
            SerializedObject settings = new SerializedObject(arm.GetComponent<KeyboardTypingArmController>());
            settings.FindProperty("_keepElbowBehind").boolValue = true;
            settings.FindProperty("_minimumElbowBackOffset").floatValue = ELBOW_BACK_OFFSET;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Transform yaw = (Transform)settings.FindProperty("_baseYaw").objectReferenceValue;
            yaw.localRotation = Quaternion.Euler(0f, ARM_READY_YAW, 0f);
        }

        /// <summary>
        /// root에 적의 키보드 공급과 라운드를 기존 스코어 컴포넌트로 연결한다.
        /// provider와 round 참조 및 자동 타격 설정을 저장하고 전용 연결 컴포넌트를 구성한다.
        /// </summary>
        private static void ConfigureScoreBinding(GameObject root, EnemyKeyboardProvider provider, EnemyRoundController round)
        {
            if (!root.TryGetComponent(out KeyboardScoreCombo scoreCombo))
                scoreCombo = root.AddComponent<KeyboardScoreCombo>();
            SerializedObject settings = new SerializedObject(scoreCombo);
            settings.FindProperty("_keyboard").objectReferenceValue = null;
            settings.FindProperty("_includeAutomaticHits").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (!root.TryGetComponent(out EnemyScoreComboBinding binding))
                binding = root.AddComponent<EnemyScoreComboBinding>();
            SetReference(binding, "_round", round);
            SetReference(binding, "_provider", provider);
            SetReference(binding, "_scoreCombo", scoreCombo);
        }

        /// <summary>
        /// component의 propertyName 직렬화 참조를 value로 설정한다.
        /// Inspector 필드 이름과 Unity 객체를 사용하여 에셋에 저장할 참조 상태를 변경한다.
        /// </summary>
        private static void SetReference(Component component, string propertyName, UnityEngine.Object value)
        {
            SerializedObject settings = new SerializedObject(component);
            settings.FindProperty(propertyName).objectReferenceValue = value;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
