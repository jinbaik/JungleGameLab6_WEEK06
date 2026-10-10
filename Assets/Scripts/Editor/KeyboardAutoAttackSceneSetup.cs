using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using Game.Upgrades;

namespace KeyboardModeling.Editor
{
    public static class KeyboardAutoAttackSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/Main_AutoAttack.unity";
        private const string UPGRADE_PATH = "Assets/Data/Upgrades/AutoAttackArmDemo.asset";

        /// <summary>
        /// Main을 복제하고 자동공격 강화와 비활성 기계팔을 연결한다.
        /// 기존 씬과 기본 키보드의 흰색 재질을 사용해 새 씬, 전용 강화 정의와 자동공격 참조를 저장한다.
        /// </summary>
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬 보호를 위해 Unity 배치 모드로 실행합니다.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) != null)
                throw new InvalidOperationException("기존 자동공격 씬은 덮어쓰지 않습니다.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            KeyboardAutoAttackManagerSetup.ConfigureCurrentScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), SCENE_PATH, true);
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
            KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
            GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
            SerializedObject interactionSettings = new SerializedObject(interaction);
            GameObject reference = (GameObject)interactionSettings.FindProperty("_referenceKeyboard").objectReferenceValue;
            GameObject keyboardPrefab = (GameObject)interactionSettings.FindProperty("_inGameKeyBoardPrefab").objectReferenceValue;
            Material white = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Porcelain_PBT.mat");
            var eligible = new List<Key>();
            foreach (KeyValuePair<Key, KeycapHealth> pair in keyboardPrefab.GetComponent<KeyboardInputController>().GetKeycapLayout())
            {
                Transform shell = pair.Value.transform.Find("PBT_SculptedShell");
                if (shell.GetComponent<MeshRenderer>().sharedMaterial == white)
                    eligible.Add(pair.Key);
            }
            if (eligible.Count == 0)
                throw new InvalidOperationException("기본 흰색 키 영역을 찾지 못했습니다.");
            eligible.Sort();

            UpgradeDefinition upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(UPGRADE_PATH);
            if (upgrade == null)
            {
                upgrade = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UpgradeDefinition>("Assets/Data/Upgrades/AutoClick.asset"));
                upgrade.name = "AutoAttackArmDemo";
                AssetDatabase.CreateAsset(upgrade, UPGRADE_PATH);
            }
            SerializedObject upgradeSettings = new SerializedObject(upgrade);
            upgradeSettings.FindProperty("_displayName").stringValue = "Auto Attack";
            upgradeSettings.FindProperty("_description").stringValue = "Unlock a robotic arm: one intact white key, 1 damage, every second.";
            upgradeSettings.FindProperty("_levelCosts").arraySize = 1;
            upgradeSettings.FindProperty("_levelCosts").GetArrayElementAtIndex(0).longValue = 50;
            upgradeSettings.ApplyModifiedPropertiesWithoutUndo();
            SerializedObject sessionSettings = new SerializedObject(session);
            SerializedProperty definitions = sessionSettings.FindProperty("_upgradeDefinitions");
            bool replaced = false;
            for (int index = 0; index < definitions.arraySize; index++)
            {
                UpgradeDefinition definition = (UpgradeDefinition)definitions.GetArrayElementAtIndex(index).objectReferenceValue;
                if (definition.Id != UpgradeId.AutoClick)
                    continue;
                definitions.GetArrayElementAtIndex(index).objectReferenceValue = upgrade;
                replaced = true;
                break;
            }
            if (!replaced)
            {
                definitions.arraySize++;
                definitions.GetArrayElementAtIndex(definitions.arraySize - 1).objectReferenceValue = upgrade;
            }
            sessionSettings.ApplyModifiedPropertiesWithoutUndo();

            KeyboardTypingArmController[] arms = UnityEngine.Object.FindObjectsByType<KeyboardTypingArmController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            KeyboardTypingArmController arm = arms.Length > 0 ? arms[0] :
                ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/KeyboardTypingArm.prefab"))).GetComponent<KeyboardTypingArmController>();
            float scale = reference.transform.lossyScale.x / .01905f;
            arm.transform.localScale = Vector3.one * scale;
            arm.transform.SetPositionAndRotation(
                reference.transform.TransformPoint(new Vector3(9.25f, 0, 2.8f)) + reference.transform.rotation * new Vector3(0, 0, .145f * scale),
                reference.transform.rotation);
            SerializedObject armSettings = new SerializedObject(arm);
            armSettings.FindProperty("_temporarySmashInput").boolValue = false;
            armSettings.ApplyModifiedPropertiesWithoutUndo();
            arm.gameObject.SetActive(false);

            KeyboardAutoAttackController auto = new GameObject("AutomaticKeyboardAttack").AddComponent<KeyboardAutoAttackController>();
            auto.gameObject.AddComponent<KeyboardAutoAttackManager>();
            SerializedObject autoSettings = new SerializedObject(auto);
            autoSettings.FindProperty("_arm").objectReferenceValue = arm;
            SerializedProperty keys = autoSettings.FindProperty("_eligibleKeys");
            keys.arraySize = eligible.Count;
            for (int index = 0; index < eligible.Count; index++)
                keys.GetArrayElementAtIndex(index).intValue = (int)eligible[index];
            autoSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            KeyboardAutoAttackManagerSetup.ConfigureCurrentScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("AUTO_ATTACK_SCENE_COMPLETE whiteKeys=" + eligible.Count + " interval=1 damage=1 upgradeCost=50");
        }
    }
}
