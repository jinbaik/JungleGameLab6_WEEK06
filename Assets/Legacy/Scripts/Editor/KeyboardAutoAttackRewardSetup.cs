using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.UI;

namespace KeyboardModeling.Editor
{
    public static class KeyboardAutoAttackRewardSetup
    {
        /// <summary>
        /// 자동공격 씬의 기존 보상 표시 프리팹과 카메라 참조를 복구한다.
        /// Main_AutoAttack과 ToastCanvas를 사용해 모든 보상 Presenter에 같은 풀을 연결하고 씬을 저장한다.
        /// </summary>
        public static void Restore()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run this repair in the isolated batch project.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main_AutoAttack.unity");
            ConfigureCurrentScene();
        }

        /// <summary>
        /// 현재 씬의 보상 Presenter와 기존 ToastCanvas 및 메인 카메라를 연결한다.
        /// 누락된 풀을 재사용 프리팹으로 구성하고 변경된 씬 참조를 저장한다.
        /// </summary>
        public static void ConfigureCurrentScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            ToastPool pool = UnityEngine.Object.FindFirstObjectByType<ToastPool>();
            if (pool == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/UI/ToastCanvas.prefab");
                GameObject canvas = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                canvas.transform.localScale = Vector3.one;
                pool = canvas.GetComponent<ToastPool>();
            }
            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("Auto attack scene requires its main camera.");
            SerializedObject poolSettings = new SerializedObject(pool);
            poolSettings.FindProperty("_camera").objectReferenceValue = camera;
            poolSettings.ApplyModifiedPropertiesWithoutUndo();
            KeyboardToastPresenter[] presenters = UnityEngine.Object.FindObjectsByType<KeyboardToastPresenter>(FindObjectsSortMode.None);
            if (presenters.Length == 0)
                throw new InvalidOperationException("Auto attack reward presenter is missing.");
            foreach (KeyboardToastPresenter presenter in presenters)
            {
                SerializedObject settings = new SerializedObject(presenter);
                settings.FindProperty("_toastPool").objectReferenceValue = pool;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"AUTO_ATTACK_REWARD_REPAIRED presenters={presenters.Length} pool={pool.name} camera={camera.name}");
        }
    }
}
