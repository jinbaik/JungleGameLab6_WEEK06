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
        /// canvasObject의 피버 패널을 간소화하고 중앙 상단 활성 HUD를 구성한다.
        /// 기존 표시 객체와 상태 참조를 보존하며 삭제한 안내를 정리하고 새 게이지 및 배율 참조를 연결한다.
        /// </summary>
        public static void AddFeverPanel(GameObject canvasObject)
        {
            FeverHudView view = canvasObject.GetComponentInChildren<FeverHudView>(true);
            if (view == null)
            {
                foreach (string name in new[] { "WalletPanel", "HUDToastLayer" })
                {
                    RectTransform existing = (RectTransform)canvasObject.transform.Find(name);
                    existing.anchoredPosition = new Vector2(-256f, existing.anchoredPosition.y);
                }
            }

            RectTransform panel = GetOrCreateRect("FeverPanel", canvasObject.transform, new Vector2(-30f, -40f), new Vector2(96f, 440f));
            foreach (string name in new[] { "Charge", "Remaining", "PauseGuide" })
            {
                Transform obsolete = panel.Find(name);
                if (obsolete != null) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
            }
            AddImage(panel, _background);
            RectTransform accent = GetOrCreateRect("Accent", panel, Vector2.zero, new Vector2(96f, 3f));
            AddImage(accent, _green);
            Text title = CreateText("Status", panel, new Vector2(-12f, -12f), new Vector2(72f, 28f), "FEVER", 18, _green);
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleCenter;

            RectTransform gaugeRoot = GetOrCreateRect("VerticalGauge", panel, new Vector2(-31f, -52f), new Vector2(34f, 320f));
            Slider gauge = ConfigureGauge(gaugeRoot, Slider.Direction.BottomToTop, _green);

            Text multiplier = CreateText("Multiplier", panel, new Vector2(-12f, -384f), new Vector2(72f, 36f), "1.0", 24, Color.white);
            multiplier.alignment = TextAnchor.MiddleCenter;

            Color activeColor = new Color(1f, 0.56f, 0.16f);
            RectTransform activeRoot = GetOrCreateRect("ActiveFeverHUD", canvasObject.transform, new Vector2(0f, -32f), new Vector2(0f, 224f));
            activeRoot.anchorMin = new Vector2(0.25f, 1f);
            activeRoot.anchorMax = new Vector2(0.75f, 1f);
            activeRoot.pivot = new Vector2(0.5f, 1f);
            activeRoot.anchoredPosition = new Vector2(0f, -32f);
            activeRoot.sizeDelta = new Vector2(0f, 224f);

            Text activeTitle = CreateText("Title", activeRoot, Vector2.zero, Vector2.zero, "FEVER-TIME!", 56, activeColor);
            ConfigureCenteredRow(activeTitle.rectTransform, 0f, 72f);
            activeTitle.fontStyle = FontStyle.Bold;
            activeTitle.alignment = TextAnchor.MiddleCenter;
            AddOutline(activeTitle);

            RectTransform remainingRoot = GetOrCreateRect("RemainingGauge", activeRoot, Vector2.zero, Vector2.zero);
            ConfigureCenteredRow(remainingRoot, 84f, 20f);
            Slider remainingGauge = ConfigureGauge(remainingRoot, Slider.Direction.LeftToRight, activeColor);

            Text activeMultiplier = CreateText("DamageMultiplier", activeRoot, Vector2.zero, Vector2.zero,
                "DAMAGE X 2.0", 48, new Color(1f, 0.85f, 0.25f));
            ConfigureCenteredRow(activeMultiplier.rectTransform, 128f, 64f);
            activeMultiplier.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            activeMultiplier.rectTransform.anchoredPosition = new Vector2(0f, -160f);
            activeMultiplier.fontStyle = FontStyle.Bold;
            activeMultiplier.alignment = TextAnchor.MiddleCenter;
            AddOutline(activeMultiplier);

            if (view == null) view = panel.gameObject.AddComponent<FeverHudView>();
            SetReference(view, "_gauge", gauge);
            SetReference(view, "_status", title);
            SetReference(view, "_multiplier", multiplier);
            SetReference(view, "_activeFeverRoot", activeRoot.gameObject);
            SetReference(view, "_remainingGauge", remainingGauge);
            SetReference(view, "_activeMultiplier", activeMultiplier);
            activeRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// root의 기존 이미지와 Slider를 재사용하거나 추가하여 표시 전용 게이지를 구성한다.
        /// direction과 color를 적용하고 입력·탐색을 끈 0부터 1까지의 Slider를 반환한다.
        /// </summary>
        private static Slider ConfigureGauge(RectTransform root, Slider.Direction direction, Color color)
        {
            AddImage(root, _surface);
            RectTransform fill = GetOrCreateRect("Fill", root, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            Image fillImage = AddImage(fill, color);
            Slider gauge = root.GetComponent<Slider>();
            if (gauge == null) gauge = root.gameObject.AddComponent<Slider>();
            gauge.fillRect = fill;
            gauge.targetGraphic = fillImage;
            gauge.handleRect = null;
            gauge.direction = direction;
            gauge.minValue = 0f;
            gauge.maxValue = 1f;
            gauge.wholeNumbers = false;
            gauge.interactable = false;
            gauge.transition = Selectable.Transition.None;
            gauge.navigation = new Navigation { mode = Navigation.Mode.None };
            gauge.SetValueWithoutNotify(0f);
            return gauge;
        }

        /// <summary>
        /// rect를 부모 너비에 맞춘 중앙 정렬 행으로 배치한다.
        /// top과 height로 상단 여백 및 높이를 설정하며 가로 크기는 부모에 따라 변한다.
        /// </summary>
        private static void ConfigureCenteredRow(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>
        /// text에 기존 외곽선을 재사용하거나 추가하여 피버 강조 문구를 배경과 구분한다.
        /// 검은색 외곽선과 두 픽셀 간격을 설정하며 텍스트 내용은 변경하지 않는다.
        /// </summary>
        private static void AddOutline(Text text)
        {
            Outline outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;
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
        /// parent의 name 자식을 재사용하거나 생성하고 위치·크기를 구성한다.
        /// position과 size를 적용한 우측 상단 기준 RectTransform을 반환하며 기존 객체 식별자를 보존한다.
        /// </summary>
        private static RectTransform GetOrCreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = (RectTransform)parent.Find(name);
            if (rect == null) return CreateRect(name, parent, position, size);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>
        /// rect의 기존 배경 이미지를 재사용하거나 추가한다.
        /// color를 적용하고 레이캐스트를 끈 Image를 반환한다.
        /// </summary>
        private static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.GetComponent<Image>();
            if (image == null) image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// parent의 지정 영역에 피버 정보 텍스트를 생성하거나 기존 객체를 갱신한다.
        /// content, fontSize, color와 위치 및 크기를 적용한 비상호작용 Text를 반환한다.
        /// </summary>
        private static Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, string content, int fontSize, Color color)
        {
            RectTransform rect = GetOrCreateRect(name, parent, position, size);
            Text text = rect.GetComponent<Text>();
            if (text == null) text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.resizeTextForBestFit = false;
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
