using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Shop;
using Game.Session;
using Game.Upgrades;

public static class HandPowerUpgradeSetup
{
    /// <summary>
    /// Main 1의 직접·범위 강화 세 항목을 승급형 손 강화로 통합하고 행운 강화 가격을 10단계까지 채운다.
    /// 새 강화 데이터와 기존 세션 목록을 사용하며 다른 씬의 강화 목록은 변경하지 않는다.
    /// </summary>
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch project.");
        const string HAND_PATH = "Assets/Data/Upgrades/HandPower.asset";
        UpgradeDefinition hand = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(HAND_PATH);
        if (hand == null)
        {
            hand = ScriptableObject.CreateInstance<UpgradeDefinition>();
            AssetDatabase.CreateAsset(hand, HAND_PATH);
            SerializedObject settings = new SerializedObject(hand);
            settings.FindProperty("_id").intValue = (int)UpgradeId.HandPower;
            settings.FindProperty("_displayName").stringValue = "손 강화";
            settings.FindProperty("_description").stringValue = "손의 추가 피해를 높이고 승급으로 주먹과 망치의 추가 타격을 해금합니다.";
            SerializedProperty costs = settings.FindProperty("_levelCosts");
            costs.arraySize = 15;
            for (int index = 0; index < 15; index++)
                costs.GetArrayElementAtIndex(index).longValue = (index + 1) * 100;
            costs.GetArrayElementAtIndex(5).longValue = 1000;
            costs.GetArrayElementAtIndex(10).longValue = 2000;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hand);
        }
        UpgradeDefinition lucky = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>("Assets/Data/Upgrades/LuckyReward.asset");
        SerializedObject luckySettings = new SerializedObject(lucky);
        SerializedProperty luckyCosts = luckySettings.FindProperty("_levelCosts");
        luckyCosts.arraySize = 10;
        for (int index = 0; index < 10; index++)
            luckyCosts.GetArrayElementAtIndex(index).longValue = (index + 1) * 200;
        luckySettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lucky);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main 1.unity");
        hand = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(HAND_PATH);
        lucky = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>("Assets/Data/Upgrades/LuckyReward.asset");
        ShopView shop = UnityEngine.Object.FindFirstObjectByType<ShopView>();
        GameSession session = (GameSession)new SerializedObject(shop).FindProperty("_gameSession").objectReferenceValue;
        SerializedObject sessionSettings = new SerializedObject(session);
        SerializedProperty definitions = sessionSettings.FindProperty("_upgradeDefinitions");
        List<UpgradeDefinition> result = new List<UpgradeDefinition> { hand };
        bool hasLucky = false;
        for (int index = 0; index < definitions.arraySize; index++)
        {
            UpgradeDefinition definition = (UpgradeDefinition)definitions.GetArrayElementAtIndex(index).objectReferenceValue;
            if (definition == null || definition.Id == UpgradeId.SmashDamage || definition.Id == UpgradeId.AreaSmash || definition.Id == UpgradeId.AreaDamage || definition.Id == UpgradeId.HandPower) continue;
            result.Add(definition);
            if (definition.Id == UpgradeId.LuckyReward) hasLucky = true;
        }
        if (!hasLucky) result.Add(lucky);
        definitions.arraySize = result.Count;
        for (int index = 0; index < result.Count; index++)
            definitions.GetArrayElementAtIndex(index).objectReferenceValue = result[index];
        sessionSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(session);
        PrefabUtility.RecordPrefabInstancePropertyModifications(session);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Main 1 save failed.");
        Debug.Log($"HAND_UPGRADE_READY shopCount={result.Count} handLevels={hand.MaxLevel} luckyLevels={lucky.MaxLevel}");
        for (int level = 0; level <= 15; level++)
            Debug.Log($"HAND_STAGE level={level} name={hand.GetHandName(level)} damageBonus={hand.GetHandDamageBonus(level)} radius={hand.GetHandRadius(level)} promotion={hand.IsHandPromotion(level)}");
    }
}
