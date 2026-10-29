using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

using KeyboardModeling;

public sealed class KeyboardCustomizationTestUI : MonoBehaviour
{
    [Header("Storage")]
    [SerializeField] private KeyboardCustomizationStorage _storage;
    private IKeyboardPhotoTarget _assemblyPhotoTarget;
    private RectTransform _assemblyListContent;
    private IKeyboardPhotoTarget PhotoTarget => _assemblyPhotoTarget ?? _storage?.Customization;
    private InputField _path;
    private InputField _index;
    private RawImage _preview;
    private Text _message;
    private Text _imageStateLabel;
    private Texture2D _appliedPreviewTexture;
    private ImageTarget _appliedPreviewTarget;
    private int _appliedPreviewIndex;
    private Vector2 _appliedPreviewOffset;
    private float _appliedPreviewZoom;
    private Font _font;
    private Texture2D _texture;
    private KeyboardInputController _input;
    private bool _previousInput;
    private GameObject _canvas;
    private GameObject _customizationPanel;
    private float _catalogWidth;
    private GameObject _startPanel;
    private bool _embedded;
    private bool _isCustomizing;

    [Header("Embedded Photo Panel")]
    private float _wheelZoomSensitivity = 0.03f;
    private RectTransform _embeddedParentRect;
    private RectTransform _embeddedPanelRect;
    private RectTransform _embeddedViewportRect;
    private RectTransform _embeddedContentRect;
    private float _lastParentHeight = -1f;
    private Text _fileName;
    private Text _fileDimensions;
    private Text _applyText;
    private Text _scopeNote;
    private Text _pathToggleLabel;
    private KeyboardCustomizationTestButton _applyButton;
    private Image _applyBackground;
    private RectTransform _targetSection;
    private RectTransform _toolsSection;
    private RectTransform _keyIndexPanel;
    private Text _keyInputWaitingLabel;
    private Image _keyInputButtonBackground;
    private Text _keyInputButtonLabel;
    private bool _isWaitingForKeyInput;
    private RectTransform _applyButtonRect;
    private RectTransform _scopeNoteRect;
    private RectTransform _formatNoteRect;
    private RectTransform _imageDividerRect;
    private RectTransform _cropEditor;
    private RectTransform _cropFrame;
    private RawImage _cropPreview;
    private KeyboardPhotoCropInput _cropInput;
    private readonly List<RectTransform> _cropDashes = new List<RectTransform>();
    private Vector2 _cropOffset;
    private float _cropZoom = 1f;
    private readonly Image[] _targetBackgrounds = new Image[3];
    private readonly Text[] _targetLabels = new Text[3];
    private bool _hasSelectedImage;
    private bool _pathExpanded;
    private ImageTarget _selectedTarget = ImageTarget.AllKeycaps;
    private Texture2D _previewCheckerTexture;

    public event Action CloseRequested;
    public event Action CustomizationLoaded;

    private enum ImageTarget
    {
        AllKeycaps,
        Case,
        Key
    }

    void Start()
    {
        if (_embedded) return;
        _input = _assemblyPhotoTarget == null ? _storage.Customization.GetComponent<KeyboardInputController>() : null;
        _previousInput = _input.InputEnabled;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _canvas = new GameObject("KeyboardPhotoTestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 1;
        if (EventSystem.current == null)
        {
            GameObject events = new GameObject("PhotoTestEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(_canvas.transform);
            InGameCursor.ConfigureUI(events.GetComponent<InputSystemUIInputModule>());
        }
        CreatePanel(_canvas.transform);
        var startPanel = Rect("Customization start", _canvas.transform, 20, 20, 340, 82);
        _startPanel = startPanel.gameObject;
        startPanel.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.96f);
        Button(startPanel, "Start customization", 18, 18, 304, BeginCustomization);
        BeginCustomization();
    }

    void Update()
    {
        if (Game.Tutorials.FirstEncounterTutorial.IsBlocking
            || Game.Logging.PlaytestResearchSession.IsUploadConfirmationOpen
            || (_embedded && !InGameCursor.CanProcessVirtualPointer)) return;
        if (_isWaitingForKeyInput)
            CaptureWaitingKeyInput();

        if (!_embedded || _embeddedParentRect == null)
            return;

        float parentHeight = _embeddedParentRect.rect.height;
        if (Mathf.Abs(parentHeight - _lastParentHeight) < 1f)
            return;

        _lastParentHeight = parentHeight;
        ResizeEmbeddedPanel();
    }

    /// <summary>
    /// 대기 중인 Unity 키 입력을 감지해 사진용 키캡 인덱스로 변환한다.
    /// Escape는 0번 인덱스로 완료하고, 연결되지 않은 키는 대기 상태를 유지한다.
    /// </summary>
    private void CaptureWaitingKeyInput()
    {
        if (!_embedded || !_isCustomizing || _selectedTarget != ImageTarget.Key)
        {
            SetKeyInputWaiting(false);
            return;
        }

        if (!Application.isFocused)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            CompleteKeyInput(0);
            return;
        }

        foreach (var keyControl in keyboard.allKeys)
        {
            if (!WindowsSpecialKeyInput.WasPressedThisFrame(keyboard, keyControl.keyCode))
                continue;

            if (PhotoTarget.TryGetPhotoKeycapIndex(keyControl.keyCode, out int keycapIndex))
            {
                CompleteKeyInput(keycapIndex);
                return;
            }

            _message.text = "해당 키에 연결된 키캡이 없습니다. 다른 키를 입력하세요.";
            return;
        }
    }

    /// <summary>
    /// 키 입력 대기 상태를 종료하고 선택 인덱스를 입력란과 미리보기에 반영한다.
    /// keycapIndex를 사용해 적용 버튼과 사진 프레임을 갱신한다.
    /// </summary>
    private void CompleteKeyInput(int keycapIndex)
    {
        SetKeyInputWaiting(false);
        _index.text = keycapIndex.ToString();
        HandleKeyIndexChanged(_index.text);
        _message.text = "키 " + keycapIndex + "번을 선택했습니다.";
    }

    /// <summary>
    /// 키 입력 대기 버튼의 전환을 처리하고 대기 안내와 버튼 모양을 갱신한다.
    /// 대기 상태에서는 숫자 입력란을 숨기고 키 입력 대기 문구를 표시한다.
    /// </summary>
    private void ToggleKeyInputWaiting()
    {
        SetKeyInputWaiting(!_isWaitingForKeyInput);
        if (_isWaitingForKeyInput)
        {
            EventSystem.current.SetSelectedGameObject(null);
            _message.text = "키 입력 대기 중입니다. Escape는 0번으로 선택합니다.";
        }
        else
        {
            _message.text = "키 입력 대기를 취소했습니다.";
        }

        RefreshEmbeddedPanel();
    }

    /// <summary>
    /// 키 입력 대기 상태를 지정하고 관련 UI를 갱신한다.
    /// isWaiting 값에 따라 인덱스 입력란과 대기 안내 표시를 전환한다.
    /// </summary>
    private void SetKeyInputWaiting(bool isWaiting)
    {
        _isWaitingForKeyInput = isWaiting;
        RefreshKeyInputControls();
    }

    /// <summary>
    /// 대기 상태에 맞춰 숫자 입력란, 안내 문구, 키 입력 버튼의 색과 문구를 갱신한다.
    /// _isWaitingForKeyInput 상태를 사용해 세 UI 요소를 동기화한다.
    /// </summary>
    private void RefreshKeyInputControls()
    {
        _index.gameObject.SetActive(!_isWaitingForKeyInput);
        _keyInputWaitingLabel.gameObject.SetActive(_isWaitingForKeyInput);
        _keyInputButtonBackground.color = _isWaitingForKeyInput
            ? new Color(0.17f, 0.53f, 0.48f)
            : new Color(0.12f, 0.21f, 0.25f);
        _keyInputButtonLabel.color = _isWaitingForKeyInput
            ? Color.white
            : new Color(0.78f, 0.88f, 0.9f);
        _keyInputButtonLabel.text = _isWaitingForKeyInput ? "입력 중" : "키 입력";
    }

    /// <summary>
    /// storage와 wheelZoomSensitivity로 HTML 사진 편집 UI를 현재 오버레이에 구성한다.
    /// 별도 Canvas를 생성하지 않고 편집 패널의 휠 민감도와 저장소 참조를 설정한다.
    /// </summary>
    public void InitializeEmbedded(KeyboardCustomizationStorage storage, float wheelZoomSensitivity)
    {
        _embedded = true;
        _storage = storage;
        SetWheelZoomSensitivity(wheelZoomSensitivity);
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _embeddedParentRect = transform as RectTransform;
        CreateEmbeddedPanel(transform);
        RectTransform panel = (RectTransform)_customizationPanel.transform;
        panel.anchorMin = panel.anchorMax = Vector2.one;
        panel.anchoredPosition = new Vector2(-408f, -20f);
        ResizeEmbeddedPanel();
        _customizationPanel.SetActive(false);
    }

