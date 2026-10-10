using System;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Session;
using Game.UI;

namespace KeyboardModeling.Editor
{
    public static class FeverHudSceneSetup
    {
        private const string HUD_PATH = "Assets/Resource/Prefabs/UI/WalletHUDCanvas.prefab";
        private const string MINI_PATH = "Assets/Resource/Prefabs/MiniGame/MiniGameScreen.prefab";
        private const string MONITOR_PATH = "Assets/Resource/Prefabs/Monitor/Monitor_16x9_ScaleUp.prefab";

        private static readonly Color _background = new Color(0.018f, 0.035f, 0.055f, 0.94f);
        private static readonly Color _surface = new Color(0.035f, 0.075f, 0.095f);
        private static readonly Color _green = new Color(0.25f, 0.95f, 0.72f);
        private static readonly Color _muted = new Color(0.48f, 0.65f, 0.70f);

        /// <summary>
        /// 기존 미니게임 및 모니터 프리팹을 분리된 피버 상태와 Overlay HUD 구성으로 갱신한다.
        /// 설정과 참조를 복사하여 모니터 Canvas 밖에 상태 컴포넌트를 두고 프리팹을 저장한다.
        /// </summary>
        [MenuItem("Tools/Mini Game/Configure Fever HUD Prefabs")]
        public static void ConfigurePrefabs()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Configure Fever HUD in Edit Mode.");

