using System;

using UnityEngine;
using UnityEngine.UI;

using UnityEditor;
using UnityEditor.SceneManagement;

public static class KeyboardGoalAppSetup
{
    private const string PREFAB_PATH = "Assets/Resource/Prefabs/Monitor/Monitor_16x9_ScaleUp.prefab";

    /// <summary>
    /// 모니터 프리팹에 목표 앱 UI를 구성하고 현재 Main 씬의 진행 컴포넌트를 연결한다.
    /// 기존 프리팹과 씬 참조를 보존하며 변경한 프리팹 및 씬을 저장한다.
    /// </summary>
    [MenuItem("Tools/Monitor/Configure Keyboard Goal App")]
    public static void ConfigureCurrentScene()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Edit Mode에서 목표 앱을 구성합니다.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Legacy/Scene/Main.unity")
            throw new InvalidOperationException("Main 씬을 연 뒤 목표 앱을 구성합니다.");
        if (scene.isDirty)
            throw new InvalidOperationException("현재 씬의 변경을 저장한 뒤 목표 앱을 구성합니다.");

        GameObject prefab = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
        try
        {
            MonitorController monitor = prefab.GetComponentInChildren<MonitorController>(true);
            RectTransform desktop = (RectTransform)monitor.transform.Find("DesktopCanvas");
            KeyboardGoalAppView view = desktop.GetComponentInChildren<KeyboardGoalAppView>(true);
            if (view == null) view = CreateApp(desktop);
            SetReference(monitor, "_goalAppView", view);
            PrefabUtility.SaveAsPrefabAsset(prefab, PREFAB_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }

        MonitorController sceneMonitor = UnityEngine.Object.FindFirstObjectByType<MonitorController>();
        KeyboardCompletionProgress progress = UnityEngine.Object.FindFirstObjectByType<KeyboardCompletionProgress>();
        KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
        KeyboardGoalAppView sceneView = sceneMonitor.GetComponentInChildren<KeyboardGoalAppView>(true);
        Undo.RecordObject(sceneView, "Connect Keyboard Goal Progress");
        Undo.RecordObject(interaction, "Connect Keyboard Goal Progress");
        SetReference(sceneView, "_completionProgress", progress);
        SetReference(interaction, "_completionProgress", progress);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sceneView);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>
    /// desktop의 기존 폰트와 월드 캔버스를 사용해 목표 앱과 팝업을 생성한다.
    /// 아이콘, 진행 표시, 닫기 입력을 연결한 KeyboardGoalAppView를 반환한다.
    /// </summary>
    private static KeyboardGoalAppView CreateApp(RectTransform desktop)
    {
        Font font = desktop.Find("ShopApp/Name").GetComponent<Text>().font;
        RectTransform app = CreateRect("KeyboardGoalApp", desktop, new Vector2(150f, 172f), Vector2.zero);
        app.anchorMin = app.anchorMax = app.pivot = new Vector2(0f, 1f);
        app.anchoredPosition = new Vector2(214f, -34f);
        AddImage(app, Color.clear, true);
        RectTransform icon = CreateRect("Icon", app, new Vector2(112f, 84f), new Vector2(0f, 18f));
        AddImage(icon, new Color(0.12f, 0.24f, 0.38f), false);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                RectTransform key = CreateRect($"Key_{row}_{column}", icon, new Vector2(14f, 12f),
                    new Vector2(-36f + column * 18f, 25f - row * 17f));
                AddImage(key, new Color(0.72f, 0.88f, 1f), false);
            }
        }
        AddImage(CreateRect("Space", icon, new Vector2(62f, 10f), new Vector2(0f, -29f)),
            new Color(0.3f, 0.72f, 1f), false);
        Text name = CreateText("Name", app, new Vector2(150f, 38f), new Vector2(0f, -61f), "키보드 목표", font, 24);
        Shadow shadow = name.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        RectTransform popup = CreateRect("KeyboardGoalPopup", desktop, Vector2.zero, Vector2.zero);
        popup.anchorMin = Vector2.zero;
        popup.anchorMax = Vector2.one;
        popup.offsetMin = popup.offsetMax = Vector2.zero;
        Canvas popupCanvas = popup.gameObject.AddComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = 2;
        popupCanvas.worldCamera = desktop.GetComponent<Canvas>().worldCamera;
        popup.gameObject.AddComponent<GraphicRaycaster>();
        CanvasGroup group = popup.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        group.ignoreParentGroups = true;
        AddImage(popup, new Color(0f, 0f, 0f, 0.45f), true);

        RectTransform window = CreateRect("Window", popup, new Vector2(980f, 520f), Vector2.zero);
        AddImage(window, new Color(0.06f, 0.1f, 0.16f), true);
        RectTransform titleBar = CreateRect("TitleBar", window, new Vector2(980f, 72f), new Vector2(0f, 224f));
        AddImage(titleBar, new Color(0.1f, 0.17f, 0.26f), false);
        Text title = CreateText("Title", titleBar, new Vector2(780f, 58f), new Vector2(-55f, 0f), "키보드 목표", font, 32);
        title.alignment = TextAnchor.MiddleLeft;
        RectTransform close = CreateRect("Close", titleBar, new Vector2(52f, 48f), new Vector2(446f, 0f));
        AddImage(close, new Color(0.2f, 0.29f, 0.39f), true);
        CreateText("Label", close, new Vector2(52f, 48f), Vector2.zero, "X", font, 28);
        MonitorGoalCloseButton closeButton = close.gameObject.AddComponent<MonitorGoalCloseButton>();
        SetReference(closeButton, "_popupCanvasGroup", group);

        Text completed = CreateText("Completed", window, new Vector2(900f, 100f), new Vector2(0f, 94f),
            "파괴 완료 0 / 100", font, 58);
        Text remaining = CreateText("Remaining", window, new Vector2(900f, 64f), new Vector2(0f, 2f),
            "남은 목표 100개", font, 36);
        remaining.color = new Color(0.55f, 0.82f, 1f);
        RectTransform track = CreateRect("ProgressTrack", window, new Vector2(830f, 18f), new Vector2(0f, -70f));
        AddImage(track, new Color(0.17f, 0.25f, 0.34f), false);
        RectTransform fillRect = CreateRect("Fill", track, new Vector2(830f, 18f), Vector2.zero);
        Image fill = AddImage(fillRect, new Color(0.28f, 0.68f, 1f), false);
        fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0f;
        Text status = CreateText("Status", window, new Vector2(900f, 76f), new Vector2(0f, -164f),
            "초기 키보드를 파괴하면 금 키보드 3개를 받습니다.", font, 27);

        KeyboardGoalAppView view = app.gameObject.AddComponent<KeyboardGoalAppView>();
        SetReference(view, "_popupCanvasGroup", group);
        SetReference(view, "_completedText", completed);
        SetReference(view, "_remainingText", remaining);
        SetReference(view, "_statusText", status);
        SetReference(view, "_progressFill", fill);
        SetReference(view, "_closeButton", closeButton);
        return view;
    }

    /// <summary>
    /// name, parent, size와 position으로 중앙 정렬된 UI 사각형을 생성한다.
    /// 부모 레이어와 좌표계를 적용한 RectTransform을 반환한다.
    /// </summary>
    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    /// <summary>
    /// rect에 color와 raycastTarget으로 설정한 이미지를 연결한다.
    /// 화면 표시 및 클릭 영역으로 사용할 Image를 반환한다.
    /// </summary>
    private static Image AddImage(RectTransform rect, Color color, bool raycastTarget)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    /// <summary>
    /// 이름, 부모, 크기, 위치와 폰트 설정으로 UI 텍스트를 생성한다.
    /// 중앙 정렬과 클릭 통과를 적용한 Text를 반환한다.
    /// </summary>
    private static Text CreateText(string name, Transform parent, Vector2 size, Vector2 position,
        string value, Font font, int fontSize)
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
    /// target의 직렬화된 fieldName에 value 참조를 연결한다.
    /// 프리팹 또는 씬 컴포넌트의 Inspector 설정을 변경한다.
    /// </summary>
    private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(fieldName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