    /// <summary>
    /// target을 사용하는 기존 사진 편집 컨트롤을 parent 목록 안에 구성한다.
    /// width에 맞춰 배치하고 조립 씬에 연결되지 않은 저장 영역은 숨긴 뒤 콘텐츠 높이를 반환한다.
    /// </summary>
    public float InitializeAssemblyEmbedded(IKeyboardPhotoTarget target, RectTransform parent, float width, KeyboardCustomizationStorage storage)
    {
        _assemblyPhotoTarget = target;
        _storage = storage;
        _assemblyListContent = parent.parent as RectTransform;
        _embedded = true;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CreateEmbeddedPanel(parent);
        GameObject frame = _customizationPanel;
        _embeddedContentRect.SetParent(parent, false);
        _embeddedContentRect.anchorMin = _embeddedContentRect.anchorMax = _embeddedContentRect.pivot = new Vector2(0f, 1f);
        _embeddedContentRect.anchoredPosition = Vector2.zero;
        _embeddedContentRect.sizeDelta = new Vector2(366f, 802f);
        _embeddedContentRect.localScale = Vector3.one * (width / 366f);
        foreach (Text label in _embeddedContentRect.GetComponentsInChildren<Text>(true))
        {
            label.fontSize = Mathf.Max(18, label.fontSize);
            label.resizeTextForBestFit = false;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }
        _toolsSection.Find("StoragePanel").gameObject.SetActive(_storage != null);
        ConfigureAssemblyPhotoLayout();
        _customizationPanel = _embeddedContentRect.gameObject;
        _embeddedPanelRect = null;
        _embeddedViewportRect = null;
        Destroy(frame);
        _customizationPanel.SetActive(false);
        return _embeddedContentRect.rect.height * width / 366f;
    }

