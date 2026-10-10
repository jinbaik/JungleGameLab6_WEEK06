using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Economy;
using Game.Session;

namespace KeyboardModeling.Editor
{
    public static class KeyboardMainAutoAttackRecovery
    {
        /// <summary>
        /// Main에 가져온 작업대의 중복 매니저와 누락된 씬 참조를 복구한다.
        /// 기존 작업대 매니저의 공격 수치와 배치를 유지하고 다른 매니저만 제거하여 공통 참조를 저장한다.
        /// </summary>
        public static void Restore()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Use the isolated recovery project.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            KeyboardAutoAttackManager[] managers = UnityEngine.Object.FindObjectsByType<KeyboardAutoAttackManager>(FindObjectsSortMode.None);
            KeyboardAutoAttackManager selected = null;
            foreach (KeyboardAutoAttackManager manager in managers)
            {
                if (manager.GetComponentsInChildren<KeyboardAutoAttackController>(true).Length > 0)
                {
                    selected = manager;
                    break;
                }
            }
            if (selected == null)
                throw new InvalidOperationException("Main has no automatic workbench manager.");
            foreach (KeyboardAutoAttackManager manager in managers)
            {
                if (manager != selected)
                    UnityEngine.Object.DestroyImmediate(manager);
            }
            KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
            SerializedObject settings = new SerializedObject(selected);
            settings.FindProperty("_interactionController").objectReferenceValue = interaction;
            settings.FindProperty("_gameSession").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<GameSession>();
            settings.FindProperty("_rewardController").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<KeyboardRewardController>();
            SerializedObject interactionSettings = new SerializedObject(interaction);
            settings.FindProperty("_keyboardFragmentsPrefab").objectReferenceValue = interactionSettings.FindProperty("_keyboardFragmentsPrefab").objectReferenceValue;
            settings.ApplyModifiedPropertiesWithoutUndo();
            KeyboardAutoAttackRewardSetup.ConfigureCurrentScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            MonitorCoinRainSetup.RestoreSceneReferences();
            Debug.Log($"MAIN_AUTO_ATTACK_RECOVERED managers={UnityEngine.Object.FindObjectsByType<KeyboardAutoAttackManager>(FindObjectsSortMode.None).Length} slots={UnityEngine.Object.FindObjectsByType<KeyboardAutomaticStation>(FindObjectsSortMode.None).Length}");
        }
    }
}
