using System.Collections.Generic;
using System.Globalization;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using KeyboardModeling;

namespace Game.Player
{
    public sealed class KeyboardLEDPanelUI : MonoBehaviour, IPointerDownHandler
    {
        private static readonly string[] _presetColors = { "#20DCC8", "#4EA7FF", "#AD79FF", "#FF6AA3", "#FFBD59", "#E8F4FF" };
        private static readonly string[] _patternNames = { "고정색 — Solid", "무지개 — Rainbow", "숨쉬기 — Breathing" };
        private static readonly string[] _patternDescriptions =
        {
            "선택한 색상을 모든 키에 일정하게 유지합니다.",
            "키보드 전체에 무지개 색상이 흐릅니다.\n선택한 색상은 별도로 유지됩니다.",
            "선택한 색상의 밝기가 호흡하듯 서서히 변합니다."
        };
        private static readonly Color _borderColor = new Color32(51, 66, 81, 255);
        private static readonly Color _mutedColor = new Color32(140, 155, 170, 255);
        private static readonly Color _fieldColor = new Color32(13, 21, 32, 255);

        [Header("Runtime State")]
        private KeyboardCustomization _customization;
        private KeyboardInputController _input;
        private Canvas _canvas;
        private RectTransform _panel;
        private Font _font;
        private bool _isOpen;
        private bool _previousInput;
        private Sprite _roundedSprite;
        private Sprite _circleSprite;
        private Sprite _frameSprite;
        private Sprite _cardSprite;
        private readonly List<Sprite> _sprites = new List<Sprite>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<Image> _presetFrames = new List<Image>();
        private KeyboardLEDColorPicker _colorPicker;
        private KeyboardLEDHexInput _hexInput;
        private KeyboardLEDSlider _intensitySlider;
        private KeyboardLEDSlider _speedSlider;
        private Image _hexBorder;
        private Image _colorChip;
        private Image _powerTrack;
        private Image _powerKnob;
        private Image _intensityFill;
        private Image _speedFill;
        private Text _powerText;
        private Text _rgbText;
        private Text _patternText;
        private Text _patternDescription;
        private Text _intensityText;
        private Text _speedText;
        private GameObject _patternMenu;

