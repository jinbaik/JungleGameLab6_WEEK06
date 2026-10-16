using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using Game.Shop;
using Game.Upgrades;

public static class BoxCapacityUpgradeSetup
{
    /// <summary>
    /// Main 1의 기존 상점에 상자 용량 강화를 등록하고 상자의 세션 참조를 연결한다.
    /// 기본 5개부터 최대 30개까지의 새 강화 에셋과 씬 변경을 저장한다.
    /// </summary>
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch project.");
        const string PATH = "Assets/Data/Upgrades/BoxCapacity.asset";
        UpgradeDefinition definition = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(PATH);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<UpgradeDefinition>();
            AssetDatabase.CreateAsset(definition, PATH);
            SerializedObject settings = new SerializedObject(definition);
            settings.FindProperty("_id").intValue = (int)UpgradeId.BoxCapacity;
            settings.FindProperty("_displayName").stringValue = "Box Upgrade";
            settings.FindProperty("_description").stringValue = "Increase the number of keyboards stored in each box.";
            SerializedProperty costs = settings.FindProperty("_levelCosts");
            costs.arraySize = 5;
            for (int index = 0; index < costs.arraySize; index++)
                costs.GetArrayElementAtIndex(index).longValue = 100L << index;
            SerializedProperty capacities = settings.FindProperty("_boxCapacities");
            capacities.arraySize = 6;
            for (int index = 0; index < capacities.arraySize; index++)
                capacities.GetArrayElementAtIndex(index).intValue = 5 + index * 5;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main 1.unity");
        definition = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(PATH);
        ShopView shop = UnityEngine.Object.FindFirstObjectByType<ShopView>(FindObjectsInactive.Include);
        GameSession session = (GameSession)new SerializedObject(shop).FindProperty("_gameSession").objectReferenceValue;
        SerializedObject sessionSettings = new SerializedObject(session);
        SerializedProperty definitions = sessionSettings.FindProperty("_upgradeDefinitions");
        bool exists = false;
        for (int index = 0; index < definitions.arraySize; index++)
        {
            UpgradeDefinition existing = (UpgradeDefinition)definitions.GetArrayElementAtIndex(index).objectReferenceValue;
            if (existing != null && existing.Id == UpgradeId.BoxCapacity) exists = true;
        }
        if (!exists)
        {
            int index = definitions.arraySize;
            definitions.arraySize++;
            definitions.GetArrayElementAtIndex(index).objectReferenceValue = definition;
        }
        sessionSettings.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(session);
        foreach (KeyboardStorageBox box in UnityEngine.Object.FindObjectsByType<KeyboardStorageBox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject boxSettings = new SerializedObject(box);
            boxSettings.FindProperty("_gameSession").objectReferenceValue = session;
            boxSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Main 1 save failed.");
        Debug.Log($"BOX_UPGRADE_READY levels={definition.MaxLevel} capacity={definition.GetBoxCapacity(0)}->{definition.GetBoxCapacity(definition.MaxLevel)} shopCount={definitions.arraySize}");
    }
}