            GameObject miniRoot = PrefabUtility.LoadPrefabContents(MINI_PATH);
            try
            {
                MiniGameController mini = miniRoot.GetComponent<MiniGameController>();
                FeverController fever = miniRoot.GetComponent<FeverController>();
                if (fever == null) fever = miniRoot.AddComponent<FeverController>();
                ConnectState(mini, fever, null);
                foreach (string name in new[] { "FeverDivider", "FeverStatus", "FeverCharge" })
                {
                    Transform child = miniRoot.transform.Find(name);
                    if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                MiniGameScreenView view = miniRoot.GetComponent<MiniGameScreenView>();
                SerializedObject viewData = new SerializedObject(view);
                Text details = (Text)viewData.FindProperty("_idleDetails").objectReferenceValue;
                details.text = "Next event waiting.\nSecurity system standing by.";
                PrefabUtility.SaveAsPrefabAsset(miniRoot, MINI_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(miniRoot);
            }

            GameObject monitorRoot = PrefabUtility.LoadPrefabContents(MONITOR_PATH);
            try
            {
                MiniGameController mini = monitorRoot.GetComponentInChildren<MiniGameController>(true);
                if (mini.GetComponent<Canvas>() != null)
                {
                    MiniGameController previous = mini;
                    FeverController previousFever = previous.GetComponent<FeverController>();
                    GameObject system = new GameObject("MiniGameSystem");
                    system.transform.SetParent(previous.transform.parent, false);
                    mini = system.AddComponent<MiniGameController>();
                    EditorUtility.CopySerialized(previous, mini);
                    FeverController fever = system.AddComponent<FeverController>();
                    ConnectState(mini, fever, monitorRoot.GetComponentInChildren<GameSession>(true));
                    ReplaceReferences(monitorRoot, previous, mini);
                    ReplaceReferences(monitorRoot, previousFever, fever);
                    UnityEngine.Object.DestroyImmediate(previousFever);
                    UnityEngine.Object.DestroyImmediate(previous);
                }
                foreach (KeyboardAttackController attack in monitorRoot.GetComponentsInChildren<KeyboardAttackController>(true))
                    SetReference(attack, "_feverController", mini.GetComponent<FeverController>());
                RemoveScreenStateOverrides(monitorRoot.GetComponentInChildren<MiniGameScreenView>(true));
                PrefabUtility.SaveAsPrefabAsset(monitorRoot, MONITOR_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(monitorRoot);
            }

            GameObject hudRoot = PrefabUtility.LoadPrefabContents(HUD_PATH);
            try
            {
                AddFeverPanel(hudRoot);
                PrefabUtility.SaveAsPrefabAsset(hudRoot, HUD_PATH);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(hudRoot);
            }
        }

        /// <summary>
        /// 현재 씬의 피버 상태를 공격 처리와 Wallet HUD에 연결한다.
        /// 씬에 피버가 하나 있을 때 비활성 객체까지 찾아 참조를 저장하며, 피버가 없으면 변경하지 않는다.
        /// </summary>
        [MenuItem("Tools/Mini Game/Connect Fever HUD In Current Scene")]
        public static void ConfigureCurrentScene()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Connect Fever HUD in Edit Mode.");
            Scene scene = SceneManager.GetActiveScene();
            FeverController fever = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (FeverController candidate in root.GetComponentsInChildren<FeverController>(true))
                {
                    if (fever != null) throw new InvalidOperationException("Select a FeverController explicitly in scenes with multiple systems.");
                    fever = candidate;
                }
            }
            if (fever == null) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (KeyboardAttackController attack in root.GetComponentsInChildren<KeyboardAttackController>(true))
                    SetReference(attack, "_feverController", fever);
                foreach (FeverHudView view in root.GetComponentsInChildren<FeverHudView>(true))
                    SetReference(view, "_controller", fever);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// mini와 fever의 C# 이벤트 구독 대상을 서로 연결하고 선택적 session을 피버에 저장한다.
        /// 입력 대상과 미니게임 설정은 유지하며 직렬화된 상태 참조를 변경한다.
        /// </summary>
        public static void ConnectState(MiniGameController mini, FeverController fever, GameSession session)
        {
            SetReference(mini, "_feverController", fever);
            SetReference(fever, "_miniGameController", mini);
            SetReference(fever, "_gameSession", session);
        }

        /// <summary>
        /// canvasObject 우측에 피버 전용 세로 게이지와 정보를 추가한다.
        /// 기존 지갑과 알림을 왼쪽으로 이동하여 겹침을 피하고, 이미 패널이 있으면 기존 구성을 유지한다.
        /// </summary>
        public static void AddFeverPanel(GameObject canvasObject)
        {
            if (canvasObject.GetComponentInChildren<FeverHudView>(true) != null) return;
            foreach (string name in new[] { "WalletPanel", "HUDToastLayer" })
            {
                RectTransform existing = (RectTransform)canvasObject.transform.Find(name);
                existing.anchoredPosition = new Vector2(-256f, existing.anchoredPosition.y);
            }

            RectTransform panel = CreateRect("FeverPanel", canvasObject.transform, new Vector2(-32f, -32f), new Vector2(200f, 440f));
            AddImage(panel, _background);
            RectTransform accent = CreateRect("Accent", panel, Vector2.zero, new Vector2(200f, 3f));
            AddImage(accent, _green);
            Text title = CreateText("Status", panel, new Vector2(-12f, -14f), new Vector2(176f, 28f), "FEVER CHARGE", 18, _green);
            title.fontStyle = FontStyle.Bold;

            RectTransform gaugeRoot = CreateRect("VerticalGauge", panel, new Vector2(-148f, -58f), new Vector2(34f, 360f));
            AddImage(gaugeRoot, _surface);
            RectTransform fill = CreateRect("Fill", gaugeRoot, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            Image fillImage = AddImage(fill, _green);
            Slider gauge = gaugeRoot.gameObject.AddComponent<Slider>();
            gauge.fillRect = fill;
            gauge.targetGraphic = fillImage;
            gauge.direction = Slider.Direction.BottomToTop;
            gauge.minValue = 0f;
            gauge.maxValue = 1f;
            gauge.interactable = false;
            gauge.transition = Selectable.Transition.None;
            gauge.navigation = new Navigation { mode = Navigation.Mode.None };
            gauge.SetValueWithoutNotify(0f);

            Text charge = CreateText("Charge", panel, new Vector2(-12f, -56f), new Vector2(124f, 66f), "CHARGE\n0%", 24, Color.white);
            Text remaining = CreateText("Remaining", panel, new Vector2(-12f, -140f), new Vector2(124f, 66f), "TIME LEFT\n0.0s", 24, Color.white);
            Text multiplier = CreateText("Multiplier", panel, new Vector2(-12f, -224f), new Vector2(124f, 66f), "DAMAGE\nx1.0", 24, Color.white);
            Text pause = CreateText("PauseGuide", panel, new Vector2(-12f, -320f), new Vector2(124f, 94f), "+50% per clear\nTwo clears to start.", 17, _muted);
            FeverHudView view = panel.gameObject.AddComponent<FeverHudView>();
            SetReference(view, "_gauge", gauge);
            SetReference(view, "_status", title);
            SetReference(view, "_charge", charge);
            SetReference(view, "_remaining", remaining);
            SetReference(view, "_multiplier", multiplier);
            SetReference(view, "_pauseGuide", pause);
        }

        /// <summary>
        /// root 아래 컴포넌트에서 previous를 참조하는 직렬화 필드를 replacement로 교체한다.
        /// 프리팹 내 화면과 공격의 참조를 새 상태 소유자로 연결하고 변경을 기록한다.
        /// </summary>
        private static void ReplaceReferences(GameObject root, UnityEngine.Object previous, UnityEngine.Object replacement)
        {
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                SerializedObject data = new SerializedObject(component);
                SerializedProperty property = data.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == previous)
                        property.objectReferenceValue = replacement;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        /// <summary>
        /// view의 중첩 프리팹에서 제거된 상태 컴포넌트의 기존 오버라이드를 정리한다.
        /// 화면과 배치 설정은 유지하고 MiniGameController 및 FeverController 참조 오버라이드만 제거한다.
        /// </summary>
        private static void RemoveScreenStateOverrides(MiniGameScreenView view)
        {
            GameObject instance = PrefabUtility.GetNearestPrefabInstanceRoot(view);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instance);
            System.Collections.Generic.List<PropertyModification> retained = new System.Collections.Generic.List<PropertyModification>();
            foreach (PropertyModification modification in modifications)
                if (!(modification.target is MiniGameController) && !(modification.target is FeverController))
                    retained.Add(modification);
            PrefabUtility.SetPropertyModifications(instance, retained.ToArray());
        }

        /// <summary>
        /// parent 아래에 우측 상단 기준의 UI 사각형을 생성한다.
        /// name, position, size를 적용한 RectTransform을 반환한다.
        /// </summary>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>
        /// rect에 비상호작용 배경 이미지를 추가한다.
        /// color를 적용한 Image를 반환한다.
        /// </summary>
        private static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// parent의 지정 영역에 피버 정보 텍스트를 생성한다.
        /// content, fontSize, color와 위치 및 크기를 적용한 비상호작용 Text를 반환한다.
        /// </summary>
        private static Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, string content, int fontSize, Color color)
        {
            Text text = CreateRect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.text = content;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// target의 fieldName에 value를 직렬화된 참조로 저장한다.
        /// Inspector 값과 프리팹 인스턴스 변경 기록을 갱신한다.
        /// </summary>
        private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            SerializedObject data = new SerializedObject(target);
            data.FindProperty(fieldName).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
