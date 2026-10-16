using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

using KeyboardModeling;

public sealed class KeyboardCustomizationTestUI : MonoBehaviour
{
    [Header("Storage")]
    [SerializeField] private KeyboardCustomizationStorage _storage;
    private InputField _path;
    private InputField _index;
    private RawImage _preview;
    private Text _message;
    private Font _font;
    private Texture2D _texture;
    private KeyboardInputController _input;
    private bool _previousInput;
    private GameObject _canvas;
    private GameObject _customizationPanel;
    private GameObject _startPanel;
    private GameObject _savedPanel;
    private RawImage _savedPreview;
    private Texture2D _savedTexture;
    private bool _embedded;
    private bool _isCustomizing;

    public event Action CloseRequested;
    public event Action CustomizationLoaded;

    void Start()
    {
        if (_embedded) return;
        _input = _storage.Customization.GetComponent<KeyboardInputController>();
        _previousInput = _input.InputEnabled;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _canvas = new GameObject("KeyboardPhotoTestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 1;
        if (EventSystem.current == null)
            new GameObject("PhotoTestEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(_canvas.transform);
        CreatePanel(_canvas.transform);
        var startPanel = Rect("Customization start", _canvas.transform, 20, 20, 340, 82);
        _startPanel = startPanel.gameObject;
        startPanel.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.96f);
        Button(startPanel, "Start customization", 18, 18, 304, BeginCustomization);
        BeginCustomization();
    }

    /// <summary>
    /// storage를 사용해 기존 사진 편집 기능을 현재 오버레이의 오른쪽 패널과 상단 단일 저장 슬롯으로 구성한다.
    /// 별도 Canvas를 생성하지 않으며 표시와 키보드 대상은 WorkShop2의 편집 상태에 연결한다.
    /// </summary>
    public void InitializeEmbedded(KeyboardCustomizationStorage storage)
    {
        _embedded = true;
        _storage = storage;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CreatePanel(transform);
        RectTransform panel = (RectTransform)_customizationPanel.transform;
        panel.anchorMin = panel.anchorMax = Vector2.one;
        panel.anchoredPosition = new Vector2(-368f, -180f);

        RectTransform saved = Rect("Saved customization", transform, 0f, 24f, 304f, 132f);
        saved.anchorMin = saved.anchorMax = new Vector2(0.5f, 1f);
        saved.pivot = new Vector2(0.5f, 1f);
        saved.gameObject.AddComponent<Image>().color = Color.clear;
        saved.gameObject.AddComponent<KeyboardCustomizationTestButton>().Clicked += LoadCustomization;
        _savedPanel = saved.gameObject;
        _savedPreview = Rect("Saved image", saved, 0f, 0f, 304f, 104f).gameObject.AddComponent<RawImage>();
        _savedPreview.raycastTarget = false;
        Label(saved, "Saved customization · Click to load", 0f, 108f, 304f, 24f, 16);
        _customizationPanel.SetActive(false);
        _savedPanel.SetActive(false);
    }

    /// <summary>
    /// parent 아래에 테스트 씬과 동일한 이미지 선택, 적용, 제거 및 Save/Load UI를 만든다.
    /// 생성한 패널과 입력란 참조를 저장하며 내장 패널은 별도의 배경 박스를 사용하지 않는다.
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
        Button(panel, "Clear keycaps", 18, 504, 147, () => { _storage.Customization.ClearKeycapImage(); _message.text = "Keycap photos cleared."; });
        Button(panel, "Clear case", 175, 504, 147, () => { _storage.Customization.ClearCaseImage(); _message.text = "Case photo cleared."; });
        Button(panel, "Save", 18, 550, 147, SaveCustomization);
        Button(panel, "Load", 175, 550, 147, LoadCustomization);
        Button(panel, "Finish customization", 18, 596, 304, EndCustomization);
        _message = Label(panel, "Ready. All keycaps form one photo canvas.", 18, 642, 304, 60, 15);
    }

    /// <summary>
    /// 입력 없이 사진 커스터마이징을 시작한다.
    /// 반환값 없이 편집 패널을 열고 키보드 파괴 입력을 비활성화한다.
    /// </summary>
    public void BeginCustomization()
    {
        if (_isCustomizing || _storage.Customization == null) return;
        _input = _storage.Customization.GetComponent<KeyboardInputController>();
        if (_embedded) _previousInput = _input.InputEnabled;
        _isCustomizing = true;
        EventSystem.current.SetSelectedGameObject(null);
        _input.InputEnabled = false;
        if (_startPanel != null) _startPanel.SetActive(false);
        _customizationPanel.SetActive(true);
        if (_embedded) RefreshSavedPreview();
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
        _input.InputEnabled = true;
        _isCustomizing = false;
    }

    /// <summary>
    /// 내장 사진 편집 패널과 저장 슬롯을 닫고 진입 전 키보드 입력을 복원한다.
    /// 현재 활성 상태를 사용하며 다른 편집 항목 선택이나 오버레이 종료 시 호출한다.
    /// </summary>
    public void HideEmbedded()
    {
        if (!_isCustomizing) return;
        _isCustomizing = false;
        EventSystem.current.SetSelectedGameObject(null);
        _customizationPanel.SetActive(false);
        _savedPanel.SetActive(false);
        if (_input != null) _input.InputEnabled = _previousInput;
    }

