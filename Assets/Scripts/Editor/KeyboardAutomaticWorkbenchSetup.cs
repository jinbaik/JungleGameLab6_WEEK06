using System;

using UnityEngine;

using Unity.Cinemachine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Economy;
using Game.Session;

namespace KeyboardModeling.Editor
{
    public static class KeyboardAutomaticWorkbenchSetup
    {
        /// <summary>
        /// 자동공격 복제 씬에 회수 불가능한 두 키보드 슬롯과 두 기계팔을 구성한다.
        /// 기존 세션, 입력 관리자와 자동공격 후보 목록을 사용해 별도 작업대 및 자세히 보기 카메라 참조를 저장한다.
        /// </summary>
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("복사 프로젝트 배치에서 실행합니다.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main_AutoAttack.unity", OpenSceneMode.Single);
            if (GameObject.Find("AutomaticWorkbench") != null)
                throw new InvalidOperationException("기존 자동작업대는 덮어쓰지 않습니다.");
            KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
            GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
            KeyboardRewardController rewards = UnityEngine.Object.FindFirstObjectByType<KeyboardRewardController>();
            KeyboardFeedbackManager feedbackTemplate = UnityEngine.Object.FindFirstObjectByType<KeyboardFeedbackManager>();
            KeyboardAutoAttackController previous = UnityEngine.Object.FindFirstObjectByType<KeyboardAutoAttackController>();
            SerializedObject previousSettings = new SerializedObject(previous);
            SerializedProperty previousKeys = previousSettings.FindProperty("_eligibleKeys");
            int[] eligible = new int[previousKeys.arraySize];
            for (int index = 0; index < eligible.Length; index++)
                eligible[index] = previousKeys.GetArrayElementAtIndex(index).intValue;
            KeyboardTypingArmController previousArm = (KeyboardTypingArmController)previousSettings.FindProperty("_arm").objectReferenceValue;
            UnityEngine.Object.DestroyImmediate(previousArm.gameObject);
            UnityEngine.Object.DestroyImmediate(previous.gameObject);
            SerializedObject interactionSettings = new SerializedObject(interaction);
            Transform reference = ((GameObject)interactionSettings.FindProperty("_referenceKeyboard").objectReferenceValue).transform;
            GameObject fragments = (GameObject)interactionSettings.FindProperty("_keyboardFragmentsPrefab").objectReferenceValue;
            float scale = reference.lossyScale.x / .01905f;
            Quaternion rotation = reference.rotation;
            Vector3 center = reference.TransformPoint(new Vector3(9.25f, 0, 2.8f));
            Transform bench = new GameObject("AutomaticWorkbench").transform;
            bench.gameObject.AddComponent<KeyboardAutoAttackManager>();
            bench.SetPositionAndRotation(center + rotation * new Vector3(5.8f, 0, 0), rotation);
            Material desk = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Materials/Desk.mat");
            Surface("WorkbenchTable", bench, new Vector3(1.8f, -.55f, 0), new Vector3(7.1f, 1f, 1.7f), desk);
            for (int slot = 0; slot < 2; slot++)
            {
                Transform root = new GameObject("AutomaticSlot_" + (slot + 1)).transform;
                root.SetParent(bench, false);
                root.localPosition = new Vector3(slot * 3.6f, 0, 0);
                KeyboardAutomaticStation station = root.gameObject.AddComponent<KeyboardAutomaticStation>();
                BoxCollider trigger = root.GetComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0, .4f, 0);
                trigger.size = new Vector3(3.1f, .9f, 1.1f);
                Surface("PlacementSurface", root, new Vector3(0, -.025f, 0), new Vector3(3.2f, .05f, 1.25f), desk);
                Transform placement = new GameObject("KeyboardPlacement").transform;
                placement.SetParent(root, false);
                placement.localPosition = -Vector3.Scale(new Vector3(9.25f, 0, 2.8f), reference.lossyScale);
                CinemachineCamera view = new GameObject("AutomaticViewCamera_" + (slot + 1)).AddComponent<CinemachineCamera>();
                view.transform.SetParent(root, false);
                view.transform.localPosition = new Vector3(0, 2.6f, -3.1f);
                view.transform.LookAt(root.TransformPoint(new Vector3(0, .8f, 0)));
                view.Priority = 0;
                SerializedObject cameraSettings = new SerializedObject(view);
                cameraSettings.FindProperty("Lens.FieldOfView").floatValue = 65;
                cameraSettings.ApplyModifiedPropertiesWithoutUndo();
                SerializedObject stationSettings = new SerializedObject(station);
                stationSettings.FindProperty("_placement").objectReferenceValue = placement;
                stationSettings.FindProperty("_viewCamera").objectReferenceValue = view;
                stationSettings.ApplyModifiedPropertiesWithoutUndo();
                KeyboardTypingArmController arm = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/KeyboardTypingArm.prefab"))).GetComponent<KeyboardTypingArmController>();
                arm.transform.SetParent(root, false);
                arm.transform.localScale = Vector3.one * scale;
                arm.transform.localPosition = new Vector3(0, 0, .145f * scale);
                SerializedObject armSettings = new SerializedObject(arm);
                armSettings.FindProperty("_temporarySmashInput").boolValue = false;
                armSettings.ApplyModifiedPropertiesWithoutUndo();
                arm.gameObject.SetActive(false);
                KeyboardFeedbackManager feedback = new GameObject("AutomaticFeedback_" + (slot + 1)).AddComponent<KeyboardFeedbackManager>();
                feedback.transform.SetParent(root, false);
                EditorUtility.CopySerialized(feedbackTemplate, feedback);
                SerializedObject feedbackSettings = new SerializedObject(feedback);
                feedbackSettings.FindProperty("_interactionController").objectReferenceValue = null;
                feedbackSettings.FindProperty("_previewKeyboard").objectReferenceValue = null;
                feedbackSettings.ApplyModifiedPropertiesWithoutUndo();
                KeyboardAutoAttackController auto = root.gameObject.AddComponent<KeyboardAutoAttackController>();
                SerializedObject autoSettings = new SerializedObject(auto);
                autoSettings.FindProperty("_arm").objectReferenceValue = arm;
                autoSettings.FindProperty("_station").objectReferenceValue = station;
                autoSettings.FindProperty("_stationFeedback").objectReferenceValue = feedback;
                SerializedProperty keys = autoSettings.FindProperty("_eligibleKeys");
                keys.arraySize = eligible.Length;
                for (int index = 0; index < eligible.Length; index++)
                    keys.GetArrayElementAtIndex(index).intValue = eligible[index];
                autoSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            int vertices = 0;
            int triangles = 0;
            foreach (MeshFilter filter in bench.GetComponentsInChildren<MeshFilter>(true))
            {
                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
            Debug.Log("AUTOMATIC_WORKBENCH_GEOMETRY vertices=" + vertices + " triangles=" + triangles + " renderers=" + bench.GetComponentsInChildren<MeshRenderer>(true).Length);
            KeyboardAutoAttackManagerSetup.ConfigureCurrentScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("AUTOMATIC_WORKBENCH_COMPLETE slots=2 arms=2 whiteKeys=" + eligible.Length);
        }

        /// <summary>
        /// 작업대의 보이는 상자 표면과 클릭 가능한 Collider를 생성한다.
        /// name, parent, position, size와 material을 사용해 표면 오브젝트를 반환한다.
        /// </summary>
        private static GameObject Surface(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.name = name;
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = position;
            surface.transform.localScale = size;
            surface.GetComponent<MeshRenderer>().sharedMaterial = material;
            return surface;
        }
    }
}
