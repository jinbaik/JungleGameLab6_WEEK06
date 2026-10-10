using System;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

namespace KeyboardModeling.Editor
{
    public static class KeyboardTypingArmAnimationSetup
    {
        private const string PREFAB_PATH = "Assets/Resource/Prefabs/KeyboardTypingArm.prefab";

        /// <summary>
        /// 기존 기계팔에 코드 타건 관절과 입력 연결을 설정한다.
        /// 기계팔 프리팹, Keyboard 씬과 Main 씬의 기준 키보드를 사용하여 직렬화 참조와 스매쉬 입력 대상을 저장한다.
        /// </summary>
        public static void Build()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("씬 보호를 위해 Unity 배치 모드로 실행합니다.");
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            Transform shoulder = prefabRoot.transform.Find("ShoulderPivot");
            Transform yaw = prefabRoot.transform.Find("BaseYawPivot");
            if (yaw == null)
            {
                yaw = new GameObject("BaseYawPivot").transform;
                yaw.SetParent(prefabRoot.transform, false);
                shoulder.SetParent(yaw, false);
            }
            else
            {
                shoulder = yaw.Find("ShoulderPivot");
            }
            Transform elbow = shoulder.Find("ElbowPivot");
            Transform wrist = elbow.Find("WristPitchPivot");
            Transform contact = wrist.Find("TypingToolPivot/KeyContactPoint");
            KeyboardTypingArmController controller = prefabRoot.GetComponent<KeyboardTypingArmController>();
            if (controller == null)
                controller = prefabRoot.AddComponent<KeyboardTypingArmController>();
            SerializedObject settings = new SerializedObject(controller);
            settings.FindProperty("_baseYaw").objectReferenceValue = yaw;
            settings.FindProperty("_shoulder").objectReferenceValue = shoulder;
            settings.FindProperty("_elbow").objectReferenceValue = elbow;
            settings.FindProperty("_wrist").objectReferenceValue = wrist;
            settings.FindProperty("_contactPoint").objectReferenceValue = contact;
            settings.FindProperty("_pressDuration").floatValue = .035f;
            settings.FindProperty("_retractDuration").floatValue = .055f;
            settings.FindProperty("_hoverHeight").floatValue = .01f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PREFAB_PATH);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
            AssetDatabase.SaveAssets();

            EditorSceneManager.OpenScene("Assets/Scenes/Keyboard.unity", OpenSceneMode.Single);
            GameObject keyboard = Array.Find(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects(), item => item.name == "Keyboard_TKL_87");
            GameObject arm = GameObject.Find("KeyboardTypingArm");
            PlaceArm(arm, keyboard.transform);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            KeyboardInteractionController interaction = UnityEngine.Object.FindAnyObjectByType<KeyboardInteractionController>();
            SerializedObject interactionSettings = new SerializedObject(interaction);
            GameObject reference = (GameObject)interactionSettings.FindProperty("_referenceKeyboard").objectReferenceValue;
            arm = GameObject.Find("KeyboardTypingArm");
            if (arm == null)
                arm = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH));
            PlaceArm(arm, reference.transform);
            settings = new SerializedObject(arm.GetComponent<KeyboardTypingArmController>());
            KeyboardAutoAttackManagerSetup.ConfigureCurrentScene();
            settings.FindProperty("_temporarySmashInput").boolValue = true;
            settings.FindProperty("_pressDuration").floatValue = .035f;
            settings.FindProperty("_retractDuration").floatValue = .055f;
            settings.FindProperty("_hoverHeight").floatValue = .01f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Component fixedCamera = (Component)interactionSettings.FindProperty("_fixedCam").objectReferenceValue;
            SerializedObject cameraSettings = new SerializedObject(fixedCamera);
            cameraSettings.FindProperty("Lens.FieldOfView").floatValue = 65;
            cameraSettings.ApplyModifiedPropertiesWithoutUndo();
            fixedCamera.transform.rotation = reference.transform.rotation * Quaternion.Euler(25, 0, 0);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            SavePreview(fixedCamera.transform);
            Debug.Log("TYPING_ARM_ANIMATION_SETUP_COMPLETE");
        }

        /// <summary>
        /// 기계팔을 기준 키보드 뒤쪽 중앙에 배치한다.
        /// arm과 keyboard의 배율 및 키 간격을 사용해 모든 키가 팔 길이 안에 들어오는 위치와 회전을 설정한다.
        /// </summary>
        private static void PlaceArm(GameObject arm, Transform keyboard)
        {
            float scale = keyboard.lossyScale.x / .01905f;
            arm.transform.localScale = Vector3.one * scale;
            arm.transform.SetPositionAndRotation(
                keyboard.TransformPoint(new Vector3(9.25f, 0, 2.8f)) + keyboard.rotation * new Vector3(0, 0, .145f * scale),
                keyboard.rotation);
            SerializedObject keyboardSettings = new SerializedObject(keyboard.GetComponent<KeyboardInputController>());
            SerializedProperty bindings = keyboardSettings.FindProperty("_bindings");
            float maximumDistance = 0;
            for (int index = 0; index < bindings.arraySize; index++)
            {
                Transform keycap = (Transform)bindings.GetArrayElementAtIndex(index).FindPropertyRelative("_keycap").objectReferenceValue;
                BoxCollider collider = keycap.GetComponent<BoxCollider>();
                Vector3 point = keycap.TransformPoint(collider.center + Vector3.up * collider.size.y * .5f);
                Vector3 wrist = arm.transform.InverseTransformPoint(point) + Vector3.up * (.264f + .02f - .087f);
                maximumDistance = Mathf.Max(maximumDistance, wrist.magnitude);
            }
            Debug.Log("TYPING_ARM_PLACEMENT keys=" + bindings.arraySize + " maxHoverReach=" + maximumDistance + " availableReach=0.37");
        }

        /// <summary>
        /// 스매쉬 고정 카메라의 키보드와 기계팔 배치를 미리보기로 저장한다.
        /// viewpoint와 기존 메인 카메라 설정을 사용해 PNG를 생성하고 임시 카메라와 렌더 타깃을 해제한다.
        /// </summary>
        private static void SavePreview(Transform viewpoint)
        {
            Directory.CreateDirectory(".local/TypingArmAnimationPreview");
            GameObject cameraObject = new GameObject("TypingArmPreviewCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main);
            camera.transform.SetPositionAndRotation(viewpoint.position, viewpoint.rotation);
            camera.fieldOfView = 65;
            RenderTexture target = new RenderTexture(1280, 720, 24);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            File.WriteAllBytes(".local/TypingArmAnimationPreview/Main.png", texture.EncodeToPNG());
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}

