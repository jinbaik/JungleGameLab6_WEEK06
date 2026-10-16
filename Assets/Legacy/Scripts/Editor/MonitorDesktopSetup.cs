using System;

using UnityEngine;
using UnityEngine.UI;

using Unity.Cinemachine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Game.Shop;
using KeyboardModeling;

public static class MonitorDesktopSetup
{
    private const string SPRITE_DIRECTORY = "Assets/Resource/Sprites/MonitorDesktop/";
    private const string UNDO_NAME = "Configure Monitor Desktop";

    /// <summary>
    /// 현재 씬의 모니터와 기존 상점 및 미니게임 UI로 바탕화면을 구성한다.
    /// 첨부 이미지 에셋과 기존 참조를 사용해 앱, 작업 표시줄, 입력 및 코인 연결을 저장한다.
    /// </summary>
    [MenuItem("Tools/Monitor/Configure Desktop")]
    public static void ConfigureCurrentScene()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Edit Mode에서 모니터 바탕화면을 구성합니다.");

        MonitorController monitor = UnityEngine.Object.FindFirstObjectByType<MonitorController>();
        KeyboardInteractionController interaction = UnityEngine.Object.FindFirstObjectByType<KeyboardInteractionController>();
        if (monitor == null || interaction == null)
            throw new InvalidOperationException("플레이용 모니터와 키보드 상호작용 컨트롤러가 필요합니다.");