    /// <summary>
    /// 조립용 사진 컨트롤을 내용 높이에 따라 쌓이는 목록으로 구성한다.
    /// 기존 컨트롤과 이벤트는 유지하고 고정 좌표 대신 레이아웃 그룹이 간격과 높이를 결정한다.
    /// </summary>
    private void ConfigureAssemblyPhotoLayout()
    {
        CreateAssemblyPhotoStack(_embeddedContentRect, 16);
        CreateAssemblyPhotoStack(_targetSection, 0);
        CreateAssemblyPhotoStack(_toolsSection, 0);
        CreateAssemblyPhotoRow(_embeddedContentRect, "ImageStepRow", 32f, "ImageStepBadge", "ImageStepTitle", "ImageStepHint");
        CreateAssemblyPhotoRow(_embeddedContentRect, "FileDetailsRow", 64f, "FileName", "FileDimensions");
        CreateAssemblyPhotoRow(_targetSection, "TargetStepRow", 32f, "TargetStepBadge", "TargetStepTitle");
        CreateAssemblyPhotoRow(_targetSection, "TargetButtonsRow", 56f, "Target_0", "Target_1", "Target_2");
        CreateAssemblyPhotoRow(_toolsSection, "ClearButtonsRow", 72f, "ClearKeycaps", "ClearCase");
        RectTransform storagePanel = (RectTransform)_toolsSection.Find("StoragePanel");
        if (_storage != null)
        {
            CreateAssemblyPhotoStack(storagePanel, 12);
            storagePanel.Find("StorageTag").gameObject.SetActive(false);
            CreateAssemblyPhotoRow(storagePanel, "StorageButtonsRow", 64f, "SaveCustomization", "LoadCustomization");
        }
        foreach (KeyboardCustomizationTestButton button in _embeddedContentRect.GetComponentsInChildren<KeyboardCustomizationTestButton>(true))
        {
            LayoutElement element = button.GetComponent<LayoutElement>();
            if (element == null) element = button.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 56f;
            Text label = button.GetComponentInChildren<Text>(true);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(6f, 4f);
            label.rectTransform.offsetMax = new Vector2(-6f, -4f);
        }
        _cropEditor.gameObject.AddComponent<LayoutElement>().preferredHeight = 180f;
        _path.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
        _keyIndexPanel.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
        _scopeNoteRect.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
        _scopeNote.rectTransform.anchorMin = Vector2.zero;
        _scopeNote.rectTransform.anchorMax = Vector2.one;
        _scopeNote.rectTransform.offsetMin = new Vector2(22f, 0f);
        _scopeNote.rectTransform.offsetMax = Vector2.zero;
        _formatNoteRect.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        _pathToggleLabel.transform.parent.GetComponent<LayoutElement>().minHeight = 80f;
        foreach (Image divider in _toolsSection.GetComponentsInChildren<Image>())
            if (divider.name == "ToolsDivider") divider.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
        _imageDividerRect.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_embeddedContentRect);
    }

    /// <summary>
    /// 이미 생성한 이미지 조작 항목만 parent의 공통 목록으로 옮긴다.
    /// width를 사용해 세로 레이아웃을 구성하고 기존 프레임과 중첩 스크롤을 제거한 콘텐츠를 반환한다.
    /// </summary>
    public RectTransform EmbedControls(RectTransform parent, float width)
    {
        GameObject frame = _customizationPanel;
        bool visible = frame.activeSelf;
        _catalogWidth = width;
        _assemblyListContent = parent;
        _embeddedContentRect.SetParent(parent, false);
        _embeddedContentRect.anchorMin = _embeddedContentRect.anchorMax = _embeddedContentRect.pivot = Vector2.up;
        _embeddedContentRect.anchoredPosition = Vector2.zero;
        _embeddedContentRect.localScale = Vector3.one;
        _embeddedContentRect.sizeDelta = new Vector2(width, 802f);
        foreach (Text label in _embeddedContentRect.GetComponentsInChildren<Text>(true))
        {
            label.fontSize = Mathf.Max(16, label.fontSize);
            label.resizeTextForBestFit = false;
        }
        ConfigureAssemblyPhotoLayout();
        _customizationPanel = _embeddedContentRect.gameObject;
        _embeddedPanelRect = null;
        _embeddedViewportRect = null;
        frame.SetActive(false);
        Destroy(frame);
        _customizationPanel.SetActive(visible);
        return _embeddedContentRect;
    }

    /// <summary>
    /// rect 아래의 컨트롤을 padding과 일정 간격을 사용하는 세로 목록으로 배치한다.
    /// 반환값 없이 콘텐츠 높이가 실제 자식 높이를 따르도록 설정한다.
    /// </summary>
    private void CreateAssemblyPhotoStack(RectTransform rect, int padding)
    {
        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    /// <summary>
    /// parent의 names 컨트롤을 name과 height를 사용하는 한 줄로 묶는다.
    /// 기존 순서를 유지하며 각 컨트롤의 실제 글자 높이와 최소 버튼 높이를 레이아웃에 반영한다.
    /// </summary>
    private void CreateAssemblyPhotoRow(RectTransform parent, string name, float height, params string[] names)
    {
        int order = parent.Find(names[0]).GetSiblingIndex();
        RectTransform row = Rect(name, parent, 0f, 0f, 326f, height);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        foreach (string childName in names)
        {
            Transform child = parent.Find(childName);
            child.SetParent(row, false);
            LayoutElement element = child.gameObject.AddComponent<LayoutElement>();
            bool badge = childName.EndsWith("Badge", StringComparison.Ordinal);
            element.preferredWidth = badge ? 24f : 0f;
            element.flexibleWidth = badge ? 0f : 1f;
            if (badge)
            {
                RectTransform number = (RectTransform)child.GetChild(0);
                number.anchorMin = Vector2.zero;
                number.anchorMax = Vector2.one;
                number.offsetMin = number.offsetMax = Vector2.zero;
            }
        }
        row.SetSiblingIndex(order);
    }

    /// <summary>
    /// Inspector 설정값을 이미지 휠 확대·축소 처리에 반영한다.
    /// wheelZoomSensitivity를 유효 범위로 제한해 내부 민감도 상태를 갱신한다.
    /// </summary>
    public void SetWheelZoomSensitivity(float wheelZoomSensitivity)
    {
        _wheelZoomSensitivity = Mathf.Clamp(wheelZoomSensitivity, 0.01f, 0.2f);
    }

    /// <summary>
    /// 부모 UI 높이에 따라 패널과 뷰포트를 조정한다.
    /// 기본 배치를 담을 높이까지 패널을 늘리고 화면을 넘는 경우에는 뷰포트를 제한한다.
    /// </summary>
    private void ResizeEmbeddedPanel()
    {
        if (_embeddedPanelRect == null || _embeddedViewportRect == null)
            return;

        const float preferredPanelHeight = 826f;
        const float panelVerticalMargin = 40f;
        const float viewportVerticalInset = 24f;
        float availableHeight = _embeddedParentRect != null
            ? _embeddedParentRect.rect.height - panelVerticalMargin
            : preferredPanelHeight;
        if (availableHeight <= 0f)
            availableHeight = preferredPanelHeight;

        float panelHeight = Mathf.Max(320f, Mathf.Min(preferredPanelHeight, availableHeight));
        _embeddedPanelRect.sizeDelta = new Vector2(390f, panelHeight);
        _embeddedViewportRect.sizeDelta = new Vector2(366f, panelHeight - viewportVerticalInset);
    }

    /// <summary>
    /// viewport 오른쪽에 자동 표시되는 얇은 스크롤바를 만든다.
    /// track과 handle을 사용하며 스크롤바 참조를 반환한다.
    /// </summary>
    private Scrollbar CreateEmbeddedScrollbar(Transform viewport)
    {
        RectTransform scrollbarRect = new GameObject("PhotoScrollbar", typeof(RectTransform)).GetComponent<RectTransform>();
        scrollbarRect.SetParent(viewport, false);
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = new Vector2(-4f, 0f);
        scrollbarRect.sizeDelta = new Vector2(5f, -16f);
        Image trackImage = scrollbarRect.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.28f, 0.38f, 0.43f, 0.3f);

        Scrollbar scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        RectTransform handleRect = new GameObject("Handle", typeof(RectTransform)).GetComponent<RectTransform>();
        handleRect.SetParent(scrollbarRect, false);
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        Image handleImage = handleRect.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.67f, 0.78f, 0.82f, 0.82f);
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        return scrollbar;
    }

    /// <summary>
    /// HTML 사진 편집 레퍼런스의 선택, 대상, 적용 및 저장 UI를 내장 패널로 생성한다.
    /// content의 스크롤 영역에 컨트롤을 배치하고 기존 C# 처리 함수에 연결한다.
    /// </summary>
    private void CreateEmbeddedPanel(Transform parent)
    {
        RectTransform panel = Rect("Photo customization", parent, 20f, 20f, 390f, 826f);
        _embeddedPanelRect = panel;
        _customizationPanel = panel.gameObject;
        panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.1f, 0.14f, 0.98f);

        RectTransform viewport = Rect("PhotoCustomizerViewport", panel, 12f, 12f, 366f, 802f);
        _embeddedViewportRect = viewport;
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0.06f, 0.09f, 0.13f, 1f);
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        RectTransform content = new GameObject("PhotoCustomizerContent", typeof(RectTransform)).GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 802f);
        _embeddedContentRect = content;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34f;
        scroll.verticalScrollbar = CreateEmbeddedScrollbar(viewport);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        AddEmbeddedLabel(content, "PhotoTitle", "키보드 이미지", 16f, 14f, 326f, 30f, 23, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        AddEmbeddedLabel(content, "PhotoSubtitle", "원하는 사진을 키캡이나 케이스에 입혀보세요.", 16f, 44f, 326f, 18f, 11, new Color(0.62f, 0.69f, 0.73f), TextAnchor.MiddleLeft);
        AddStepHeading(content, "ImageStep", "1", "이미지 선택", "PNG · JPG", 16f, 64f, 326f);

        _previewCheckerTexture = CreatePreviewCheckerTexture();
        AddEmbeddedLabel(content, "CropHeading", "이미지 배치  ·  드래그 이동 / 휠 확대·축소", 16f, 90f, 326f, 20f, 11,
            new Color(0.75f, 0.84f, 0.87f), TextAnchor.MiddleLeft, FontStyle.Bold);
        CreateCropEditor(content);

        _fileName = AddEmbeddedLabel(content, "FileName", "파일을 선택해 주세요", 16f, 270f, 245f, 18f, 10, new Color(0.67f, 0.73f, 0.77f), TextAnchor.MiddleLeft);
        _fileDimensions = AddEmbeddedLabel(content, "FileDimensions", "—", 260f, 270f, 82f, 18f, 9, new Color(0.48f, 0.61f, 0.65f), TextAnchor.MiddleRight);
        CreateEmbeddedButton(content, "ChooseImage", "이미지 파일 선택", 16f, 292f, 326f, 36f,
            new Color(0.14f, 0.24f, 0.29f), 12, ChooseImage, out _);
        CreateEmbeddedButton(content, "PathToggle", "[+] 파일 경로 직접 입력   ·   빌드 버전용", 16f, 330f, 326f, 20f,
            new Color(0.06f, 0.09f, 0.13f), 10, TogglePathInput, out _pathToggleLabel);
        _pathToggleLabel.alignment = TextAnchor.MiddleLeft;
        _pathToggleLabel.color = new Color(0.58f, 0.66f, 0.71f);
        _pathToggleLabel.fontStyle = FontStyle.Normal;
        _path = Field(content, "C:\\Images\\my-keyboard.png", 16f, 352f, 326f);
        _path.gameObject.SetActive(false);
        _path.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.12f);
        _path.textComponent.color = new Color(0.89f, 0.94f, 0.96f);
        _path.onEndEdit.AddListener(HandleImagePathEdited);
        _formatNoteRect = AddEmbeddedLabel(content, "FormatNote", "최대 32MB · 이미지 한 변 8,192px 이하 · PNG/JPG만 지원", 16f, 354f, 326f, 28f, 9,
            new Color(0.45f, 0.54f, 0.6f), TextAnchor.MiddleLeft).rectTransform;
        _imageDividerRect = CreateDivider(content, "ImageDivider", 16f, 392f, 326f);

        _targetSection = Rect("TargetSection", content, 16f, 398f, 326f, 200f);
        AddStepHeading(_targetSection, "TargetStep", "2", "적용 위치 선택", "", 0f, 0f, 326f);
        CreateTargetButton(_targetSection, 0, "전체 키캡", "", 0f, 24f, 104f, 42f);
        CreateTargetButton(_targetSection, 1, "케이스", "", 111f, 24f, 104f, 42f);
        CreateTargetButton(_targetSection, 2, "키캡 한 개", "", 222f, 24f, 104f, 42f);

        _imageStateLabel = AddEmbeddedLabel(_targetSection, "ImageState", "이미지 미선택", 0f, 72f, 326f, 26f, 12,
            new Color32(140, 155, 170, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
        _keyIndexPanel = Rect("KeyIndexPanel", _targetSection, 0f, 100f, 326f, 40f);
        _keyIndexPanel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.16f, 0.19f);
        AddEmbeddedLabel(_keyIndexPanel, "KeyIndexLabel", "적용 키 (0부터)", 10f, 0f, 92f, 40f, 9,
            new Color(0.78f, 0.86f, 0.88f), TextAnchor.MiddleLeft);
        _index = Field(_keyIndexPanel, "0", 110f, 4f, 96f);
        _index.text = "0";
        _index.contentType = InputField.ContentType.IntegerNumber;
        _index.textComponent.alignment = TextAnchor.MiddleCenter;
        _index.textComponent.fontStyle = FontStyle.Bold;
        _index.GetComponent<Image>().color = new Color(0.04f, 0.09f, 0.12f);
        _index.onValueChanged.AddListener(HandleKeyIndexChanged);
        _keyInputWaitingLabel = AddEmbeddedLabel(_keyIndexPanel, "KeyInputWaiting", "키 입력 대기 중", 110f, 4f, 96f, 32f, 9,
            new Color(0.87f, 0.95f, 0.95f), TextAnchor.MiddleCenter, FontStyle.Bold);
        _keyInputWaitingLabel.gameObject.SetActive(false);
        _keyInputButtonBackground = CreateEmbeddedButton(_keyIndexPanel, "CaptureKeyInput", "키 입력", 216f, 4f, 100f, 32f,
            new Color(0.12f, 0.21f, 0.25f), 9, ToggleKeyInputWaiting, out _keyInputButtonLabel);

        _applyBackground = CreateEmbeddedButton(_targetSection, "ApplyImage", "", 0f, 72f, 326f, 40f,
            new Color(0.2f, 0.42f, 0.41f), 13, ApplyEmbeddedImage, out _applyText);
        _applyButton = _applyBackground.GetComponent<KeyboardCustomizationTestButton>();
        _applyText.color = new Color(0.03f, 0.18f, 0.17f);
        _applyText.fontStyle = FontStyle.Bold;
        _applyButtonRect = (RectTransform)_applyBackground.transform;
        _scopeNoteRect = Rect("ScopeNote", _targetSection, 0f, 117f, 326f, 30f);
        AddEmbeddedLabel(_scopeNoteRect, "ScopeIcon", "i", 0f, 0f, 16f, 22f, 11, new Color(0.78f, 0.72f, 0.55f), TextAnchor.MiddleCenter, FontStyle.Bold);
        _scopeNote = AddEmbeddedLabel(_scopeNoteRect, "ScopeText", "", 22f, 0f, 304f, 30f, 9,
            new Color(0.56f, 0.63f, 0.67f), TextAnchor.MiddleLeft);

        _toolsSection = Rect("PhotoToolsSection", content, 16f, 566f, 326f, 220f);
        CreateDivider(_toolsSection, "ToolsDivider", 0f, 0f, 326f);
        AddEmbeddedLabel(_toolsSection, "ClearHeading", "이미지 제거", 0f, 5f, 176f, 20f, 11,
            new Color(0.73f, 0.79f, 0.82f), TextAnchor.MiddleLeft, FontStyle.Bold);
        AddEmbeddedLabel(_toolsSection, "ClearHint", "기존 재질로 복원", 180f, 5f, 146f, 20f, 9,
            new Color(0.42f, 0.54f, 0.61f), TextAnchor.MiddleRight);
        CreateEmbeddedButton(_toolsSection, "ClearKeycaps", "키캡 이미지 지우기", 0f, 28f, 159f, 32f,
            new Color(0.19f, 0.15f, 0.17f), 10, ClearKeycapImages, out _);
        CreateEmbeddedButton(_toolsSection, "ClearCase", "케이스 이미지 지우기", 167f, 28f, 159f, 32f,
            new Color(0.19f, 0.15f, 0.17f), 10, ClearCaseImage, out _);

        RectTransform storagePanel = Rect("StoragePanel", _toolsSection, 0f, 68f, 326f, 110f);
        storagePanel.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.11f, 0.15f);
        AddEmbeddedLabel(storagePanel, "StorageHeading", "커스터마이징 저장", 12f, 4f, 212f, 18f, 11,
            new Color(0.8f, 0.86f, 0.88f), TextAnchor.MiddleLeft, FontStyle.Bold);
        AddEmbeddedLabel(storagePanel, "StorageTag", "KEYBOARD DATA", 216f, 4f, 98f, 18f, 8,
            new Color(0.46f, 0.56f, 0.61f), TextAnchor.MiddleRight, FontStyle.Bold);
        CreateEmbeddedButton(storagePanel, "SaveCustomization", "저장하기", 12f, 26f, 145f, 32f,
            new Color(0.13f, 0.2f, 0.25f), 11, SaveCustomization, out _);
        CreateEmbeddedButton(storagePanel, "LoadCustomization", "불러오기", 169f, 26f, 145f, 32f,
            new Color(0.13f, 0.2f, 0.25f), 11, LoadCustomization, out _);
        AddEmbeddedLabel(storagePanel, "StorageWarning", "이미지뿐 아니라 재질·스위치·보강판·LED도 함께 저장됩니다. 불러오기는 현재 설정을 교체하며 저장 파일은 키보드 간에 공유됩니다.",
            12f, 62f, 302f, 42f, 8, new Color(0.49f, 0.58f, 0.63f), TextAnchor.MiddleLeft);
        _message = AddEmbeddedLabel(_toolsSection, "Status", "이미지를 선택한 뒤 적용 위치를 골라 주세요.", 0f, 180f, 326f, 36f, 9,
            new Color(0.58f, 0.72f, 0.7f), TextAnchor.MiddleLeft);

        _keyIndexPanel.gameObject.SetActive(false);
        RefreshKeyInputControls();
        RefreshEmbeddedPanel();
    }

    /// <summary>
    /// 원본 이미지와 투명한 프레임 바깥을 보여줄 crop 편집 영역을 만든다.
    /// content를 부모로 사용하며 편집 프리뷰와 드래그·휠 이벤트를 연결한다.
    /// </summary>
    private void CreateCropEditor(Transform content)
    {
        _cropEditor = Rect("CropEditor", content, 16f, 112f, 326f, 154f);
        Image editorBackground = _cropEditor.gameObject.AddComponent<Image>();
        editorBackground.color = Color.clear;
        editorBackground.raycastTarget = false;
        _cropEditor.gameObject.AddComponent<RectMask2D>();

        _cropFrame = Rect("CropFrame", _cropEditor, 0f, 0f, 306f, 134f);
        _cropFrame.anchorMin = _cropFrame.anchorMax = _cropFrame.pivot = new Vector2(0.5f, 0.5f);
        _cropFrame.anchoredPosition = Vector2.zero;
        Image frameBackground = _cropFrame.gameObject.AddComponent<Image>();
        frameBackground.color = new Color(0.12f, 0.15f, 0.18f);
        frameBackground.raycastTarget = true;

        RectTransform checkerRect = new GameObject("CropCheckerboard", typeof(RectTransform)).GetComponent<RectTransform>();
        checkerRect.SetParent(_cropFrame, false);
        checkerRect.anchorMin = Vector2.zero;
        checkerRect.anchorMax = Vector2.one;
        checkerRect.offsetMin = Vector2.zero;
        checkerRect.offsetMax = Vector2.zero;
        RawImage checker = checkerRect.gameObject.AddComponent<RawImage>();
        checker.texture = _previewCheckerTexture;
        checker.uvRect = new Rect(0f, 0f, 15.3f, 6.7f);
        checker.raycastTarget = false;

        RectTransform cropImageRect = new GameObject("CropImage", typeof(RectTransform)).GetComponent<RectTransform>();
        cropImageRect.SetParent(_cropFrame, false);
        cropImageRect.anchorMin = cropImageRect.anchorMax = cropImageRect.pivot = new Vector2(0.5f, 0.5f);
        _cropPreview = cropImageRect.gameObject.AddComponent<RawImage>();
        _cropPreview.color = Color.clear;
        _cropPreview.raycastTarget = false;

        _cropInput = _cropFrame.gameObject.AddComponent<KeyboardPhotoCropInput>();
        _cropInput.Dragged += HandleCropDragged;
        _cropInput.Zoomed += HandleCropZoomed;
    }

    /// <summary>
    /// targetIndex의 적용 범위 버튼을 만들고 선택 상태 갱신 이벤트를 연결한다.
    /// title과 description을 표시하며 좁은 가로 버튼 크기에 맞춰 중앙 정렬한다.
    /// </summary>
    private void CreateTargetButton(Transform parent, int targetIndex, string title, string description, float x, float y, float width, float height)
    {
        Color backgroundColor = new Color(0.06f, 0.1f, 0.14f);
        Image background = CreateEmbeddedButton(parent, "Target_" + targetIndex, "", x, y, width, height,
            backgroundColor, 10, () => SelectImageTarget((ImageTarget)targetIndex), out Text label);
        _targetBackgrounds[targetIndex] = background;
        _targetLabels[targetIndex] = label;
        _targetLabels[targetIndex].text = string.IsNullOrEmpty(description) ? title : title + "\n" + description;
        _targetLabels[targetIndex].alignment = TextAnchor.MiddleCenter;
        _targetLabels[targetIndex].rectTransform.anchoredPosition = new Vector2(6f, 0f);
        _targetLabels[targetIndex].rectTransform.sizeDelta = new Vector2(width - 12f, height);
        _targetLabels[targetIndex].color = new Color(0.86f, 0.91f, 0.93f);
    }

    /// <summary>
    /// parent 안에 스타일과 C# 클릭 콜백을 가진 버튼을 만든다.
    /// 크기와 표시 문자열을 사용하고 배경 Image와 라벨 참조를 반환한다.
    /// </summary>
    private Image CreateEmbeddedButton(Transform parent, string name, string text, float x, float y, float width, float height,
        Color backgroundColor, int fontSize, Action action, out Text label)
    {
        RectTransform rect = Rect(name, parent, x, y, width, height);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = backgroundColor;
        KeyboardCustomizationTestButton button = rect.gameObject.AddComponent<KeyboardCustomizationTestButton>();
        button.Clicked += action;
        label = AddEmbeddedLabel(rect, "Label", text, 10f, 0f, width - 20f, height, fontSize,
            Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        return background;
    }

    /// <summary>
    /// parent 아래 텍스트와 정렬을 지정한 라벨을 만들고 Text 참조를 반환한다.
    /// color와 style을 사용하며 장식용 라벨은 UI 입력을 가로채지 않는다.
    /// </summary>
    private Text AddEmbeddedLabel(Transform parent, string name, string text, float x, float y, float width, float height,
        int fontSize, Color color, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        RectTransform rect = Rect(name, parent, x, y, width, height);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = _font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>
    /// title과 step 번호를 사용해 HTML 레퍼런스와 같은 단계 제목을 만든다.
    /// parent의 지정 위치에 번호 표시와 보조 텍스트를 배치한다.
    /// </summary>
    private void AddStepHeading(Transform parent, string name, string step, string title, string hint, float x, float y, float width)
    {
        RectTransform badge = Rect(name + "Badge", parent, x, y, 20f, 20f);
        badge.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.3f, 0.3f);
        AddEmbeddedLabel(badge, "Number", step, 0f, 0f, 20f, 20f, 10, new Color(0.56f, 0.98f, 0.91f), TextAnchor.MiddleCenter, FontStyle.Bold);
        AddEmbeddedLabel(parent, name + "Title", title, x + 28f, y, width - 128f, 20f, 13,
            new Color(0.9f, 0.94f, 0.95f), TextAnchor.MiddleLeft, FontStyle.Bold);
        if (!string.IsNullOrEmpty(hint))
            AddEmbeddedLabel(parent, name + "Hint", hint, x + width - 96f, y, 96f, 20f, 9,
                new Color(0.56f, 0.65f, 0.7f), TextAnchor.MiddleRight);
    }

    /// <summary>
    /// preview 영역에 반복 가능한 어두운 체크무늬 텍스처를 생성해 반환한다.
    /// UI의 투명 이미지 배경에 사용하며 호출자가 파괴한다.
    /// </summary>
    private Texture2D CreatePreviewCheckerTexture()
    {
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels(new[]
        {
            new Color(0.12f, 0.17f, 0.21f),
            new Color(0.07f, 0.11f, 0.15f),
            new Color(0.07f, 0.11f, 0.15f),
            new Color(0.12f, 0.17f, 0.21f)
        });
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// parent의 지정 위치에 구분선을 만들고 RectTransform을 반환한다.
    /// width와 좌표를 사용해 얇은 어두운 가로선을 배치한다.
    /// </summary>
    private RectTransform CreateDivider(Transform parent, string name, float x, float y, float width)
    {
        RectTransform rect = Rect(name, parent, x, y, width, 1f);
        rect.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.24f, 0.29f);
        return rect;
    }

    /// <summary>
    /// target에 맞춰 선택 표시, 인덱스 입력과 적용 범위 안내를 갱신한다.
    /// 키 대상이면 적용 버튼과 도구 영역을 아래로 이동한다.
    /// </summary>
    private void SelectImageTarget(ImageTarget target)
    {
        _selectedTarget = target;
        RefreshEmbeddedPanel();
    }

    /// <summary>
    /// 현재 사진과 인덱스 입력을 사용해 적용 버튼 상태와 대상별 안내를 갱신한다.
    /// 유효한 사진과 키 번호일 때 적용 버튼을 켜고 확장된 내용 높이를 갱신한다.
    /// </summary>
    private void RefreshEmbeddedPanel()
    {
        if (!_embedded || _applyButton == null)
            return;

        if (_isWaitingForKeyInput && _selectedTarget != ImageTarget.Key)
            _isWaitingForKeyInput = false;

        for (int index = 0; index < _targetBackgrounds.Length; index++)
        {
            bool selected = index == (int)_selectedTarget;
            _targetBackgrounds[index].color = selected
                ? new Color(0.08f, 0.24f, 0.25f)
                : new Color(0.06f, 0.1f, 0.14f);
            string[] labels = { "전체 키캡", "케이스", "키캡 한 개" };
            _targetLabels[index].text = labels[index];
        }

        bool keyTarget = _selectedTarget == ImageTarget.Key;
        bool parsedIndex = int.TryParse(_index.text, out int keyIndex);
        bool validIndex = !keyTarget || (parsedIndex && keyIndex >= 0 && keyIndex < PhotoTarget.PhotoKeycapCount);
        float pathOffset = _pathExpanded ? 0f : 36f;
        float targetTop = 434f - pathOffset;
        float targetHeight = keyTarget ? 230f : 190f;
        _keyIndexPanel.gameObject.SetActive(keyTarget);
        _applyButtonRect.anchoredPosition = new Vector2(0f, keyTarget ? -146f : -102f);
        _scopeNoteRect.anchoredPosition = new Vector2(0f, keyTarget ? -191f : -147f);
        _formatNoteRect.anchoredPosition = new Vector2(16f, -(390f - pathOffset));
        _imageDividerRect.anchoredPosition = new Vector2(16f, -(428f - pathOffset));
        _targetSection.anchoredPosition = new Vector2(16f, -targetTop);
        _targetSection.sizeDelta = new Vector2(326f, targetHeight);
        float toolsTop = targetTop + targetHeight + 8f;
        _toolsSection.anchoredPosition = new Vector2(16f, -toolsTop);
        _embeddedContentRect.sizeDelta = new Vector2(_catalogWidth > 0f ? _catalogWidth : (_assemblyPhotoTarget != null ? 366f : 0f), toolsTop + _toolsSection.rect.height + 16f);
        if (_assemblyListContent != null)
            _assemblyListContent.sizeDelta = new Vector2(0f, _embeddedContentRect.sizeDelta.y * _embeddedContentRect.localScale.y);
        _path.gameObject.SetActive(_pathExpanded);
        _pathToggleLabel.text = (_pathExpanded ? "[-]" : "[+]") + " 파일 경로 직접 입력   ·   빌드 버전용";

        bool canApply = _hasSelectedImage && validIndex && !_isWaitingForKeyInput;
        _applyButton.enabled = canApply;
        _applyBackground.color = canApply
            ? new Color(0.23f, 0.85f, 0.77f)
            : new Color(0.19f, 0.39f, 0.37f);
        _applyText.color = canApply ? new Color(0.03f, 0.19f, 0.18f) : new Color(0.72f, 0.84f, 0.81f);
        _applyText.text = keyTarget
            ? "키캡 " + (_index.text.Length > 0 ? _index.text : "?") + "번에 이미지 적용"
            : _selectedTarget == ImageTarget.Case ? "키보드 케이스에 이미지 적용" : "전체 키캡에 이미지 적용";

        if (_selectedTarget == ImageTarget.Case)
            _scopeNote.text = "케이스 렌더러 전체에 한 장의 이미지를 배치합니다.";
        else if (keyTarget)
            _scopeNote.text = "전체 키캡 사진 위에 선택한 키의 개별 이미지를 적용합니다.";
        else
            _scopeNote.text = "전체 적용 시 기존에 개별 설정한 키캡 이미지가 덮어써집니다.";
        RefreshKeyInputControls();
        if ((_assemblyPhotoTarget != null || _catalogWidth > 0f) && _embeddedContentRect.GetComponent<VerticalLayoutGroup>() != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_embeddedContentRect);
            _assemblyListContent.sizeDelta = new Vector2(0f, _embeddedContentRect.rect.height * _embeddedContentRect.localScale.y);
        }
        RefreshCropEditor();
    }

    /// <summary>
    /// 현재 대상의 실제 경계 비율에 맞춰 crop 프레임과 사진 배치를 갱신한다.
    /// 대상 종류와 키 인덱스를 사용하며 편집 및 키보드 미리보기를 동기화한다.
    /// </summary>
    private void RefreshCropEditor()
    {
        if (_cropFrame == null || PhotoTarget == null) return;

        bool caseImage = _selectedTarget == ImageTarget.Case;
        int keyIndex = -1;
        if (_selectedTarget == ImageTarget.Key
            && int.TryParse(_index.text, out int parsedKeyIndex)
            && parsedKeyIndex >= 0 && parsedKeyIndex < PhotoTarget.PhotoKeycapCount)
            keyIndex = parsedKeyIndex;

        Vector2 canvasSize = PhotoTarget.GetPhotoCanvasSize(caseImage, keyIndex);
        float aspect = canvasSize.y > 0f ? canvasSize.x / canvasSize.y : 1f;
        const float maximumWidth = 306f;
        const float maximumHeight = 134f;
        float frameWidth = maximumWidth;
        float frameHeight = frameWidth / Mathf.Max(aspect, 0.01f);
        if (frameHeight > maximumHeight)
        {
            frameHeight = maximumHeight;
            frameWidth = frameHeight * aspect;
        }

        _cropFrame.sizeDelta = new Vector2(frameWidth, frameHeight);
        DrawCropFrame(new Vector2(frameWidth, frameHeight));
        RefreshCropVisuals();
    }

    /// <summary>
    /// crop 이미지의 화면 크기와 위치를 갱신하고 실제 키보드에 임시 배치를 표시한다.
    /// 선택 이미지와 배치값을 사용하며 비어 있는 영역은 원래 재질로 보인다.
    /// </summary>
    private void RefreshCropVisuals()
    {
        if (_cropPreview == null || _cropFrame == null) return;
        _cropFrame.GetComponent<Image>().raycastTarget = _hasSelectedImage;
        _cropPreview.texture = _hasSelectedImage ? _texture : null;
        _cropPreview.color = _hasSelectedImage ? Color.white : Color.clear;

        if (_hasSelectedImage && _texture != null)
        {
            Vector2 frameSize = _cropFrame.rect.size;
            float imageAspect = (float)_texture.width / _texture.height;
            float frameAspect = frameSize.x / Mathf.Max(frameSize.y, 0.001f);
            Vector2 coverSize = imageAspect >= frameAspect
                ? new Vector2(imageAspect / frameAspect, 1f)
                : new Vector2(1f, frameAspect / imageAspect);
            _cropPreview.rectTransform.sizeDelta = Vector2.Scale(frameSize, coverSize) * _cropZoom;
            _cropPreview.rectTransform.anchoredPosition = Vector2.Scale(frameSize, _cropOffset);
        }
        else
        {
            _cropPreview.rectTransform.sizeDelta = _cropFrame.rect.size;
            _cropPreview.rectTransform.anchoredPosition = Vector2.zero;
        }

        RefreshLiveKeyboardPreview();
    }

    /// <summary>
    /// 현재 편집 이미지를 선택 대상에 임시 적용하거나 이전 미리보기를 지운다.
    /// 이미지 종류, 키 인덱스와 배치값을 사용하며 저장 상태는 바꾸지 않는다.
    /// </summary>
    private void RefreshLiveKeyboardPreview()
    {
        if (!_embedded || !_hasSelectedImage || _texture == null)
        {
            RefreshImageStateLabel(false, -1);
            if (PhotoTarget != null)
                PhotoTarget.ClearPhotoImagePreview();
            return;
        }

        if (_selectedTarget == ImageTarget.Case)
        {
            bool previewed = PhotoTarget.PreviewCaseImage(_texture, _cropOffset, _cropZoom);
            RefreshImageStateLabel(previewed, -1);
            return;
        }

        if (_selectedTarget == ImageTarget.Key)
        {
            if (!int.TryParse(_index.text, out int keyIndex)
                || keyIndex < 0 || keyIndex >= PhotoTarget.PhotoKeycapCount)
            {
                PhotoTarget.ClearPhotoImagePreview();
                RefreshImageStateLabel(false, -1);
                return;
            }

            bool previewed = PhotoTarget.PreviewKeycapImage(_texture, keyIndex, _cropOffset, _cropZoom);
            RefreshImageStateLabel(previewed, keyIndex);
            return;
        }

        bool allPreviewed = PhotoTarget.PreviewKeycapImage(_texture, -1, _cropOffset, _cropZoom);
        RefreshImageStateLabel(allPreviewed, -1);
    }

    /// <summary>
    /// previewed와 keyIndex를 사용해 현재 사진의 미리보기 또는 적용 완료 상태를 표시한다.
    /// 마지막으로 적용한 이미지, 대상과 배치를 비교해 이미지나 배치를 수정하면 미적용 표시로 되돌린다.
    /// </summary>
    private void RefreshImageStateLabel(bool previewed, int keyIndex)
    {
        if (_imageStateLabel == null) return;
        bool applied = previewed && _appliedPreviewTexture != null && _appliedPreviewTexture == _texture
            && _appliedPreviewTarget == _selectedTarget && _appliedPreviewIndex == keyIndex
            && _appliedPreviewOffset.Equals(_cropOffset) && _appliedPreviewZoom.Equals(_cropZoom);
        _imageStateLabel.text = !_hasSelectedImage ? "이미지 미선택"
            : !previewed ? "미리보기 불가 · 적용 대상을 확인하세요."
            : applied ? "적용 완료 · 이미지가 적용됨"
            : "미리보기 중 · 아직 적용되지 않음";
        _imageStateLabel.color = applied ? new Color32(59, 217, 196, 255)
            : previewed ? new Color32(255, 200, 96, 255) : new Color32(140, 155, 170, 255);
    }

    /// <summary>현재 이미지와 크롭 입력 상태를 조회해 state에 실행 가능한 이동 및 확대 조작을 추가한다. 이미지를 변경하지 않고 표시 여부를 반환한다.</summary>
    public bool CollectControlHints(Game.Player.WorkshopControlHintState state)
    {
        if (_isCustomizing && _isWaitingForKeyInput && Keyboard.current != null)
            state.Add("키보드 입력", "적용할 키 입력");
        if (!_isCustomizing || !_hasSelectedImage || _cropInput == null || !_cropInput.isActiveAndEnabled) return false;
        bool over = RectTransformUtility.RectangleContainsScreenPoint(_cropFrame, InGameCursor.Position,
            null);
        if (!_cropInput.IsDragging && !over) return false;
        state.Title = "이미지 크롭";
        state.Add(_cropInput.IsDragging ? "좌클릭 유지·이동" : "좌클릭 드래그", "이미지 위치 조절");
        if (over) state.Add("마우스 휠", "이미지 확대·축소");
        if (_cropInput.IsDragging) state.Add("좌클릭 놓기", "크롭 이동 종료");
        return true;
    }

    /// <summary>
    /// crop 영역의 포인터 이동량을 이미지 위치에 반영한다.
    /// 프레임 기준 delta를 사용하고 두 미리보기를 다시 그린다.
    /// </summary>
    private void HandleCropDragged(Vector2 delta)
    {
        if (!_hasSelectedImage) return;
        _cropOffset += delta;
        RefreshCropVisuals();
    }

    /// <summary>
    /// 포인터 위치를 기준으로 휠 입력을 이미지 확대·축소에 반영한다.
    /// pointerPosition과 wheelDelta를 사용해 배율과 이미지 중심을 갱신한다.
    /// </summary>
    private void HandleCropZoomed(Vector2 pointerPosition, float wheelDelta)
    {
        if (!_hasSelectedImage || Mathf.Approximately(wheelDelta, 0f)) return;
        float previousZoom = _cropZoom;
        float wheelSteps = Mathf.Abs(wheelDelta) > 10f ? wheelDelta / 120f : wheelDelta;
        float sensitivity = Mathf.Clamp(_wheelZoomSensitivity, 0.01f, 0.2f);
        float zoomFactor = Mathf.Pow(1f + sensitivity, Mathf.Clamp(wheelSteps, -10f, 10f));
        _cropZoom = Mathf.Clamp(previousZoom * zoomFactor, 0.1f, 10f);
        float adjustedFactor = _cropZoom / previousZoom;
        _cropOffset = pointerPosition - (pointerPosition - _cropOffset) * adjustedFactor;
        RefreshCropVisuals();
    }

    /// <summary>
    /// 프레임 크기에 맞춰 회색 점선을 그리고 남는 선분을 숨긴다.
    /// size는 대상 메시 비율에 맞춘 크기이며 점선 UI를 갱신한다.
    /// </summary>
    private void DrawCropFrame(Vector2 size)
    {
        int horizontalCount = Mathf.Max(1, Mathf.CeilToInt(size.x / 11f));
        int verticalCount = Mathf.Max(1, Mathf.CeilToInt(size.y / 11f));
        int requiredCount = horizontalCount * 2 + verticalCount * 2;
        while (_cropDashes.Count < requiredCount)
        {
            RectTransform dash = new GameObject("CropFrameDash", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            dash.SetParent(_cropEditor, false);
            dash.anchorMin = dash.anchorMax = dash.pivot = new Vector2(0.5f, 0.5f);
            Image dashImage = dash.GetComponent<Image>();
            dashImage.color = new Color(0.66f, 0.72f, 0.76f, 0.95f);
            dashImage.raycastTarget = false;
            _cropDashes.Add(dash);
        }

        int dashIndex = 0;
        float horizontalDashWidth = size.x / horizontalCount * 0.55f;
        for (int index = 0; index < horizontalCount; index++)
        {
            float x = -size.x * 0.5f + (index + 0.5f) * size.x / horizontalCount;
            SetCropDash(dashIndex++, new Vector2(x, size.y * 0.5f), new Vector2(horizontalDashWidth, 2f));
            SetCropDash(dashIndex++, new Vector2(x, -size.y * 0.5f), new Vector2(horizontalDashWidth, 2f));
        }

        float verticalDashHeight = size.y / verticalCount * 0.55f;
        for (int index = 0; index < verticalCount; index++)
        {
            float y = -size.y * 0.5f + (index + 0.5f) * size.y / verticalCount;
            SetCropDash(dashIndex++, new Vector2(-size.x * 0.5f, y), new Vector2(2f, verticalDashHeight));
            SetCropDash(dashIndex++, new Vector2(size.x * 0.5f, y), new Vector2(2f, verticalDashHeight));
        }

        for (int index = dashIndex; index < _cropDashes.Count; index++) _cropDashes[index].gameObject.SetActive(false);
    }

    /// <summary>
    /// 지정한 점선 선분의 중심 위치와 크기를 설정한다.
    /// index, center와 size를 사용해 해당 UI 선분을 활성화한다.
    /// </summary>
    private void SetCropDash(int index, Vector2 center, Vector2 size)
    {
        RectTransform dash = _cropDashes[index];
        dash.gameObject.SetActive(true);
        dash.anchoredPosition = center;
        dash.sizeDelta = size;
    }

    /// <summary>
    /// 키 인덱스 변경을 받아 적용 가능 상태와 프레임을 갱신한다.
    /// value를 입력란과 동기화하고 선택된 키의 크기를 미리보기에 반영한다.
    /// </summary>
    private void HandleKeyIndexChanged(string value)
    {
        if (_index.text != value)
            _index.text = value;
        RefreshEmbeddedPanel();
    }

    /// <summary>
    /// 빌드용 파일 경로 입력란의 표시 상태를 전환한다.
    /// 접힘 상태를 바꾸고 뒤쪽 섹션의 위치를 다시 계산한다.
    /// </summary>
    private void TogglePathInput()
    {
        _pathExpanded = !_pathExpanded;
        RefreshEmbeddedPanel();
    }

    /// <summary>
    /// parent 아래 독립 테스트 씬용 이미지 선택, 적용, 제거 및 저장 UI를 만든다.
    /// 생성한 패널과 입력란 참조를 저장하며 편집 모드의 내장 패널은 별도 생성 함수를 사용한다.
    /// </summary>
    private void CreatePanel(Transform parent)
    {
        var panel = Rect("Photo customization", parent, 20, 20, 340, 710);
        _customizationPanel = panel.gameObject;
        if (!_embedded)
            panel.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.96f);
        Label(panel, "KEYBOARD PHOTO", 18, 18, 304, 28, 23);
        Label(panel, "Choose PNG / JPG, then apply.\nKeyboard damage input is paused.", 18, 56, 304, 48, 15);
        _preview = Rect("Preview", panel, 18, 112, 304, 150).gameObject.AddComponent<RawImage>();
        _preview.color = Color.gray;
        _path = Field(panel, "Image file path", 18, 276, 304);
        Button(panel, "Choose image", 18, 320, 304, ChooseImage);
        Button(panel, "Apply to ALL KEYCAPS", 18, 366, 304, () => ApplyImage(false, -1));
        Button(panel, "Apply to CASE", 18, 412, 304, () => ApplyImage(true, -1));
        _index = Field(panel, "0", 18, 458, 64);
        _index.text = "0";
        _index.contentType = InputField.ContentType.IntegerNumber;
        Button(panel, "Apply to KEY index", 92, 458, 230, ApplySelectedKey);
        Button(panel, "Clear keycaps", 18, 504, 147, () => { PhotoTarget.ClearKeycapImage(); ObserveImage("Remove", "AllKeycapsCanvas", -1, true); _message.text = "Keycap photos cleared."; });
        Button(panel, "Clear case", 175, 504, 147, () => { PhotoTarget.ClearCaseImage(); ObserveImage("Remove", "CaseWhole", -1, true); _message.text = "Case photo cleared."; });
        Button(panel, "Save", 18, 550, 147, SaveCustomization);
        Button(panel, "Load", 175, 550, 147, LoadCustomization);
        Button(panel, "Finish customization", 18, 596, 304, EndCustomization);
        _message = Label(panel, "Ready. All keycaps form one photo canvas.", 18, 642, 304, 60, 15);
    }

    /// <summary>
    /// 입력 없이 사진 커스터마이징을 시작한다.
    /// 반환값 없이 편집 패널을 열고 키보드 타건 입력을 활성 상태로 유지한다.
    /// </summary>
    public void BeginCustomization()
    {
        if (_isCustomizing || PhotoTarget == null) return;
        _input = _assemblyPhotoTarget == null ? _storage.Customization.GetComponent<KeyboardInputController>() : null;
        if (_embedded && _input != null) _previousInput = _input.InputEnabled;
        _isCustomizing = true;
        EventSystem.current.SetSelectedGameObject(null);
        if (_input != null) _input.InputEnabled = true;
        if (_startPanel != null) _startPanel.SetActive(false);
        _customizationPanel.SetActive(true);
        RefreshEmbeddedPanel();
    }

    /// <summary>
    /// 입력 없이 사진 커스터마이징을 종료한다.
    /// 반환값 없이 적용한 외관을 유지하고 패널을 접으며 키보드 파괴 입력을 활성화한다.
    /// </summary>
    public void EndCustomization()
    {
        if (_embedded)
        {
            CloseRequested?.Invoke();
            return;
        }
        EventSystem.current.SetSelectedGameObject(null);
        _customizationPanel.SetActive(false);
        _startPanel.SetActive(true);
        if (_input != null) _input.InputEnabled = true;
        _isCustomizing = false;
    }

    /// <summary>
    /// 내장 사진 편집 패널을 닫고 진입 전 키보드 입력을 복원한다.
    /// 현재 활성 상태를 사용하며 다른 편집 항목 선택이나 오버레이 종료 시 호출한다.
    /// </summary>
    public void HideEmbedded()
    {
        if (!_isCustomizing) return;
        _isCustomizing = false;
        _isWaitingForKeyInput = false;
        RefreshKeyInputControls();
        if (PhotoTarget != null)
            PhotoTarget.ClearPhotoImagePreview();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _customizationPanel.SetActive(false);
        if (_input != null) _input.InputEnabled = _previousInput;
    }

    /// <summary>
    /// screenPosition이 현재 열린 사진 패널에 포함되는지 반환한다.
    /// 오버레이 Canvas의 카메라를 사용해 키보드 회전 및 확대 입력과 UI 입력을 구분한다.
    /// </summary>
    public bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (!_isCustomizing) return false;
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_customizationPanel.transform, screenPosition, camera);
    }

    /// <summary>
    /// 기존 저장 함수를 호출해 단일 파일을 덮어쓴다.
    /// 별도 저장 형식을 만들지 않으며 처리 결과를 안내 문구에 표시한다.
    /// </summary>
    private void SaveCustomization()
    {
        bool saved = _storage.SaveCustomization();
        Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "Save", "CustomImage", success: saved);
        _message.text = _embedded && saved ? "커스터마이징을 저장했습니다." : _storage.LastMessage;
    }

    /// <summary>
    /// 기존 Load 함수를 사용해 저장 상태를 현재 키보드에 적용한다.
    /// 결과를 안내하며 성공 시 선택 이미지를 초기화하고 선택 UI 갱신 이벤트를 전달한다.
    /// </summary>
    private void LoadCustomization()
    {
        if (PhotoTarget != null) PhotoTarget.ClearPhotoImagePreview();
        bool loaded = _storage.LoadCustomization();
        Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "Load", "CustomImage", success: loaded);
        _message.text = _embedded && loaded ? "저장된 커스터마이징을 불러왔습니다." : _storage.LastMessage;
        if (!loaded) return;
        if (_embedded)
        {
            if (_texture != null) Destroy(_texture);
            _texture = null;
            _path.text = "";
            _hasSelectedImage = false;
            _cropOffset = Vector2.zero;
            _cropZoom = 1f;
            _fileName.text = "새 이미지를 선택해 배치를 편집하세요";
            _fileDimensions.text = "—";
            RefreshEmbeddedPanel();
        }
        CustomizationLoaded?.Invoke();
    }

    void OnDestroy()
    {
        if (_cropInput != null)
        {
            _cropInput.Dragged -= HandleCropDragged;
            _cropInput.Zoomed -= HandleCropZoomed;
        }
        if (PhotoTarget != null)
            PhotoTarget.ClearPhotoImagePreview();
        if (_input != null && (!_embedded || _isCustomizing)) _input.InputEnabled = _previousInput;
        if (_texture != null) Destroy(_texture);
        if (_previewCheckerTexture != null) Destroy(_previewCheckerTexture);
        if (_canvas != null) Destroy(_canvas);
    }

    /// <summary>파일 선택 창에서 사진 경로를 받아 입력란과 미리보기를 변경한다. 반환값은 없다.</summary>
    private void ChooseImage()
    {
        Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "ImageAction", "CustomImage", "ChooseStarted");
        Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "ExternalDialogStart");
        string path;
        try { path = KeyboardImageFileDialog.ChooseImage(); }
        catch (Exception exception) when (exception is InvalidOperationException || exception is PlatformNotSupportedException
            || exception is DllNotFoundException || exception is EntryPointNotFoundException)
        {
            _message.text = exception.Message;
            return;
        }
        finally { Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "ExternalDialogEnd"); }
        if (string.IsNullOrEmpty(path))
        {
            Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "ImageAction", "CustomImage", "ChooseCancelled");
            return;
        }
        _path.text = path;
        if (_embedded) HandleImagePathEdited(path);
        else ShowPreview();
    }

    /// <summary>
    /// 편집 경로의 확장자를 확인하고 선택된 이미지 미리보기를 읽는다.
    /// path를 사용하며 지원하지 않는 확장자나 빈 입력은 적용 상태에서 제외한다.
    /// </summary>
    private void HandleImagePathEdited(string path)
    {
        if (!_embedded)
            return;

        string selectedPath = path.Trim();
        if (selectedPath.Length == 0)
        {
            ClearEmbeddedPreview();
            _message.text = "이미지를 선택하거나 경로를 입력해 주세요.";
            RefreshEmbeddedPanel();
            return;
        }

        string extension = Path.GetExtension(selectedPath);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            ClearEmbeddedPreview();
            _path.GetComponent<Image>().color = new Color(0.3f, 0.12f, 0.15f);
            _message.text = "파일 경로는 PNG 또는 JPG 이미지여야 합니다.";
            RefreshEmbeddedPanel();
            return;
        }

        _path.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.12f);
        _path.text = selectedPath;
        ShowPreview();
    }

    /// <summary>
    /// HTML 편집 패널의 적용 버튼에서 선택 범위에 따라 사진 적용을 실행한다.
    /// 현재 이미지 대상과 키 인덱스를 사용하며 선택되지 않은 사진은 적용하지 않는다.
    /// </summary>
    private void ApplyEmbeddedImage()
    {
        if (!_hasSelectedImage)
        {
            _message.text = "먼저 PNG 또는 JPG 이미지를 선택해 주세요.";
            return;
        }

        if (_selectedTarget == ImageTarget.Key)
        {
            if (!int.TryParse(_index.text, out int index) || index < 0)
            {
                _message.text = "키 인덱스에 0 이상의 정수를 입력해 주세요.";
                RefreshEmbeddedPanel();
                return;
            }

            ApplyImage(false, index);
            return;
        }

        ApplyImage(_selectedTarget == ImageTarget.Case, -1);
    }

    /// <summary>
    /// 현재 키보드의 전체 키캡 사진을 지우고 HTML 편집 패널에 결과를 표시한다.
    /// 기존 재질을 복원하며 사진 제거 telemetry를 기록한다.
    /// </summary>
    private void ClearKeycapImages()
    {
        _appliedPreviewTexture = null;
        PhotoTarget.ClearPhotoImagePreview();
        PhotoTarget.ClearKeycapImage();
        RefreshImageStateLabel(false, -1);
        ObserveImage("Remove", "AllKeycapsCanvas", -1, true);
        _message.text = _embedded ? "키캡 이미지가 지워졌습니다." : "Keycap photos cleared.";
    }

    /// <summary>
    /// 현재 키보드의 케이스 사진을 지우고 HTML 편집 패널에 결과를 표시한다.
    /// 기존 재질을 복원하며 사진 제거 telemetry를 기록한다.
    /// </summary>
    private void ClearCaseImage()
    {
        _appliedPreviewTexture = null;
        PhotoTarget.ClearPhotoImagePreview();
        PhotoTarget.ClearCaseImage();
        RefreshImageStateLabel(false, -1);
        ObserveImage("Remove", "CaseWhole", -1, true);
        _message.text = _embedded ? "케이스 이미지가 지워졌습니다." : "Case photo cleared.";
    }

    /// <summary>
    /// 선택 이미지를 지우고 적용 가능한 배치 편집 상태를 해제한다.
    /// 이전 텍스처와 임시 키보드 배치를 정리하며 파일 정보를 초기화한다.
    /// </summary>
    private void ClearEmbeddedPreview()
    {
        if (PhotoTarget != null)
            PhotoTarget.ClearPhotoImagePreview();
        _hasSelectedImage = false;
        _cropOffset = Vector2.zero;
        _cropZoom = 1f;
        if (_texture != null)
        {
            Destroy(_texture);
            _texture = null;
        }

        RefreshCropVisuals();
        _fileName.text = "파일을 선택해 주세요";
        _fileDimensions.text = "—";
        _path.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.12f);
    }

    /// <summary>숫자 입력란의 index를 검사해 개별 키 사진 적용을 실행하고 오류를 표시한다. 반환값은 없다.</summary>
    private void ApplySelectedKey()
    {
        if (int.TryParse(_index.text, out int index) && index >= 0) ApplyImage(false, index);
        else _message.text = "Enter a key index of 0 or greater.";
    }

    /// <summary>
    /// caseImage와 index, 경로 입력을 공개 API로 전달해 사진을 적용하고 결과를 표시한다.
    /// 독립 테스트 화면에서는 성공한 입력 이미지의 미리보기도 갱신한다.
    /// </summary>
    private void ApplyImage(bool caseImage, int index)
    {
        var customization = PhotoTarget;
        bool replacing = false;
        foreach (string[] value in customization.GetCustomizationLogValues())
        {
            bool matches = caseImage ? value[0] == "Image:CaseWhole"
                : index < 0 ? value[0].StartsWith("Image:Keycap:", StringComparison.Ordinal)
                : value[0] == "Image:Keycap:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (matches && value[1] != "None") replacing = true;
        }
        bool applied = caseImage
            ? _embedded
                ? customization.LoadCaseImage(_path.text, _cropOffset, _cropZoom)
                : customization.LoadCaseImage(_path.text)
            : _embedded
                ? customization.LoadKeycapImage(_path.text, index, _cropOffset, _cropZoom)
                : customization.LoadKeycapImage(_path.text, index);
        ObserveImage(replacing ? "Replace" : "Apply", caseImage ? "CaseWhole" : index < 0 ? "AllKeycapsCanvas" : "SingleKeycap", index, applied);
        _message.text = applied
            ? (_embedded ? (_assemblyPhotoTarget != null ? "이미지를 적용했습니다." : "이미지를 적용했습니다. 저장하면 배치가 유지됩니다.") : "Photo applied. Save to keep the layout.")
            : customization.ImageError;
        if (applied && _embedded)
        {
            _appliedPreviewTexture = _texture;
            _appliedPreviewTarget = caseImage ? ImageTarget.Case : index < 0 ? ImageTarget.AllKeycaps : ImageTarget.Key;
            _appliedPreviewIndex = index;
            _appliedPreviewOffset = _cropOffset;
            _appliedPreviewZoom = _cropZoom;
            RefreshLiveKeyboardPreview();
        }
        if (applied && !_embedded) ShowPreview();
    }

    /// <summary>
    /// 경로의 PNG 또는 JPEG 사진을 임시 Texture로 읽어 배치 편집기와 파일 정보를 갱신한다.
    /// 용량과 이미지 크기를 제한하며 실패하면 선택 상태를 해제하고 오류를 표시한다.
    /// </summary>
    private void ShowPreview()
    {
        if (!_embedded)
        {
            ShowStandalonePreview();
            return;
        }

        Texture2D texture = null;
        try
        {
            FileInfo file = new FileInfo(_path.text.Trim());
            if (!file.Exists || file.Length > 32 * 1024 * 1024)
                throw new InvalidOperationException("이미지가 없거나 파일 크기가 32MB를 초과합니다.");

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(file.FullName)))
                throw new InvalidOperationException("지원하지 않는 이미지 형식입니다.");
            if (texture.width > 8192 || texture.height > 8192)
                throw new InvalidOperationException("이미지의 가로와 세로는 각각 8,192px 이하여야 합니다.");

            if (PhotoTarget != null) PhotoTarget.ClearPhotoImagePreview();
            if (_texture != null) Destroy(_texture);
            _texture = texture;
            texture = null;
            _cropOffset = Vector2.zero;
            _cropZoom = 1f;
            ObserveImage("LoadSucceeded", "", -1, true);
            _hasSelectedImage = true;
            _fileName.text = file.Name.Length > 36 ? file.Name.Substring(0, 33) + "..." : file.Name;
            _fileDimensions.text = _texture.width + " × " + _texture.height;
            _message.text = "이미지를 선택했습니다. 적용 위치를 고르고 적용 버튼을 눌러 주세요.";
            RefreshEmbeddedPanel();
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
            || exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException)
        {
            if (texture != null) Destroy(texture);
            ShowPreviewError(exception.Message);
        }
    }

    /// <summary>
    /// 독립 테스트 씬의 기존 경로 미리보기 동작을 유지한다.
    /// 32MB 제한과 304×150 영역을 사용하며 결과를 테스트 패널에 표시한다.
    /// </summary>
    private void ShowStandalonePreview()
    {
        try
        {
            FileInfo file = new FileInfo(_path.text);
            if (!file.Exists || file.Length > 32 * 1024 * 1024)
            {
                ObserveImage("LoadFailed", "", -1, false);
                _message.text = "Choose an image under 32 MB.";
                return;
            }

            Texture2D texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(file.FullName)))
            {
                ObserveImage("LoadFailed", "", -1, false);
                Destroy(texture);
                _message.text = "Unsupported image.";
                return;
            }

            if (_texture != null) Destroy(_texture);
            _texture = texture;
            ObserveImage("LoadSucceeded", "", -1, true);
            _preview.texture = texture;
            _preview.color = Color.white;
            float aspect = (float)texture.width / texture.height;
            _preview.rectTransform.sizeDelta = aspect > 304f / 150f
                ? new Vector2(304f, 304f / aspect)
                : new Vector2(150f * aspect, 150f);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
        {
            ObserveImage("LoadFailed", "", -1, false);
            _message.text = exception.Message;
        }
    }

    /// <summary>
    /// 사진 미리보기 실패를 기록하고 현재 파일 선택 상태를 해제한다.
    /// message를 안내 문구로 표시하며 적용 버튼을 비활성화한다.
    /// </summary>
    private void ShowPreviewError(string message)
    {
        ObserveImage("LoadFailed", "", -1, false);
        if (_embedded)
        {
            ClearEmbeddedPreview();
            RefreshEmbeddedPanel();
        }

        _message.text = message;
    }

    /// <summary>
    /// action의 이미지 동작과 scope, index 및 success 결과를 관찰자에게 전달한다.
    /// 원본 경로나 이미지 바이트는 전송하지 않으며 개별 대상은 키캡 인덱스로 구분한다.
    /// </summary>
    private void ObserveImage(string action, string scope, int index, bool success)
    {
        Game.Logging.CustomizationTelemetry.Emit(_storage?.Customization, "ImageAction", "CustomImage", action,
            success: success, scope: scope, key: index >= 0 ? index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
    }

    /// <summary>parent 아래 지정한 이름, 위치, 크기의 왼쪽 위 기준 UI 영역을 생성해 반환한다.</summary>
    private RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>parent의 지정 영역에 text와 size를 사용한 라벨을 생성하고 Text 참조를 반환한다.</summary>
    private Text Label(Transform parent, string text, float x, float y, float width, float height, int size)
    {
        var label = Rect("Label", parent, x, y, width, height).gameObject.AddComponent<Text>();
        label.font = _font;
        label.text = text;
        label.fontSize = size;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>parent의 지정 위치에 text 버튼을 만들고 action을 C# 이벤트로 연결한다. 반환값은 없다.</summary>
    private void Button(Transform parent, string text, float x, float y, float width, Action action)
    {
        var rect = Rect(text, parent, x, y, width, 36);
        rect.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.36f, 0.48f);
        rect.gameObject.AddComponent<KeyboardCustomizationTestButton>().Clicked += action;
        Label(rect, text, 10, 7, width - 20, 24, 16);
    }

    /// <summary>parent의 지정 위치에 hint를 표시하는 입력란을 생성해 InputField 참조를 반환한다.</summary>
    private InputField Field(Transform parent, string hint, float x, float y, float width)
    {
        var rect = Rect(hint, parent, x, y, width, 36);
        rect.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.18f, 0.23f);
        var field = rect.gameObject.AddComponent<InputField>();
        field.textComponent = Label(rect, "", 8, 7, width - 16, 24, 15);
        var placeholder = Label(rect, hint, 8, 7, width - 16, 24, 15);
        placeholder.color = Color.gray;
        field.placeholder = placeholder;
        return field;
    }
}