        /// <summary>
        /// 현재 오버레이 오른쪽에 HTML 레퍼런스의 LED 카드와 색상 및 효과 컨트롤을 구성한다.
        /// UI 전용 텍스처와 C# 이벤트를 준비하며 키보드 설정이나 입력 상태는 변경하지 않는다.
        /// </summary>
        public void Initialize()
        {
            _canvas = GetComponentInParent<Canvas>();
            _panel = (RectTransform)transform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = Vector2.one;
            _panel.anchoredPosition = new Vector2(-28f, -24f);
            _panel.sizeDelta = new Vector2(414f, 888f);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _roundedSprite = CreateRoundedSprite(Color.white, Color.white, 9f);
            _circleSprite = CreateRoundedSprite(Color.white, Color.white, 32f);
            _frameSprite = CreateRoundedSprite(Color.white, Color.white, 23f);
            _cardSprite = CreateRoundedSprite(new Color32(25, 35, 46, 255), new Color32(17, 25, 34, 255), 22f);
            Image frame = gameObject.AddComponent<Image>();
            frame.sprite = _frameSprite;
            frame.type = Image.Type.Sliced;
            frame.color = new Color32(53, 69, 84, 255);
            Shadow shadow = gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(0f, -10f);
            Image background = CreateImage("Card", _panel, 1f, 1f, 412f, 886f, Color.white);
            background.sprite = _cardSprite;
            Label(_panel, "▦  KEYBOARD WORKSHOP", 22f, 22f, 270f, 24f, 11, _mutedColor);
            Label(_panel, "CUSTOM / 02", 292f, 22f, 100f, 24f, 10, _mutedColor, TextAnchor.MiddleRight);
            Label(_panel, "LED 커스터마이징", 22f, 66f, 272f, 34f, 24, Color.white);
            Label(_panel, "내 키보드에 어울리는 빛을 디자인하세요.", 22f, 102f, 292f, 24f, 12, _mutedColor);
            _powerText = Label(_panel, "ON", 300f, 78f, 32f, 24f, 11, Color.white, TextAnchor.MiddleRight);
            KeyboardEditButton power = Button("Power", _panel, 344f, 78f, 43f, 24f, Color.white);
            power.transition = Selectable.Transition.None;
            power.Clicked += HandlePowerClicked;
            _powerTrack = (Image)power.targetGraphic;
            _powerKnob = CreateImage("Knob", power.transform, 23f, 4f, 16f, 16f, Color.white);
            _powerKnob.sprite = _circleSprite;
            _powerKnob.type = Image.Type.Simple;
            Label(_panel, "LED 색상", 22f, 142f, 180f, 22f, 13, Color.white);
            Label(_panel, "COLOR PICKER", 260f, 142f, 132f, 22f, 10, _mutedColor, TextAnchor.MiddleRight);
            RectTransform pickerRect = Rect("ColorPicker", _panel, 78f, 167f, 258f, 258f);
            _colorPicker = pickerRect.gameObject.AddComponent<KeyboardLEDColorPicker>();
            _colorPicker.Initialize(_circleSprite);
            _colorPicker.ColorChanged += HandlePickerColorChanged;
            CreateColorFields();
            CreatePresets();
            CreateImage("Divider", _panel, 22f, 559f, 370f, 1f, _borderColor);
            Label(_panel, "조명 패턴", 22f, 581f, 180f, 20f, 12, Color.white);
            Label(_panel, "EFFECT", 260f, 581f, 132f, 20f, 10, _mutedColor, TextAnchor.MiddleRight);
            KeyboardEditButton pattern = Button("Pattern", _panel, 22f, 610f, 370f, 42f, _fieldColor);
            pattern.Clicked += HandlePatternMenuClicked;
            _patternText = Label(pattern.transform, "", 13f, 0f, 310f, 42f, 12, Color.white);
            Label(pattern.transform, "⌄", 330f, 0f, 24f, 42f, 18, _mutedColor, TextAnchor.MiddleCenter);
            _patternDescription = Label(_panel, "", 22f, 660f, 370f, 36f, 10, _mutedColor);
            Label(_panel, "LED 밝기", 22f, 705f, 200f, 22f, 12, Color.white);
            _intensityText = Label(_panel, "", 258f, 705f, 134f, 22f, 12, _mutedColor, TextAnchor.MiddleRight);
            _intensitySlider = CreateSlider("Intensity", 735f, KeyboardCustomization.MaxLEDIntensity, out _intensityFill);
            _intensitySlider.ValueChanged += HandleIntensityChanged;
            Label(_panel, "0 · 최소", 22f, 760f, 160f, 18f, 10, _mutedColor);
            Label(_panel, "10 · 최대", 232f, 760f, 160f, 18f, 10, _mutedColor, TextAnchor.MiddleRight);
            Label(_panel, "애니메이션 속도", 22f, 791f, 200f, 22f, 12, Color.white);
            _speedText = Label(_panel, "", 258f, 791f, 134f, 22f, 12, _mutedColor, TextAnchor.MiddleRight);
            _speedSlider = CreateSlider("Speed", 821f, KeyboardCustomization.MaxLEDSpeed, out _speedFill);
            _speedSlider.ValueChanged += HandleSpeedChanged;
            Label(_panel, "0 · 느리게", 22f, 846f, 160f, 18f, 10, _mutedColor);
            Label(_panel, "10 · 빠르게", 232f, 846f, 160f, 18f, 10, _mutedColor, TextAnchor.MiddleRight);
            CreatePatternMenu();
        }

        /// <summary>
        /// customization의 현재 LED 값을 읽고 패널을 열며 해당 키보드의 파손 입력을 중지한다.
        /// 진입 전 입력 상태를 저장하고 UI 동기화 과정에서는 LED 설정 변경 이벤트를 발생시키지 않는다.
        /// </summary>
        public void Open(KeyboardCustomization customization)
        {
            if (_isOpen || customization == null) return;
            _customization = customization;
            _input = customization.GetComponent<KeyboardInputController>();
            _previousInput = _input.InputEnabled;
            _input.SetInputEnabled(false);
            _isOpen = true;
            EventSystem.current.SetSelectedGameObject(null);
            _patternMenu.SetActive(false);
            RefreshColor(true);
            RefreshPower();
            RefreshPattern();
            _intensitySlider.SetValueWithoutNotify(customization.LEDIntensity);
            _speedSlider.SetValueWithoutNotify(customization.LEDSpeed);
            RefreshValues();
            gameObject.SetActive(true);
        }