        ShopView shop = GetReference<ShopView>(monitor, "_shopView");
        GameObject miniGameCanvas = GetReference<GameObject>(monitor, "_miniGameCanvas");
        MiniGameScreenView miniGameView = miniGameCanvas.GetComponent<MiniGameScreenView>();
        MiniGameController miniGame = GetReference<MiniGameController>(miniGameView, "_controller");
        Font font = GetReference<Text>(shop, "_balanceText").font;
        Sprite background = ImportSprite("DesktopBackground.png");
        Sprite shopIcon = ImportSprite("ShopIcon.png");
        Sprite windowsIcon = ImportSprite("WindowsIcon.png");
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UNDO_NAME);

        RectTransform template = miniGameCanvas.GetComponent<RectTransform>();
        FitCanvasToScreen(template, monitor.transform.parent);
        RectTransform desktop = ConfigureDesktop(monitor.transform, template, font, background, shopIcon, windowsIcon);
        Undo.RecordObject(desktop, UNDO_NAME);
        desktop.localScale = template.localScale;
        desktop.localPosition = template.localPosition;
        desktop.localRotation = template.localRotation;
        ConfigureShop(shop, template);
        ConfigureMiniGame(miniGameCanvas);
        ConfigureMonitorCamera(GetReference<CinemachineCamera>(monitor, "monitorCam"), desktop);

        SetReference(monitor, "_desktopView", desktop.GetComponentInChildren<MonitorDesktopView>(true));
        SetReference(monitor, "_shopCanvasGroup", shop.GetComponent<CanvasGroup>());
        SetReference(monitor, "_miniGameController", miniGame);
        SetReference(monitor, "_interactionController", interaction);
        SetReference(interaction, "_monitorController", monitor);
        SetReference(miniGame, "_monitorController", monitor);

        MonitorCoinRain coins = UnityEngine.Object.FindFirstObjectByType<MonitorCoinRain>();
        if (coins != null)
        {
            SetReference(coins, "_desktopRoot", desktop);
            SetReference(coins, "_background", desktop.Find("Background"));
        }

        Undo.RecordObject(miniGameCanvas, UNDO_NAME);
        miniGameCanvas.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(miniGameCanvas);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(monitor.gameObject.scene);
        EditorSceneManager.SaveScene(monitor.gameObject.scene);
        Debug.Log("MONITOR_DESKTOP_CONFIGURED " + monitor.gameObject.scene.path);
    }

    /// <summary>
    /// fileName의 PNG를 단일 UI Sprite로 가져온다.
    /// 필터와 알파 설정을 적용하고 프로젝트에 저장된 Sprite를 반환한다.
    /// </summary>
    private static Sprite ImportSprite(string fileName)
    {
        string path = SPRITE_DIRECTORY + fileName;
        AssetDatabase.ImportAsset(path);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new InvalidOperationException("모니터 이미지가 없습니다: " + path);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>
    /// parent와 template의 모니터 영역에 배경, 상점 앱과 장식 작업 표시줄을 배치한다.
    /// font와 세 Sprite를 적용하며 기존 바탕화면이 있으면 그대로 반환한다.
    /// </summary>
    private static RectTransform ConfigureDesktop(Transform parent, RectTransform template, Font font,
        Sprite background, Sprite shopIcon, Sprite windowsIcon)
    {
        Transform existing = parent.Find("DesktopCanvas");
        if (existing != null)
            return (RectTransform)existing;

        RectTransform desktop = CreateRect("DesktopCanvas", parent, template.rect.size);
        desktop.localPosition = template.localPosition;
        desktop.localRotation = template.localRotation;
        desktop.localScale = template.localScale;
        desktop.gameObject.layer = template.gameObject.layer;
        Canvas canvas = Undo.AddComponent<Canvas>(desktop.gameObject);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = template.GetComponent<Canvas>().worldCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 0;
        Undo.AddComponent<CanvasScaler>(desktop.gameObject);
        Undo.AddComponent<GraphicRaycaster>(desktop.gameObject);
        Undo.AddComponent<RectMask2D>(desktop.gameObject);
        CanvasGroup group = Undo.AddComponent<CanvasGroup>(desktop.gameObject);
        group.interactable = false;
        group.blocksRaycasts = false;

        Image backgroundImage = CreateImage("Background", desktop, Vector2.zero, Color.white, background);
        Stretch(backgroundImage.rectTransform);
        backgroundImage.raycastTarget = false;

        RectTransform app = CreateRect("ShopApp", desktop, new Vector2(150f, 172f));
        Place(app, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -34f), app.sizeDelta);
        Image appHitArea = Undo.AddComponent<Image>(app.gameObject);
        appHitArea.color = Color.clear;
        Image icon = CreateImage("Icon", app, new Vector2(112f, 112f), Color.white, shopIcon);
        icon.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        icon.preserveAspect = true;
        Text label = CreateText("Name", app, new Vector2(150f, 38f), "Shop", font, 26);
        label.rectTransform.anchoredPosition = new Vector2(0f, -61f);
        Shadow shadow = Undo.AddComponent<Shadow>(label.gameObject);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        MonitorDesktopView desktopView = Undo.AddComponent<MonitorDesktopView>(app.gameObject);
        SetReference(desktopView, "_desktopCanvasGroup", group);

        Image taskbar = CreateImage("Taskbar", desktop, new Vector2(0f, 64f), new Color(0.08f, 0.11f, 0.16f, 0.96f), null);
        taskbar.rectTransform.anchorMin = Vector2.zero;
        taskbar.rectTransform.anchorMax = new Vector2(1f, 0f);
        taskbar.rectTransform.pivot = new Vector2(0.5f, 0f);
        taskbar.raycastTarget = false;
        Image startIcon = CreateImage("WindowsIcon", taskbar.transform, new Vector2(38f, 38f), Color.white, windowsIcon);
        Place(startIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(36f, 0f), new Vector2(38f, 38f));
        startIcon.preserveAspect = true;
        startIcon.raycastTarget = false;
        return desktop;
    }

    /// <summary>
    /// shop의 기존 상품 목록을 유지하면서 template 안에 고정 상점 팝업을 배치한다.
    /// Page 전체를 열기 대상으로 연결하고 기존 버튼을 X 닫기 버튼으로 변경한다.
    /// </summary>
    private static void ConfigureShop(ShopView shop, RectTransform template)
    {
        Canvas canvas = shop.GetComponent<Canvas>();
        Undo.RecordObject(canvas, UNDO_NAME);
        canvas.overrideSorting = true;
        canvas.sortingOrder = 2;
        PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
        CanvasGroup group = shop.GetComponent<CanvasGroup>();
        if (group == null)
            group = Undo.AddComponent<CanvasGroup>(shop.gameObject);
        Undo.RecordObject(group, UNDO_NAME);
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        RectTransform root = (RectTransform)shop.transform;
        Undo.RecordObject(root, UNDO_NAME);
        root.localPosition = template.localPosition;
        root.localRotation = template.localRotation;
        root.localScale = template.localScale;
        root.sizeDelta = template.sizeDelta;
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);

        Transform blocker = shop.transform.Find("PopupBlocker");
        if (blocker == null)
        {
            Image image = CreateImage("PopupBlocker", shop.transform, Vector2.zero, new Color(0f, 0f, 0f, 0.2f), null);
            Stretch(image.rectTransform);
            image.transform.SetAsFirstSibling();
        }

        RectTransform page = (RectTransform)shop.transform.Find("Page");
        Place(page, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 870f));
        RectTransform panel = (RectTransform)page.Find("ShopPanel");
        Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -25f), new Vector2(608f, 720f));
        RectTransform heading = (RectTransform)panel.Find("Heading");
        Place(heading, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(608f, 70f));
        RectTransform viewport = (RectTransform)panel.Find("UpgradeViewport");
        Place(viewport, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -77f), new Vector2(608f, 600f));
        Text balance = GetReference<Text>(shop, "_balanceText");
        Place(balance.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(460f, 52f));
        Undo.RecordObject(balance, UNDO_NAME);
        balance.fontSize = 32;
        PrefabUtility.RecordPrefabInstancePropertyModifications(balance);
        Button close = GetReference<Button>(shop, "_toggleButton");
        Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(52f, 42f));
        Text closeText = GetReference<Text>(shop, "_toggleText");
        Stretch(closeText.rectTransform);
        Undo.RecordObject(closeText, UNDO_NAME);
        closeText.text = "X";
        PrefabUtility.RecordPrefabInstancePropertyModifications(closeText);
        Transform reward = page.Find("StoreDemo/DemoPage/RewardButton");
        if (reward != null)
            Place((RectTransform)reward, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-88f, -16f), new Vector2(104f, 38f));

        SetReference(shop, "_shopPanel", page.gameObject);
        SerializedObject serialized = new SerializedObject(shop);
        serialized.FindProperty("_useWindowCloseButton").boolValue = true;
        serialized.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(shop);
        Undo.RecordObject(page.gameObject, UNDO_NAME);
        page.gameObject.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(page.gameObject);
    }

    /// <summary>
    /// miniGameCanvas의 배경을 모니터 전체로 확장하고 기존 게임 내용을 중앙에 배치한다.
    /// 최초 구성에서만 내용 배율을 적용하며 최상위 팝업 정렬 순서를 저장한다.
    /// </summary>
    private static void ConfigureMiniGame(GameObject miniGameCanvas)
    {
        Canvas canvas = miniGameCanvas.GetComponent<Canvas>();
        bool isConfigured = canvas.overrideSorting && canvas.sortingOrder == 3;
        Undo.RecordObject(canvas, UNDO_NAME);
        canvas.overrideSorting = true;
        canvas.sortingOrder = 3;
        PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
        Stretch((RectTransform)miniGameCanvas.transform.Find("Background"));
        if (isConfigured)
            return;
        foreach (RectTransform child in miniGameCanvas.transform)
        {
            if (child.name == "Background")
                continue;
            Undo.RecordObject(child, UNDO_NAME);
            child.localScale *= 1.75f;
            child.anchoredPosition *= 1.75f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(child);
        }
    }

    /// <summary>
    /// display 안의 Screen 메시 크기로 canvas의 월드 표시 영역을 맞춘다.
    /// 실제 화면 크기와 부모 배율을 사용하여 테두리 안에 들어가는 UI 배율을 저장한다.
    /// </summary>
    private static void FitCanvasToScreen(RectTransform canvas, Transform display)
    {
        foreach (MeshFilter mesh in display.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.name != "Screen")
                continue;
            Vector3 screenSize = Vector3.Scale(mesh.sharedMesh.bounds.size, mesh.transform.lossyScale) * 0.98f;
            Vector3 parentScale = canvas.parent.lossyScale;
            Undo.RecordObject(canvas, UNDO_NAME);
            canvas.localScale = new Vector3(screenSize.x / canvas.rect.width / parentScale.x,
                screenSize.y / canvas.rect.height / parentScale.y, canvas.localScale.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
            return;
        }
        throw new InvalidOperationException("모니터 표시 영역의 Screen 메시가 없습니다.");
    }

    /// <summary>
    /// camera를 desktop의 정면에 배치하고 기존 시야각으로 화면 전체를 표시한다.
    /// 화면 크기로 거리를 계산하고 모니터 앞 키보드가 조작 화면을 가리지 않도록 근접 평면을 설정한다.
    /// </summary>
    private static void ConfigureMonitorCamera(CinemachineCamera camera, RectTransform desktop)
    {
        float width = desktop.rect.width * desktop.lossyScale.x;
        float height = desktop.rect.height * desktop.lossyScale.y;
        float distance = Mathf.Max(height, width / 1.6f) * 0.56f
            / Mathf.Tan(camera.Lens.FieldOfView * Mathf.Deg2Rad * 0.5f);
        Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform }, UNDO_NAME);
        camera.transform.SetPositionAndRotation(desktop.position - desktop.forward * distance, desktop.rotation);
        LensSettings lens = camera.Lens;
        lens.NearClipPlane = Mathf.Max(0.01f, distance - 0.12f);
        camera.Lens = lens;
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera.transform);
    }

    /// <summary>
    /// name, parent와 size로 UI 사각형을 생성한다.
    /// 부모의 레이어와 중앙 앵커를 적용하고 Undo에 등록된 RectTransform을 반환한다.
    /// </summary>
    private static RectTransform CreateRect(string name, Transform parent, Vector2 size)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, UNDO_NAME);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, false);
        gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>
    /// name, parent, size, color와 sprite로 UI 이미지를 생성한다.
    /// 생성된 사각형에 Image를 연결하고 표시 컴포넌트를 반환한다.
    /// </summary>
    private static Image CreateImage(string name, Transform parent, Vector2 size, Color color, Sprite sprite)
    {
        RectTransform rect = CreateRect(name, parent, size);
        Image image = Undo.AddComponent<Image>(rect.gameObject);
        image.color = color;
        image.sprite = sprite;
        return image;
    }

    /// <summary>
    /// name, parent, size, value, font와 fontSize로 앱 이름 텍스트를 생성한다.
    /// 중앙 정렬과 클릭 통과를 적용한 Text 컴포넌트를 반환한다.
    /// </summary>
    private static Text CreateText(string name, Transform parent, Vector2 size, string value, Font font, int fontSize)
    {
        RectTransform rect = CreateRect(name, parent, size);
        Text text = Undo.AddComponent<Text>(rect.gameObject);
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// rect를 부모 전체에 맞춰 펼친다.
    /// 앵커와 여백을 변경하고 프리팹 인스턴스의 배치 변경을 기록한다.
    /// </summary>
    private static void Stretch(RectTransform rect)
    {
        Undo.RecordObject(rect, UNDO_NAME);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
    }

    /// <summary>
    /// rect에 anchor, pivot, position과 size를 적용한다.
    /// 고정 크기 UI 배치를 변경하고 프리팹 인스턴스의 변경을 기록한다.
    /// </summary>
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        Undo.RecordObject(rect, UNDO_NAME);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
    }

    /// <summary>
    /// target의 fieldName에 연결된 Unity 참조를 읽는다.
    /// 직렬화된 필드의 값을 T 타입으로 반환한다.
    /// </summary>
    private static T GetReference<T>(UnityEngine.Object target, string fieldName) where T : UnityEngine.Object
    {
        return (T)new SerializedObject(target).FindProperty(fieldName).objectReferenceValue;
    }

    /// <summary>
    /// target의 fieldName에 value를 연결한다.
    /// Inspector 참조를 적용하고 프리팹 인스턴스의 변경을 기록한다.
    /// </summary>
    private static void SetReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(fieldName).objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
