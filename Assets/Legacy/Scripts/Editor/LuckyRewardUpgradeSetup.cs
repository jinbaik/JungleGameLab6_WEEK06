using System;

using UnityEngine;
using UnityEngine.UI;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using Game.Shop;
using Game.Upgrades;

public static class LuckyRewardUpgradeSetup
{
    /// <summary>
    /// Main 1의 기존 강화 목록에 10% 확률 두 배 보상 강화를 추가한다.
    /// 기존 세션과 새 강화 데이터만 연결하고 다른 씬·강화 값은 변경하지 않은 채 저장한다.
    /// </summary>
    public static void Build()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch project.");
        const string PATH = "Assets/Data/Upgrades/LuckyReward.asset";
        UpgradeDefinition definition = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(PATH);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<UpgradeDefinition>();
            AssetDatabase.CreateAsset(definition, PATH);
            SerializedObject settings = new SerializedObject(definition);
            settings.FindProperty("_id").intValue = (int)UpgradeId.LuckyReward;
            settings.FindProperty("_displayName").stringValue = "행운 보상";
            settings.FindProperty("_description").stringValue = "키캡 파괴 시 받는 재화가 두 배가 될 확률을 높입니다.";
            settings.FindProperty("_doubleRewardChance").floatValue = .1f;
            SerializedProperty costs = settings.FindProperty("_levelCosts");
            costs.arraySize = 1;
            costs.GetArrayElementAtIndex(0).longValue = 200;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }
        var scene = EditorSceneManager.OpenScene("Assets/Legacy/Scene/Main 1.unity");
        definition = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(PATH);
        if (definition == null) throw new InvalidOperationException("LuckyReward asset is missing after scene load.");
        ShopView shop = UnityEngine.Object.FindFirstObjectByType<ShopView>();
        SerializedObject shopSettings = new SerializedObject(shop);
        GameSession session = (GameSession)shopSettings.FindProperty("_gameSession").objectReferenceValue;
        SerializedObject sessionSettings = new SerializedObject(session);
        SerializedProperty definitions = sessionSettings.FindProperty("_upgradeDefinitions");
        bool exists = false;
        for (int index = 0; index < definitions.arraySize; index++)
        {
            SerializedProperty entry = definitions.GetArrayElementAtIndex(index);
            UpgradeDefinition existing = (UpgradeDefinition)entry.objectReferenceValue;
            if (existing == null && !exists)
            {
                entry.objectReferenceValue = definition;
                exists = true;
            }
            else if (existing != null && existing.Id == UpgradeId.LuckyReward)
                exists = true;
        }
        if (!exists)
        {
            int index = definitions.arraySize;
            definitions.arraySize++;
            definitions.GetArrayElementAtIndex(index).objectReferenceValue = definition;
        }
        sessionSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(session);
        PrefabUtility.RecordPrefabInstancePropertyModifications(session);
        RectTransform content = (RectTransform)shopSettings.FindProperty("_itemRoot").objectReferenceValue;
        if (content.GetComponentInParent<ScrollRect>() == null)
        {
            Transform parent = content.parent;
            GameObject viewportObject = new GameObject("UpgradeViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(parent, false);
            viewport.SetSiblingIndex(content.GetSiblingIndex());
            viewport.anchorMin = content.anchorMin;
            viewport.anchorMax = content.anchorMax;
            viewport.pivot = content.pivot;
            viewport.sizeDelta = content.sizeDelta;
            viewport.anchoredPosition = content.anchoredPosition;
            viewportObject.GetComponent<Image>().color = Color.white;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, "Assets/Legacy/Scene/Main 1.unity"))
            throw new InvalidOperationException("Main 1 save failed.");
        Debug.Log($"LUCKY_SHOP_AUDIT shopScene={shop.gameObject.scene.path} sessionScene={session.gameObject.scene.path} count={definitions.arraySize} last={definitions.GetArrayElementAtIndex(definitions.arraySize - 1).objectReferenceValue} contentScene={content.gameObject.scene.path}");
        Debug.Log("LUCKY_REWARD_READY scene=Main 1 chance=0.1 maxLevel=1 price=200");
    }
}
