using System;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Player;

public sealed class KeyboardMaterialRegistrationWindow : EditorWindow
{
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/SeperateKeyboard/Keyboard_Customize.prefab";
    private const string SCENE_PATH = "Assets/Scenes/WorkShop2.unity";
    private GameObject _keyboardPrefab;
    private SceneAsset _workshopScene;
    private Material _material;
    private string _message;

    /// <summary>
    /// 메뉴 입력으로 키보드 재질 등록 창을 연다.
    /// 기본 키보드 프리팹과 작업실 씬을 지정한 창을 표시한다.
    /// </summary>
    [MenuItem("Tools/Keyboard/Register Material")]
    public static void OpenWindow()
    {
        GetWindow<KeyboardMaterialRegistrationWindow>("키보드 재질 등록");
    }

    void OnEnable()
    {
        _keyboardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        _workshopScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("키캡 · Case 공용 재질 등록", EditorStyles.boldLabel);
        _material = (Material)EditorGUILayout.ObjectField("등록할 Material", _material, typeof(Material), false);
        _keyboardPrefab = (GameObject)EditorGUILayout.ObjectField("키보드 프리팹", _keyboardPrefab, typeof(GameObject), false);
        _workshopScene = (SceneAsset)EditorGUILayout.ObjectField("작업실 씬", _workshopScene, typeof(SceneAsset), false);
        EditorGUILayout.HelpBox("적용 시 프리팹의 Shared Materials, 작업실 UI의 Keycap Materials, 저장용 Catalog / ID에 추가하고 저장합니다. 기존 항목과 ID는 유지하며 중복은 추가하지 않습니다.", MessageType.Info);
        bool prefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null;
        if (prefabMode) EditorGUILayout.HelpBox("Prefab Mode를 닫은 뒤 적용하세요.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || prefabMode || _material == null || _keyboardPrefab == null || _workshopScene == null))
        {
            if (GUILayout.Button("적용하기", GUILayout.Height(36f))) RegisterMaterial();
        }
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
    }

    /// <summary>
    /// 창에서 선택한 Material을 프리팹과 작업실 UI 및 저장 카탈로그에 등록한다.
    /// 기존 ID를 유지하고 Material GUID를 새 ID로 사용하며 변경한 애셋을 저장한다.
    /// </summary>
    private void RegisterMaterial()
    {
        Scene scene = default;
        bool openedScene = false;
        Scene previousActive = SceneManager.GetActiveScene();
        try
        {
            if (PrefabUtility.GetPrefabAssetType(_keyboardPrefab) == PrefabAssetType.NotAPrefab)
                throw new InvalidOperationException("키보드 프리팹 애셋을 지정하세요.");
            KeyboardCustomization customization = _keyboardPrefab.GetComponent<KeyboardCustomization>();
            if (customization == null) throw new InvalidOperationException("프리팹 루트에 KeyboardCustomization이 없습니다.");
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_material));
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("저장된 Material 애셋을 지정하세요.");
            string scenePath = AssetDatabase.GetAssetPath(_workshopScene);
            scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedScene = true;
            }
            var overlays = new System.Collections.Generic.List<KeyboardEditOverlayUI>();
            foreach (GameObject root in scene.GetRootGameObjects())
                overlays.AddRange(root.GetComponentsInChildren<KeyboardEditOverlayUI>(true));
            if (overlays.Count != 1) throw new InvalidOperationException("작업실 씬에 KeyboardEditOverlayUI가 정확히 하나 있어야 합니다.");
            KeyboardEditOverlayUI overlay = overlays[0];
            var ui = new SerializedObject(overlay);
            var storage = ui.FindProperty("_photoStorage").objectReferenceValue as KeyboardCustomizationStorage;
            if (storage == null || storage.gameObject.scene != scene)
                throw new InvalidOperationException("UI의 Photo Storage를 같은 작업실 씬의 저장 컴포넌트에 연결하세요.");
            var registry = new SerializedObject(storage);
            SerializedProperty catalog = registry.FindProperty("_materialCatalog");
            SerializedProperty ids = registry.FindProperty("_materialIds");
            if (catalog.arraySize != ids.arraySize) throw new InvalidOperationException("저장 Catalog와 ID 배열 길이를 먼저 맞춰주세요.");
            int existingIndex = FindMaterial(catalog, _material);
            for (int index = 0; index < ids.arraySize; index++)
                if (index != existingIndex && ids.GetArrayElementAtIndex(index).stringValue == guid)
                    throw new InvalidOperationException("다른 재질에 동일한 저장 ID가 등록되어 있습니다.");

            Undo.RecordObjects(new UnityEngine.Object[] { overlay, storage }, "Register keyboard material");
            var keyboard = new SerializedObject(customization);
            bool prefabChanged = AddMaterial(keyboard.FindProperty("_materials"), _material);
            bool uiChanged = AddMaterial(ui.FindProperty("_keycapMaterials"), _material);
            bool storageChanged = existingIndex < 0;
            if (storageChanged)
            {
                AddMaterial(catalog, _material);
                ids.arraySize = catalog.arraySize;
                ids.GetArrayElementAtIndex(ids.arraySize - 1).stringValue = guid;
            }
            keyboard.ApplyModifiedPropertiesWithoutUndo();
            ui.ApplyModifiedProperties();
            registry.ApplyModifiedProperties();
            if (prefabChanged) PrefabUtility.SavePrefabAsset(_keyboardPrefab);
            if (uiChanged) PrefabUtility.RecordPrefabInstancePropertyModifications(overlay);
            if (storageChanged) PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
            if (uiChanged || storageChanged)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("작업실 씬 저장에 실패했습니다.");
            }
            _message = $"{_material.name} 등록 완료\nShared Materials: {(prefabChanged ? "추가" : "이미 등록됨")}\nKeycap Materials: {(uiChanged ? "추가" : "이미 등록됨")}\n저장 Catalog / ID: {(storageChanged ? "추가" : "이미 등록됨")}";
        }
        catch (Exception exception)
        {
            _message = "등록 실패: " + exception.Message;
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            if (openedScene && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// array에서 material과 같은 참조를 찾는다.
    /// 기존 인덱스 또는 미등록을 나타내는 -1을 반환한다.
    /// </summary>
    private static int FindMaterial(SerializedProperty array, Material material)
    {
        for (int index = 0; index < array.arraySize; index++)
            if (array.GetArrayElementAtIndex(index).objectReferenceValue == material) return index;
        return -1;
    }

    /// <summary>
    /// array에 material이 없을 때만 마지막 항목으로 추가한다.
    /// 기존 배열 순서를 유지하며 추가 여부를 반환한다.
    /// </summary>
    private static bool AddMaterial(SerializedProperty array, Material material)
    {
        if (FindMaterial(array, material) >= 0) return false;
        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = material;
        return true;
    }
}
