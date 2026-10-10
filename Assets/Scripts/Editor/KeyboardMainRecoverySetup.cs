using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.UI;

namespace KeyboardModeling.Editor
{
    public static class KeyboardMainRecoverySetup
    {
        /// <summary>
        /// 현재 Main 배치를 보존하며 누락된 기계팔, 코인 효과와 보상 UI 연결을 복구한다.
        /// 기존 프리팹과 씬 기준 키보드를 사용하고 복구된 참조만 Main에 저장한다.
        /// </summary>
        public static void Restore()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run recovery in the isolated batch project.");
            MonitorCoinRainSetup.RestoreSceneReferences();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
            SerializedObject interactionSettings = new SerializedObject(interaction);
            Transform reference = ((GameObject)interactionSettings.FindProperty("_referenceKeyboard").objectReferenceValue).transform;
            KeyboardTypingArmController arm = UnityEngine.Object.FindFirstObjectByType<KeyboardTypingArmController>();
            if (arm == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/KeyboardTypingArm.prefab");
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                float scale = reference.lossyScale.x / .01905f;
                instance.transform.localScale = Vector3.one * scale;
                instance.transform.SetPositionAndRotation(reference.TransformPoint(new Vector3(9.25f, 0, 2.8f)) + reference.rotation * new Vector3(0, 0, .145f * scale), reference.rotation);
                arm = instance.GetComponent<KeyboardTypingArmController>();
            }
            SerializedObject armSettings = new SerializedObject(arm);
            armSettings.FindProperty("_temporarySmashInput").boolValue = true;
            armSettings.ApplyModifiedPropertiesWithoutUndo();
            ToastPool pool = UnityEngine.Object.FindFirstObjectByType<ToastPool>();
            if (pool == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/UI/ToastCanvas.prefab");
                GameObject canvas = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                canvas.transform.localScale = Vector3.one;
                pool = canvas.GetComponent<ToastPool>();
            }
            SerializedObject poolSettings = new SerializedObject(pool);
            poolSettings.FindProperty("_camera").objectReferenceValue = Camera.main;
            poolSettings.ApplyModifiedPropertiesWithoutUndo();
            foreach (KeyboardToastPresenter presenter in UnityEngine.Object.FindObjectsByType<KeyboardToastPresenter>(FindObjectsSortMode.None))
            {
                SerializedObject settings = new SerializedObject(presenter);
                settings.FindProperty("_toastPool").objectReferenceValue = pool;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            KeyboardAutoAttackManagerSetup.ConfigureCurrentScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("MAIN_RECOVERY_COMPLETE coin=connected arm=connected toast=connected");
        }
    }
}