    /// <summary>
    /// screenPosition이 현재 열린 사진 패널 또는 저장 슬롯에 포함되는지 반환한다.
    /// 오버레이 Canvas의 카메라를 사용해 키보드 회전 및 확대 입력과 UI 입력을 구분한다.
    /// </summary>
    public bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (!_isCustomizing) return false;
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_customizationPanel.transform, screenPosition, camera)
            || (_savedPanel.activeSelf && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_savedPanel.transform, screenPosition, camera));
    }

    /// <summary>
    /// 기존 저장 함수를 호출해 단일 파일을 덮어쓰고 성공 시 상단 미리보기를 갱신한다.
    /// 별도 저장 형식을 만들지 않으며 처리 결과를 안내 문구에 표시한다.
    /// </summary>
    private void SaveCustomization()
    {
        bool saved = _storage.SaveCustomization();
        _message.text = _storage.LastMessage;
        if (saved && _embedded) RefreshSavedPreview();
    }

    /// <summary>
    /// 기존 Load 함수를 사용해 저장 상태를 현재 키보드에 적용한다.
    /// 결과를 안내하며 성공 시 저장 사진을 미리보기로 표시하고 선택 UI 갱신 이벤트를 전달한다.
    /// </summary>
    private void LoadCustomization()
    {
        bool loaded = _storage.LoadCustomization();
        _message.text = _storage.LastMessage;
        if (!loaded) return;
        if (_embedded)
        {
            RefreshSavedPreview();
            if (_texture != null) Destroy(_texture);
            _texture = null;
            _path.text = "";
            _preview.texture = _savedTexture;
            _preview.color = _savedTexture != null ? Color.white : Color.gray;
            FitPreview(_preview, _savedTexture, new Vector2(304f, 150f));
        }
        CustomizationLoaded?.Invoke();
    }

    /// <summary>
    /// 기존 저장 파일에서 대표 사진을 읽어 상단의 단일 저장 슬롯을 갱신한다.
    /// 이전 미리보기 텍스처를 해제하며 저장 파일이 없으면 슬롯을 숨긴다.
    /// </summary>
    private void RefreshSavedPreview()
    {
        bool showingSavedImage = _savedTexture != null && _preview.texture == _savedTexture;
        if (showingSavedImage) _preview.texture = null;
        if (_savedTexture != null) Destroy(_savedTexture);
        _savedTexture = _storage.LoadSavedPreview();
        if (showingSavedImage)
        {
            _preview.texture = _savedTexture;
            _preview.color = _savedTexture != null ? Color.white : Color.gray;
            FitPreview(_preview, _savedTexture, new Vector2(304f, 150f));
        }
        _savedPreview.texture = _savedTexture;
        _savedPreview.color = _savedTexture != null ? Color.white : Color.clear;
        FitPreview(_savedPreview, _savedTexture, new Vector2(304f, 104f));
        _savedPanel.SetActive(_storage.HasSave);
    }

    /// <summary>
    /// image의 종횡비를 유지하도록 preview를 size 안에 맞춘다.
    /// 사진이 없으면 입력 크기를 사용하고 미리보기 RectTransform 크기를 변경한다.
    /// </summary>
    private void FitPreview(RawImage preview, Texture2D image, Vector2 size)
    {
        float ratio = image != null ? Mathf.Min(size.x / image.width, size.y / image.height) : 1f;
        preview.rectTransform.sizeDelta = image != null ? new Vector2(image.width, image.height) * ratio : size;
    }

    void OnDestroy()
    {
        if (_input != null && (!_embedded || _isCustomizing)) _input.InputEnabled = _previousInput;
        if (_texture != null) Destroy(_texture);
        if (_savedTexture != null) Destroy(_savedTexture);
        if (_canvas != null) Destroy(_canvas);
    }

    /// <summary>파일 선택 창에서 사진 경로를 받아 입력란과 미리보기를 변경한다. 반환값은 없다.</summary>
    private void ChooseImage()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Choose keyboard photo", "", "png,jpg,jpeg");
        if (string.IsNullOrEmpty(path)) return;
        _path.text = path;
        ShowPreview();
#else
        _message.text = "Enter an image file path, then click Apply.";
#endif
    }

    /// <summary>숫자 입력란의 index를 검사해 개별 키 사진 적용을 실행하고 오류를 표시한다. 반환값은 없다.</summary>
    private void ApplySelectedKey()
    {
        if (int.TryParse(_index.text, out int index) && index >= 0) ApplyImage(false, index);
        else _message.text = "Enter a key index of 0 or greater.";
    }

    /// <summary>caseImage와 index, 경로 입력을 공개 API로 전달하고 적용 결과와 미리보기를 변경한다. 반환값은 없다.</summary>
    private void ApplyImage(bool caseImage, int index)
    {
        var customization = _storage.Customization;
        bool applied = caseImage ? customization.LoadCaseImage(_path.text) : customization.LoadKeycapImage(_path.text, index);
        _message.text = applied ? "Photo applied. Save to keep the layout." : customization.ImageError;
        if (applied) ShowPreview();
    }

    /// <summary>경로 입력의 사진을 임시 Texture로 읽어 미리보기를 갱신하고 이전 리소스를 해제한다. 반환값은 없다.</summary>
    private void ShowPreview()
    {
        try
        {
            var file = new System.IO.FileInfo(_path.text);
            if (!file.Exists || file.Length > 32 * 1024 * 1024) { _message.text = "Choose an image under 32 MB."; return; }
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(System.IO.File.ReadAllBytes(file.FullName))) { Destroy(texture); _message.text = "Unsupported image."; return; }
            if (_texture != null) Destroy(_texture);
            _texture = texture;
            _preview.texture = texture;
            _preview.color = Color.white;
            float aspect = (float)texture.width / texture.height;
            _preview.rectTransform.sizeDelta = aspect > 304f / 150f ? new Vector2(304, 304 / aspect) : new Vector2(150 * aspect, 150);
        }
        catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
        {
            _message.text = exception.Message;
        }
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
