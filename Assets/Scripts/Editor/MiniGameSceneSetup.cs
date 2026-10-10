using System;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using Unity.Cinemachine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;

namespace KeyboardModeling.Editor
{
    public static class MiniGameSceneSetup
    {
        private const string SCENE_PATH = "Assets/Scenes/Dev_MiniGame.unity";
        private static readonly Color _background = new Color(0.018f, 0.035f, 0.055f);
        private static readonly Color _surface = new Color(0.035f, 0.075f, 0.095f);
        private static readonly Color _green = new Color(0.25f, 0.95f, 0.72f);
        private static readonly Color _muted = new Color(0.48f, 0.65f, 0.70f);

        /// <summary>
        /// 열린 개발 씬의 Screen에 미니게임 화면과 분리된 피버 상태를 설치한다.
        /// Dev_MiniGame의 Screen, 키보드 및 카메라를 사용하여 월드 캔버스와 피버 상태 참조를 저장한다.
        /// </summary>
        [MenuItem("Tools/Mini Game/Configure Dev_MiniGame")]
        public static void Configure()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SCENE_PATH || Application.isPlaying)
                throw new InvalidOperationException("Open Dev_MiniGame in Edit Mode before configuring.");
            if (GameObject.Find("MiniGameSystem") != null)
                throw new InvalidOperationException("MiniGameSystem is already configured. Existing UI was preserved.");

            Transform screen = GameObject.Find("Monitor/Screen").transform;
            KeyboardInputController keyboard = UnityEngine.Object.FindFirstObjectByType<KeyboardInputController>();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure Dev_MiniGame");
            GameObject system = new GameObject("MiniGameSystem");
            Undo.RegisterCreatedObjectUndo(system, "Create mini game system");
            MiniGameController controller = system.AddComponent<MiniGameController>();
            SetReference(controller, "_keyboard", keyboard);
            FeverController fever = system.AddComponent<FeverController>();
            GameSession session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
            FeverHudSceneSetup.ConnectState(controller, fever, session);
            KeyboardAttackController attack = UnityEngine.Object.FindFirstObjectByType<KeyboardAttackController>();
            if (attack != null) SetReference(attack, "_feverController", fever);

            RectTransform world = CreateRect("MiniGameScreen", screen, Vector2.zero, new Vector2(960f, 600f));
            Undo.RegisterCreatedObjectUndo(world.gameObject, "Create monitor UI");
            world.localPosition = new Vector3(0f, 0f, -0.508f);
            world.localScale = new Vector3(2.82f / 960f / screen.lossyScale.x, 1.82f / 600f / screen.lossyScale.y, 0.003f);
            Canvas canvas = world.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 1;
            world.gameObject.AddComponent<CanvasScaler>();
            CreateImage("Background", world, Vector2.zero, new Vector2(960f, 600f), _background);
            CreateImage("TopAccent", world, new Vector2(0f, 294f), new Vector2(960f, 6f), _green);
            CreateText("Brand", world, new Vector2(-210f, 250f), new Vector2(450f, 44f), "KEY//OS", 38, _green, TextAnchor.MiddleLeft);
            CreateText("TerminalId", world, new Vector2(265f, 250f), new Vector2(340f, 44f), "SECURITY TERMINAL / 01", 20, _muted, TextAnchor.MiddleRight);
            Text status = CreateText("Status", world, new Vector2(0f, 198f), new Vector2(860f, 36f), "SYSTEM ONLINE / STANDBY", 22, _muted);
            CreateImage("Divider", world, new Vector2(0f, 166f), new Vector2(860f, 2f), _surface);

            RectTransform idle = CreateRect("Standby", world, new Vector2(0f, -8f), new Vector2(860f, 330f));
            CreateText("Icon", idle, new Vector2(0f, 100f), new Vector2(600f, 60f), "[ + ]", 54, _green);
            Text idleTitle = CreateText("Title", idle, new Vector2(0f, 20f), new Vector2(800f, 64f), "SYSTEM SECURE", 46, _green);
            Text idleDetails = CreateText("Details", idle, new Vector2(0f, -82f), new Vector2(820f, 100f), "Next event in 15s\nComplete events to restore security.", 26, _muted);

            RectTransform hacking = CreateRect("Hacking", world, new Vector2(0f, -8f), new Vector2(860f, 330f));
            Color red = new Color(1f, 0.27f, 0.32f);
            CreateText("Alert", hacking, new Vector2(0f, 125f), new Vector2(850f, 56f), "!  INTRUSION DETECTED", 40, red);
            CreateText("Help", hacking, new Vector2(0f, 73f), new Vector2(850f, 42f), "MASH ANY KEY / reach 100% / progress slowly decays", 24, _muted);
            Text percent = CreateText("Progress", hacking, new Vector2(0f, 26f), new Vector2(850f, 32f), "COUNTERMEASURE / 0%", 22, _green, TextAnchor.MiddleLeft);
            Image hackingFill = CreateBar("Countermeasure", hacking, new Vector2(0f, -8f), new Vector2(850f, 18f), _green);
            CreateImage("CommandBackground", hacking, new Vector2(0f, -100f), new Vector2(850f, 142f), _surface);
            Text log = CreateText("CommandLog", hacking, new Vector2(0f, -100f), new Vector2(810f, 128f), "> intrusion detected\n> awaiting countermeasures...", 23, _green, TextAnchor.UpperLeft);

