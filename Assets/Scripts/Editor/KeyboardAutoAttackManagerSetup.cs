using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using Game.Economy;

namespace KeyboardModeling.Editor
{
    public static class KeyboardAutoAttackManagerSetup
    {
        /// <summary>
        /// 두 메인씬에 기계팔 공통 참조 매니저를 배치한다.
        /// 각 씬의 입력 관리자와 세션을 한 번 할당하고 기존 배치를 유지하며 저장한다.
        /// </summary>
        public static void RestoreBoth()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Use the isolated batch project.");
            foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/Main_AutoAttack.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path);
                ConfigureCurrentScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("ARM_SCENE_MANAGER_CONNECTED " + path);
            }
        }

        /// <summary>
        /// 현재 씬의 모든 기계팔이 사용할 공통 참조를 구성한다.
        /// 현재 입력 관리자와 세션을 찾아 매니저에 저장하며 기존 매니저가 있으면 재사용한다.
        /// </summary>
        public static void ConfigureCurrentScene()
        {
            KeyboardAutoAttackManager manager = UnityEngine.Object.FindFirstObjectByType<KeyboardAutoAttackManager>();
            if (manager == null)
                manager = new GameObject("KeyboardAutoAttackManager").AddComponent<KeyboardAutoAttackManager>();
            SerializedObject settings = new SerializedObject(manager);
            settings.FindProperty("_interactionController").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
            settings.FindProperty("_gameSession").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<GameSession>();
            settings.FindProperty("_rewardController").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<KeyboardRewardController>();
            SerializedObject interactionSettings = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>());
            settings.FindProperty("_keyboardFragmentsPrefab").objectReferenceValue = interactionSettings.FindProperty("_keyboardFragmentsPrefab").objectReferenceValue;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 두 씬의 공통 참조와 슬롯별 팔의 도달 거리를 진단 로그로 출력한다.
        /// 현재 씬의 배치와 기본 키보드 프리팹의 키캡 좌표를 사용하며 에셋은 수정하지 않는다.
        /// </summary>
        public static void InspectBoth()
        {
            foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/Main_AutoAttack.unity" })
            {
                EditorSceneManager.OpenScene(path);
                KeyboardAutoAttackManager manager = UnityEngine.Object.FindFirstObjectByType<KeyboardAutoAttackManager>();
                Debug.Log($"SCENE_AUDIT {path} managers={UnityEngine.Object.FindObjectsByType<KeyboardAutoAttackManager>(FindObjectsSortMode.None).Length} reward={manager.RewardController} coin={UnityEngine.Object.FindFirstObjectByType<MonitorCoinRain>()}");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Keyboard_TKL_87.prefab");
                foreach (KeyboardAutomaticStation station in UnityEngine.Object.FindObjectsByType<KeyboardAutomaticStation>(FindObjectsSortMode.None))
                {
                    KeyboardTypingArmController arm = station.GetComponentInChildren<KeyboardTypingArmController>(true);
                    SerializedObject settings = new SerializedObject(arm);
                    Transform shoulder = (Transform)settings.FindProperty("_shoulder").objectReferenceValue;
                    Transform elbow = (Transform)settings.FindProperty("_elbow").objectReferenceValue;
                    Transform wrist = (Transform)settings.FindProperty("_wrist").objectReferenceValue;
                    Transform tip = (Transform)settings.FindProperty("_contactPoint").objectReferenceValue;
                    float tool = wrist.InverseTransformPoint(tip.position).magnitude;
                    float maximum = elbow.localPosition.magnitude + wrist.localPosition.magnitude;
                    float minimum = Mathf.Abs(elbow.localPosition.magnitude - wrist.localPosition.magnitude);
                    Vector3 origin = arm.transform.InverseTransformPoint(shoulder.position);
                    int unreachable = 0;
                    float farthest = 0;
                    foreach (KeycapHealth health in prefab.GetComponent<KeyboardInputController>().GetDestructionKeycaps())
                    {
                        BoxCollider collider = health.GetComponent<BoxCollider>();
                        Vector3 local = prefab.transform.InverseTransformPoint(health.transform.TransformPoint(collider.center + Vector3.up * collider.size.y * .5f));
                        Vector3 world = station.Placement.position + station.Placement.rotation * Vector3.Scale(local, prefab.transform.localScale);
                        Vector3 normal = station.Placement.up;
                        Vector3 target = arm.transform.InverseTransformPoint(world + normal * settings.FindProperty("_hoverHeight").floatValue * arm.transform.lossyScale.x) + arm.transform.InverseTransformDirection(normal).normalized * tool;
                        float distance = (target - origin).magnitude;
                        farthest = Mathf.Max(farthest, distance);
                        if (distance >= maximum - .0001f || distance <= minimum + .0001f) unreachable++;
                    }
                    Debug.Log($"ARM_AUDIT {station.name} position={arm.transform.position} scale={arm.transform.lossyScale} unreachable={unreachable} farthest={farthest} max={maximum} placement={station.Placement.position}");
                }
            }
        }
    }
}
