using System;

using UnityEngine;
using UnityEngine.UI;

using UnityEditor;

using Game.Session;

public static class MonitorSettingsSetup
{
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Monitor/Monitor_16x9_ScaleUp.prefab";

    /// <summary>
    /// 모니터 프리팹에 설정 앱과 종료 확인 UI를 구성한다.
    /// 기존 앱과 씬 상태를 보존하며 설정 참조를 연결한 프리팹만 저장한다.
    /// </summary>
    [MenuItem("Tools/Monitor/Configure Settings App")]
    public static void ConfigurePrefab()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Mode에서 설정 앱을 구성합니다.");
        GameObject prefab = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        try
        {
            MonitorController monitor = prefab.GetComponentInChildren<MonitorController>(true);
            RectTransform desktop = (RectTransform)monitor.transform.Find("DesktopCanvas");
            MonitorSettingsAppView view = desktop.GetComponentInChildren<MonitorSettingsAppView>(true);
            if (view == null) view = CreateApp(desktop);
            SetReference(view, "_gameSession", monitor.GetComponent<GameSession>());
            SetReference(monitor, "_settingsAppView", view);
            PrefabUtility.SaveAsPrefabAsset(prefab, PREFAB_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    /// <summary>
    /// desktop의 폰트와 월드 캔버스로 설정 아이콘 및 두 단계 팝업을 구성한다.
    /// 슬라이더와 버튼 참조를 연결한 MonitorSettingsAppView를 반환한다.
    /// </summary>
    private static MonitorSettingsAppView CreateApp(RectTransform desktop)
    {
        Font font = desktop.Find("ShopApp/Name").GetComponent<Text>().font;
        RectTransform app = CreateRect("SettingsApp", desktop, new Vector2(150f, 172f), Vector2.zero);
        app.anchorMin = app.anchorMax = app.pivot = new Vector2(0f, 1f);
        app.anchoredPosition = new Vector2(20f, -440f);
        AddImage(app, Color.clear, true);
        RectTransform icon = CreateRect("Icon", app, new Vector2(112f, 84f), new Vector2(0f, 18f));
        AddImage(icon, new Color(0.12f, 0.24f, 0.38f), false);
        for (int row = 0; row < 3; row++)
        {
            float y = 24f - row * 24f;
            AddImage(CreateRect($"Track_{row}", icon, new Vector2(76f, 6f), new Vector2(0f, y)),
                new Color(0.55f, 0.72f, 0.85f), false);
            AddImage(CreateRect($"Knob_{row}", icon, new Vector2(14f, 18f), new Vector2(row == 1 ? 20f : -16f, y)),
                new Color(0.3f, 0.72f, 1f), false);
        }
        Text name = CreateText("Name", app, new Vector2(150f, 38f), new Vector2(0f, -61f), "설정", font, 24);
        Shadow shadow = name.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform popup = CreateStretch("SettingsPopup", desktop);
        Canvas canvas = popup.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 3;
        canvas.worldCamera = desktop.GetComponent<Canvas>().worldCamera;
        popup.gameObject.AddComponent<GraphicRaycaster>();
        CanvasGroup popupGroup = popup.gameObject.AddComponent<CanvasGroup>();
        popupGroup.alpha = 0f;
        popupGroup.interactable = false;
        popupGroup.blocksRaycasts = false;
        popupGroup.ignoreParentGroups = true;
        AddImage(popup, new Color(0f, 0f, 0f, 0.45f), true);

        RectTransform window = CreateRect("Window", popup, new Vector2(760f, 500f), Vector2.zero);
        CanvasGroup windowGroup = window.gameObject.AddComponent<CanvasGroup>();
        AddImage(window, new Color(0.06f, 0.1f, 0.16f), true);
        RectTransform titleBar = CreateRect("TitleBar", window, new Vector2(760f, 72f), new Vector2(0f, 214f));
        AddImage(titleBar, new Color(0.1f, 0.17f, 0.26f), false);
        CreateText("Title", titleBar, new Vector2(600f, 58f), new Vector2(-30f, 0f), "설정", font, 32).alignment = TextAnchor.MiddleLeft;
        MonitorSettingsButton close = CreateButton("Close", titleBar, new Vector2(52f, 48f), new Vector2(336f, 0f), "X", font, windowGroup, false);

        CreateText("SfxLabel", window, new Vector2(500f, 44f), new Vector2(-76f, 112f), "효과음", font, 28).alignment = TextAnchor.MiddleLeft;
        Text sfxPercent = CreateText("SfxPercentage", window, new Vector2(110f, 44f), new Vector2(272f, 112f), "100%", font, 26);
        MonitorSettingsSlider sfx = CreateSlider("SfxSlider", window, new Vector2(0f, 54f));
        CreateText("ShakeLabel", window, new Vector2(500f, 44f), new Vector2(-76f, -26f), "카메라 흔들림", font, 28).alignment = TextAnchor.MiddleLeft;
        Text shakePercent = CreateText("ShakePercentage", window, new Vector2(110f, 44f), new Vector2(272f, -26f), "100%", font, 26);
        MonitorSettingsSlider shake = CreateSlider("ShakeSlider", window, new Vector2(0f, -84f));
        MonitorSettingsButton quit = CreateButton("Quit", window, new Vector2(252f, 58f), new Vector2(0f, -184f), "게임 종료", font, windowGroup, true);

        RectTransform confirmation = CreateStretch("QuitConfirmation", popup);
        CanvasGroup confirmationGroup = confirmation.gameObject.AddComponent<CanvasGroup>();
        confirmationGroup.alpha = 0f;
        confirmationGroup.interactable = false;
        confirmationGroup.blocksRaycasts = false;
        AddImage(confirmation, new Color(0f, 0f, 0f, 0.65f), true);
        RectTransform dialog = CreateRect("Dialog", confirmation, new Vector2(600f, 276f), Vector2.zero);
        AddImage(dialog, new Color(0.08f, 0.13f, 0.2f), true);
        CreateText("Title", dialog, new Vector2(540f, 60f), new Vector2(0f, 76f), "게임을 종료하시겠습니까?", font, 32);
        CreateText("Description", dialog, new Vector2(540f, 48f), new Vector2(0f, 8f), "설정은 다음 실행에도 유지됩니다.", font, 24);
        MonitorSettingsButton cancel = CreateButton("Cancel", dialog, new Vector2(220f, 58f), new Vector2(-126f, -78f), "취소", font, confirmationGroup, false);
        MonitorSettingsButton confirm = CreateButton("Confirm", dialog, new Vector2(220f, 58f), new Vector2(126f, -78f), "종료", font, confirmationGroup, true);

        MonitorSettingsAppView view = app.gameObject.AddComponent<MonitorSettingsAppView>();
        SetReference(view, "_popupCanvasGroup", popupGroup);
        SetReference(view, "_windowCanvasGroup", windowGroup);
        SetReference(view, "_quitConfirmationCanvasGroup", confirmationGroup);
        SetReference(view, "_closeButton", close);
        SetReference(view, "_quitButton", quit);
        SetReference(view, "_confirmQuitButton", confirm);
        SetReference(view, "_cancelQuitButton", cancel);
        SetReference(view, "_sfxSlider", sfx);
        SetReference(view, "_shakeSlider", shake);
        SetReference(view, "_sfxPercentage", sfxPercent);
        SetReference(view, "_shakePercentage", shakePercent);
        return view;
    }

    /// <summary>
    /// parent 아래에 name, size와 position을 적용한 UI 사각형을 생성한다.
    /// 부모 레이어와 중앙 기준 좌표를 사용하는 RectTransform을 반환한다.
    /// </summary>
    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject element = new GameObject(name, typeof(RectTransform));
        element.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)element.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    /// <summary>
    /// parent의 전체 영역을 채우는 name UI 사각형을 생성한다.
    /// 모달 입력 차단과 슬라이더 채움에 사용할 RectTransform을 반환한다.
    /// </summary>
    private static RectTransform CreateStretch(string name, Transform parent)
    {
        RectTransform rect = CreateRect(name, parent, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    /// <summary>
    /// rect에 color와 raycastTarget을 적용한 이미지를 추가한다.
    /// 화면 표시와 클릭 영역으로 사용할 Image를 반환한다.
    /// </summary>
    private static Image AddImage(RectTransform rect, Color color, bool raycastTarget)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    /// <summary>
    /// name, parent, 크기, 위치와 폰트 설정으로 텍스트를 생성한다.
    /// value를 표시하고 클릭을 통과하는 Text를 반환한다.
    /// </summary>
    private static Text CreateText(string name, Transform parent, Vector2 size, Vector2 position, string value, Font font, int fontSize)
    {
        Text text = CreateRect(name, parent, size, position).gameObject.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// name, parent, 크기와 위치로 설정 버튼과 label을 생성한다.
    /// danger 색상 및 입력 그룹을 적용한 C# 클릭 이벤트 버튼을 반환한다.
    /// </summary>
    private static MonitorSettingsButton CreateButton(string name, Transform parent, Vector2 size, Vector2 position, string label, Font font, CanvasGroup input, bool danger)
    {
        RectTransform rect = CreateRect(name, parent, size, position);
        AddImage(rect, danger ? new Color(0.65f, 0.19f, 0.22f) : new Color(0.2f, 0.29f, 0.39f), true);
        CreateText("Label", rect, size, Vector2.zero, label, font, 26);
        MonitorSettingsButton button = rect.gameObject.AddComponent<MonitorSettingsButton>();
        SetReference(button, "_inputCanvasGroup", input);
        return button;
    }

    /// <summary>
    /// parent 아래 position에 name 슬라이더의 트랙과 핸들을 구성한다.
    /// 0부터 100까지 정수 비율을 전달하는 MonitorSettingsSlider를 반환한다.
    /// </summary>
    private static MonitorSettingsSlider CreateSlider(string name, Transform parent, Vector2 position)
    {
        RectTransform rect = CreateRect(name, parent, new Vector2(648f, 48f), position);
        AddImage(rect, Color.clear, true);
        RectTransform track = CreateRect("Track", rect, new Vector2(620f, 10f), Vector2.zero);
        AddImage(track, new Color(0.17f, 0.25f, 0.34f), false);
        RectTransform fill = CreateStretch("Fill", track);
        AddImage(fill, new Color(0.28f, 0.68f, 1f), false);
        RectTransform handleArea = CreateRect("HandleArea", rect, new Vector2(620f, 48f), Vector2.zero);
        RectTransform handle = CreateRect("Handle", handleArea, new Vector2(22f, 36f), Vector2.zero);
        Image handleImage = AddImage(handle, new Color(0.75f, 0.9f, 1f), true);
        MonitorSettingsSlider slider = rect.gameObject.AddComponent<MonitorSettingsSlider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        handle.sizeDelta = new Vector2(22f, -12f);
        slider.targetGraphic = handleImage;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        slider.SetValueWithoutNotify(100f);
        Navigation navigation = slider.navigation;
        navigation.mode = Navigation.Mode.None;
        slider.navigation = navigation;
        return slider;
    }

    /// <summary>
    /// target의 직렬화 필드 fieldName에 value를 연결한다.
    /// 개발자가 Inspector에서 추적할 수 있는 컴포넌트 참조를 저장한다.
    /// </summary>
    private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(fieldName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