            RectTransform keyMash = CreateRect("KeyMash", world, new Vector2(0f, -8f), new Vector2(860f, 330f));
            CreateText("Title", keyMash, new Vector2(0f, 128f), new Vector2(850f, 38f), "TARGET KEY / RAPID PRESS", 30, _green);
            CreateText("TargetLabel", keyMash, new Vector2(0f, 86f), new Vector2(800f, 30f), "MASH THIS KEY", 22, _muted);
            Text target = CreateText("TargetKey", keyMash, new Vector2(0f, 12f), new Vector2(600f, 76f), "SPACE", 60, _green);
            target.supportRichText = false;
            Text keyMashPercent = CreateText("Progress", keyMash, new Vector2(0f, -64f), new Vector2(850f, 32f), "CHARGE / 0%", 26, _green);
            Image keyMashFill = CreateBar("Charge", keyMash, new Vector2(0f, -98f), new Vector2(820f, 20f), _green);
            CreateText("Help", keyMash, new Vector2(0f, -146f), new Vector2(850f, 54f), "Reach 100% / progress decays when you stop\nOther keys ignored / release and press again", 22, _muted);
            hacking.gameObject.SetActive(false);
            keyMash.gameObject.SetActive(false);

            MiniGameScreenView view = system.AddComponent<MiniGameScreenView>();
            SetReference(view, "_controller", controller);
            SetReference(view, "_idlePanel", idle.gameObject);
            SetReference(view, "_hackingPanel", hacking.gameObject);
            SetReference(view, "_keyMashPanel", keyMash.gameObject);
            SetReference(view, "_status", status);
            SetReference(view, "_idleTitle", idleTitle);
            SetReference(view, "_idleDetails", idleDetails);
            SetReference(view, "_hackingFill", hackingFill);
            SetReference(view, "_hackingPercent", percent);
            SetReference(view, "_commandLog", log);
            SetReference(view, "_targetKey", target);
            SetReference(view, "_keyMashFill", keyMashFill);
            SetReference(view, "_keyMashPercent", keyMashPercent);
            if (session != null) WalletHudSceneSetup.Configure();
            FeverHudSceneSetup.ConfigureCurrentScene();

            FrameMonitorAndKeyboard();
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("MINIGAME_CONFIGURED WorldSpace=Monitor/Screen/MiniGameScreen Fever=MiniGameSystem Overlay=WalletHUDCanvas");
        }

        /// <summary>
        /// 캔버스 아래에 고정 크기 UI 사각형을 생성한다.
        /// name, parent, position, size로 부모와 중앙 기준 배치를 설정한 RectTransform을 반환한다.
        /// </summary>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = rect.anchorMin;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>
        /// 지정 영역에 배경 또는 구분선 이미지를 생성한다.
        /// 이름, 부모, 위치, 크기와 색상을 적용한 비상호작용 Image를 반환한다.
        /// </summary>
        private static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            Image image = CreateRect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 텍스트 UI를 생성하여 지정한 모양으로 배치한다.
        /// 내용, 글자 크기, 색상, 정렬과 영역을 사용하여 설정한 Text를 반환한다.
        /// </summary>
        private static Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, string content, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            Text text = CreateRect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.text = content;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 왼쪽부터 채워지는 수평 게이지와 배경을 생성한다.
        /// 부모, 위치, 크기 및 색상을 사용하여 fillAmount로 제어할 Image를 반환한다.
        /// </summary>
        private static Image CreateBar(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            Image background = CreateImage(name, parent, position, size, _surface);
            Image fill = CreateImage("Fill", background.transform, Vector2.zero, size, color);
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            return fill;
        }

        /// <summary>
        /// 컴포넌트의 직렬화된 참조를 설정한다.
        /// target, fieldName과 value를 사용하여 Inspector 참조를 적용한다.
        /// </summary>
        private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 개발 씬에서 모니터 전체와 난타 키보드가 보이도록 카메라를 배치한다.
        /// 기존 두 Cinemachine 카메라와 Main Camera의 위치, 시야각 및 초기 틸트를 변경한다.
        /// </summary>
        private static void FrameMonitorAndKeyboard()
        {
            foreach (CinemachineCamera camera in UnityEngine.Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform }, "Frame mini game screen");
                camera.transform.position = new Vector3(0f, 2f, -3.2f);
                camera.transform.rotation = Quaternion.Euler(17f, 0f, 0f);
                camera.Lens.FieldOfView = 50f;
                CinemachinePanTilt panTilt = camera.GetComponent<CinemachinePanTilt>();
                if (panTilt != null)
                {
                    Undo.RecordObject(panTilt, "Set initial monitor angle");
                    panTilt.TiltAxis.Value = 17f;
                }
            }
            Camera main = Camera.main;
            Undo.RecordObjects(new UnityEngine.Object[] { main, main.transform }, "Frame main camera");
            main.transform.SetPositionAndRotation(new Vector3(0f, 2f, -3.2f), Quaternion.Euler(17f, 0f, 0f));
            main.fieldOfView = 50f;
        }
    }
}