        /// <summary>
        /// LED 패널과 패턴 목록을 닫고 저장한 키보드 입력 상태를 복원한다.
        /// 적용된 LED 값은 유지하며 다른 편집 모드와 오버레이 종료에서 같은 정리를 사용한다.
        /// </summary>
        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            EventSystem.current.SetSelectedGameObject(null);
            if (_input != null) _input.SetInputEnabled(_previousInput);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// screenPosition이 열린 LED 카드 안에 있는지 Canvas 카메라를 사용해 반환한다.
        /// 키보드 회전, 확대 및 외부 클릭 닫기 입력을 LED 컨트롤 입력과 구분한다.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            return _isOpen && RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPosition, camera);
        }

        /// <summary>
        /// eventData의 좌클릭이 카드 빈 영역에 도달하면 패턴 선택 목록을 닫는다.
        /// LED 설정과 전체 편집 모드는 유지한다.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _patternMenu.SetActive(false);
        }

        void OnDisable()
        {
            Close();
        }

        void OnDestroy()
        {
            foreach (Sprite sprite in _sprites) Destroy(sprite);
            foreach (Texture2D texture in _textures) Destroy(texture);
        }

        /// <summary>
        /// HEX 입력과 RGB 표시 및 현재 색상 칩을 HTML 배치에 맞춰 생성한다.
        /// 입력 편집과 완료 이벤트를 C#으로 연결하고 부분 입력의 오류 테두리를 준비한다.
        /// </summary>
        private void CreateColorFields()
        {
            Label(_panel, "HEX COLOR", 22f, 440f, 220f, 18f, 10, _mutedColor);
            Label(_panel, "RGB", 259f, 440f, 133f, 18f, 10, _mutedColor);
            _hexBorder = CreateImage("HexBorder", _panel, 22f, 463f, 226f, 38f, _borderColor);
            Image fieldImage = CreateImage("HexInput", _hexBorder.transform, 1f, 1f, 224f, 36f, _fieldColor);
            fieldImage.raycastTarget = true;
            _hexInput = fieldImage.gameObject.AddComponent<KeyboardLEDHexInput>();
            _hexInput.targetGraphic = fieldImage;
            _hexInput.navigation = new Navigation { mode = Navigation.Mode.None };
            _hexInput.characterLimit = 7;
            _hexInput.textComponent = Label(fieldImage.transform, "", 38f, 0f, 176f, 36f, 13, Color.white);
            _hexInput.TextEdited += HandleHexEdited;
            _hexInput.EditingFinished += HandleHexFinished;
            _colorChip = CreateImage("ColorChip", fieldImage.transform, 8f, 7f, 22f, 22f, Color.white);
            Image rgbFrame = CreateImage("RGBBorder", _panel, 259f, 463f, 133f, 38f, _borderColor);
            Image rgbBackground = CreateImage("RGBBackground", rgbFrame.transform, 1f, 1f, 131f, 36f, _fieldColor);
            _rgbText = Label(rgbBackground.transform, "", 0f, 0f, 131f, 36f, 11, _mutedColor, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// HTML의 여섯 색상 프리셋을 버튼으로 생성하고 각 색상을 C# 클릭 이벤트로 연결한다.
        /// 선택 프레임을 보관해 색상환이나 HEX 입력에서도 선택 표시를 갱신할 수 있게 한다.
        /// </summary>
        private void CreatePresets()
        {
            for (int index = 0; index < _presetColors.Length; index++)
            {
                ColorUtility.TryParseHtmlString(_presetColors[index], out Color color);
                Image frame = CreateImage("PresetFrame", _panel, 22f + index * 64f, 515f, 48f, 24f, _borderColor);
                _presetFrames.Add(frame);
                KeyboardEditButton button = Button("Preset", frame.transform, 2f, 2f, 44f, 20f, color);
                button.Clicked += selected => ApplyColor(color, true);
            }
        }

        /// <summary>
        /// 고정색, 무지개 및 숨쉬기 패턴의 드롭다운 목록을 만든다.
        /// 기존 LEDPattern enum 순서를 사용하고 선택 시 패턴을 즉시 적용한 뒤 목록을 닫는다.
        /// </summary>
        private void CreatePatternMenu()
        {
            Image menu = CreateImage("PatternMenu", _panel, 22f, 654f, 370f, 112f, _borderColor);
            menu.raycastTarget = true;
            for (int index = 0; index < _patternNames.Length; index++)
            {
                LEDPattern pattern = (LEDPattern)index;
                KeyboardEditButton button = Button("PatternOption", menu.transform, 5f, 5f + index * 34f, 360f, 32f, _fieldColor);
                Label(button.transform, _patternNames[index], 10f, 0f, 340f, 32f, 12, Color.white);
                button.Clicked += selected =>
                {
                    string previous = _customization.CurrentLEDPattern.ToString();
                    _customization.SetLEDPattern(pattern);
                    Game.Logging.CustomizationTelemetry.Emit(_customization, "ElementSelected", "LED", pattern.ToString(),
                        previous, changed: previous != pattern.ToString());
                    RefreshPattern();
                    _patternMenu.SetActive(false);
                };
            }
            _patternMenu = menu.gameObject;
            _patternMenu.SetActive(false);
        }

        /// <summary>
        /// button의 전원 클릭으로 현재 LED ON/OFF 상태를 반전한다.
        /// 색상과 패턴 값을 유지하고 전원 표시만 갱신한다.
        /// </summary>
        private void HandlePowerClicked(KeyboardEditButton button)
        {
            _customization.SetLEDEnabled(!_customization.LEDEnabled);
            Game.Logging.CustomizationTelemetry.Emit(_customization, "Adjustment", "LED", "Power",
                changed: true, details: _customization.LEDEnabled ? "true" : "false");
            RefreshPower();
        }

        /// <summary>
        /// button의 패턴 버튼 클릭으로 선택 목록의 표시 상태를 전환한다.
        /// 패턴 값은 실제 목록 항목을 선택할 때만 변경한다.
        /// </summary>
        private void HandlePatternMenuClicked(KeyboardEditButton button)
        {
            _patternMenu.SetActive(!_patternMenu.activeSelf);
        }

        /// <summary>
        /// color를 색상환에서 받은 LED 색상으로 적용한다.
        /// 색상환 내부 HSV 선택은 유지하고 나머지 색상 표시를 동기화한다.
        /// </summary>
        private void HandlePickerColorChanged(Color color)
        {
            ApplyColor(color, false);
        }

        /// <summary>
        /// text의 여섯 자리 HEX 입력을 검증하고 유효한 색상만 즉시 적용한다.
        /// 불완전한 입력은 붉은 테두리로 표시하며 현재 LED 색상은 유지한다.
        /// </summary>
        private void HandleHexEdited(string text)
        {
            string hex = text.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            if (hex.Length == 7 && ColorUtility.TryParseHtmlString(hex, out Color color))
                ApplyColor(color, true);
            else
                _hexBorder.color = new Color32(250, 121, 132, 255);
        }

        /// <summary>
        /// 입력을 마치면 현재 유효한 LED 색상을 HEX 문자열로 다시 표시한다.
        /// 부분 입력을 제거하고 오류 테두리를 초기화하며 LED 값은 변경하지 않는다.
        /// </summary>
        private void HandleHexFinished()
        {
            _hexInput.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(_customization.LEDColor));
            _hexBorder.color = _borderColor;
        }

        /// <summary>
        /// value를 현재 키보드의 LED 밝기에 적용하고 숫자 표시를 갱신한다.
        /// 기존 설정 함수가 0~10 범위를 보장한다.
        /// </summary>
        private void HandleIntensityChanged(float value)
        {
            float previous = _customization.LEDIntensity;
            _customization.SetLEDIntensity(value);
            ObserveAdjustment("Intensity", previous, _customization.LEDIntensity);
            RefreshValues();
        }

        /// <summary>
        /// value를 현재 키보드의 LED 애니메이션 속도에 적용하고 숫자를 갱신한다.
        /// 기존 설정 함수가 0~10 범위를 보장한다.
        /// </summary>
        private void HandleSpeedChanged(float value)
        {
            float previous = _customization.LEDSpeed;
            _customization.SetLEDSpeed(value);
            ObserveAdjustment("Speed", previous, _customization.LEDSpeed);
            RefreshValues();
        }

        /// <summary>
        /// color를 LED에 즉시 적용하고 syncPicker에 따라 색상환도 동기화한다.
        /// 저장된 패턴과 전원은 유지하고 열려 있는 패턴 목록을 닫는다.
        /// </summary>
        private void ApplyColor(Color color, bool syncPicker)
        {
            string previous = ColorUtility.ToHtmlStringRGBA(_customization.LEDColor);
            _customization.SetLEDColor(color);
            string applied = ColorUtility.ToHtmlStringRGBA(_customization.LEDColor);
            Game.Logging.CustomizationTelemetry.Emit(_customization, "Adjustment", "LED", "Color",
                previous, changed: previous != applied, details: applied);
            _patternMenu.SetActive(false);
            RefreshColor(syncPicker);
        }

        /// <summary>
        /// LED의 element 조작 전 previous와 적용 후 value를 관찰자에게 전달한다.
        /// 연속 슬라이더 조작은 요소 버튼 선택과 별도 이벤트로 기록한다.
        /// </summary>
        private void ObserveAdjustment(string element, float previous, float value)
        {
            Game.Logging.CustomizationTelemetry.Emit(_customization, "Adjustment", "LED", element,
                previous.ToString(CultureInfo.InvariantCulture), changed: !Mathf.Approximately(previous, value),
                details: value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 현재 LED 색상으로 HEX, RGB, 색상 칩, 프리셋 및 슬라이더 강조색을 갱신한다.
        /// syncPicker가 true이면 색상환도 갱신하며 설정 함수는 호출하지 않는다.
        /// </summary>
        private void RefreshColor(bool syncPicker)
        {
            Color color = _customization.LEDColor;
            string hex = "#" + ColorUtility.ToHtmlStringRGB(color);
            _hexInput.SetTextWithoutNotify(hex);
            _hexBorder.color = _borderColor;
            Color32 rgb = color;
            _rgbText.text = $"{rgb.r}, {rgb.g}, {rgb.b}";
            _colorChip.color = color;
            _intensityFill.color = color;
            _speedFill.color = color;
            for (int index = 0; index < _presetFrames.Count; index++)
                _presetFrames[index].color = hex == _presetColors[index] ? Color.white : _borderColor;
            if (syncPicker) _colorPicker.SetColor(color);
        }

        /// <summary>
        /// 현재 LED 전원으로 ON/OFF 텍스트와 토글 트랙 및 손잡이 위치를 갱신한다.
        /// 색상과 효과 설정은 변경하지 않는다.
        /// </summary>
        private void RefreshPower()
        {
            bool enabled = _customization.LEDEnabled;
            _powerText.text = enabled ? "ON" : "OFF";
            _powerTrack.color = enabled ? new Color32(36, 199, 181, 255) : new Color32(40, 51, 61, 255);
            _powerKnob.color = enabled ? new Color32(6, 39, 35, 255) : new Color32(189, 201, 209, 255);
            _powerKnob.rectTransform.anchoredPosition = new Vector2(enabled ? 23f : 4f, -4f);
        }

        /// <summary>
        /// 현재 LEDPattern 값으로 패턴 이름과 설명을 표시한다.
        /// 선택 색상을 유지하며 무지개 패턴의 색상 보관 동작도 설명한다.
        /// </summary>
        private void RefreshPattern()
        {
            int index = (int)_customization.CurrentLEDPattern;
            _patternText.text = _patternNames[index];
            _patternDescription.text = _patternDescriptions[index];
        }

        /// <summary>
        /// 현재 밝기와 속도를 소수 첫째 자리로 표시한다.
        /// 슬라이더 이벤트를 다시 발생시키거나 LED 값을 변경하지 않는다.
        /// </summary>
        private void RefreshValues()
        {
            _intensityText.text = _customization.LEDIntensity.ToString("F1", CultureInfo.InvariantCulture);
            _speedText.text = _customization.LEDSpeed.ToString("F1", CultureInfo.InvariantCulture) + "×";
        }

        /// <summary>
        /// name과 y 위치 및 maximum 범위로 네이티브 슬라이더를 구성하고 반환한다.
        /// 강조색을 적용할 fill도 반환하며 클릭과 드래그는 Slider의 기본 입력을 사용한다.
        /// </summary>
        private KeyboardLEDSlider CreateSlider(string name, float y, float maximum, out Image fill)
        {
            RectTransform root = Rect(name, _panel, 22f, y, 370f, 24f);
            Image hitArea = root.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear;
            KeyboardLEDSlider slider = root.gameObject.AddComponent<KeyboardLEDSlider>();
            slider.minValue = 0f;
            slider.maxValue = maximum;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            CreateImage("Track", root, 8f, 9.5f, 354f, 5f, new Color32(51, 64, 78, 255));
            RectTransform fillArea = Rect("FillArea", root, 8f, 9.5f, 354f, 5f);
            fill = CreateImage("Fill", fillArea, 0f, 0f, 0f, 0f, Color.white);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.sizeDelta = Vector2.zero;
            slider.fillRect = fill.rectTransform;
            RectTransform handleArea = Rect("HandleArea", root, 8f, 12f, 354f, 0f);
            Image handle = CreateImage("Handle", handleArea, 0f, 4f, 16f, 16f, new Color32(228, 255, 249, 255));
            handle.sprite = _circleSprite;
            handle.type = Image.Type.Simple;
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            handle.rectTransform.sizeDelta = new Vector2(16f, 16f);
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            return slider;
        }

        /// <summary>
        /// parent 아래에 name, 위치와 크기로 왼쪽 위 기준 RectTransform을 만들어 반환한다.
        /// 모든 LED 컨트롤이 같은 카드 좌표를 사용하도록 앵커와 피벗을 설정한다.
        /// </summary>
        private RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>
        /// parent의 지정 영역에 color를 사용하는 둥근 이미지를 만들고 반환한다.
        /// name을 오브젝트 이름으로 사용하고 장식 이미지가 입력을 가로채지 않도록 한다.
        /// </summary>
        private Image CreateImage(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            Image image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = _roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// parent의 지정 영역에 text, size, color와 alignment를 사용하는 라벨을 반환한다.
        /// 생성한 텍스트는 포인터 입력을 받지 않으며 한국어와 HEX 및 수치 표시에 사용한다.
        /// </summary>
        private Text Label(Transform parent, string text, float x, float y, float width, float height, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text label = Rect("Label", parent, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// parent의 지정 영역에 color 배경을 갖는 네이티브 편집 버튼을 만들고 반환한다.
        /// name을 오브젝트 이름으로 사용하며 동작 연결은 KeyboardEditButton의 C# 이벤트에 맡긴다.
        /// </summary>
        private KeyboardEditButton Button(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            Image image = CreateImage(name, parent, x, y, width, height, color);
            image.raycastTarget = true;
            KeyboardEditButton button = image.gameObject.AddComponent<KeyboardEditButton>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.selectedColor = Color.white;
            button.colors = colors;
            return button;
        }

        /// <summary>
        /// top, bottom 색과 radius를 사용해 둥근 모서리의 UI 스프라이트를 생성한다.
        /// 생성한 텍스처와 스프라이트를 보관해 패널 제거 시 함께 해제한다.
        /// </summary>
        private Sprite CreateRoundedSprite(Color top, Color bottom, float radius)
        {
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    Vector2 corner = new Vector2(Mathf.Max(Mathf.Abs(x + 0.5f - 32f) - (32f - radius), 0f),
                        Mathf.Max(Mathf.Abs(y + 0.5f - 32f) - (32f - radius), 0f));
                    Color color = Color.Lerp(bottom, top, y / 63f);
                    color.a *= Mathf.Clamp01(radius - corner.magnitude + 0.5f);
                    pixels[y * 64 + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, Vector4.one * radius);
            _textures.Add(texture);
            _sprites.Add(sprite);
            return sprite;
        }
    }
}
