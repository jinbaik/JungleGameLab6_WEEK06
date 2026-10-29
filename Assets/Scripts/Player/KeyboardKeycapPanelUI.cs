using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using KeyboardModeling;

namespace Game.Player
{
    public sealed class KeyboardKeycapPanelUI : MonoBehaviour
    {
        private static readonly Color _panelColor = new Color32(17, 26, 35, 250);
        private static readonly Color _selectedTargetColor = new Color32(20, 61, 64, 255);
        private static readonly Color _targetColor = new Color32(15, 26, 36, 255);
        private static readonly Color _activeButtonColor = new Color32(59, 217, 196, 255);
        private static readonly Color _inactiveButtonColor = new Color(0.12f, 0.21f, 0.25f);
        private static readonly Color _textColor = new Color(0.78f, 0.86f, 0.88f);
        private static readonly Color _mutedColor = new Color32(136, 154, 164, 255);

        [Header("Runtime State")]
        private KeyboardCustomization _customization;
        private KeyboardInputController _input;
        private Canvas _canvas;
        private Font _font;
        private bool _isOpen;
        private bool _previousInput;
        private bool _isAllKeys = true;
        private bool _canApply;
        private bool _hasPendingMaterial;
        private bool _singleKeyOnly;
        private bool _artisanSelected;
        private int _selectedKeycapIndex;

        [Header("Panel Controls")]
        private RectTransform _panel;
        private RectTransform _statusRect;
        private RectTransform _undoButtonRect;
        private RectTransform _applyButtonRect;
        private Image _allKeysButtonBackground;
        private Image _singleKeyButtonBackground;
        private Text _allKeysButtonLabel;
        private Text _singleKeyButtonLabel;
        private Text _instructionLabel;
        private Text _statusLabel;
        private Text _stateLabel;
        private Text _applyLabel;
        private Image _undoButtonBackground;
        private Image _applyButtonBackground;
        private CanvasGroup _undoButtonGroup;
        private CanvasGroup _applyButtonGroup;

        public event Action<bool, int> TargetChanged;
        public event Action<int> KeyInputCaptured;
        public event Action UndoRequested;
        public event Action ApplyRequested;

        /// <summary>
        /// 전체 또는 단일 키 대상 선택 UI를 구성한다.
        /// 편집 오버레이의 Canvas를 사용하며 버튼과 키 입력은 C# 이벤트로 전달한다.
        /// </summary>
        public void Initialize()
        {
            _canvas = GetComponentInParent<Canvas>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _panel = transform as RectTransform;
            _panel.anchorMin = _panel.anchorMax = Vector2.one;
            _panel.pivot = new Vector2(0f, 1f);
            _panel.anchoredPosition = new Vector2(-408f, -20f);
            _panel.sizeDelta = new Vector2(390f, 234f);
            _panel.gameObject.AddComponent<Image>().color = _panelColor;

            Label(_panel, "키캡 재질 편집", 18f, 16f, 326f, 30f, 21, Color.white, FontStyle.Bold);
            _instructionLabel = Label(_panel, "적용할 키 범위를 선택하세요.", 18f, 48f, 326f, 30f, 10, _mutedColor);
            _allKeysButtonBackground = CreateButton(_panel, "AllKeycaps", "전체 키캡", 16f, 82f, 159f, 42f,
                _targetColor, 11, HandleAllKeysClicked, out _allKeysButtonLabel);
            _singleKeyButtonBackground = CreateButton(_panel, "SingleKeycap", "키캡 한 개", 183f, 82f, 159f, 42f,
                _targetColor, 11, HandleSingleKeyClicked, out _singleKeyButtonLabel);

            _stateLabel = Label(_panel, "키캡 미설치", 16f, 133f, 350f, 25f, 14, _mutedColor, FontStyle.Bold);
            _statusRect = Rect("Status", _panel, 16f, 161f, 350f, 36f);
            _statusLabel = Label(_statusRect, "재질을 선택한 후 적용하기를 누르세요.", 0f, 0f, 350f, 36f, 11, _mutedColor);
            _undoButtonBackground = CreateButton(_panel, "UndoKeycapMaterial", "미리보기 빼기", 16f, 178f, 159f, 40f,
                _inactiveButtonColor, 11, HandleUndoClicked, out _);
            _applyButtonBackground = CreateButton(_panel, "ApplyKeycapMaterial", "전체 키캡 적용하기", 183f, 178f,
                159f, 40f, new Color32(29, 62, 65, 255), 10, HandleApplyClicked, out _applyLabel);
            _undoButtonRect = (RectTransform)_undoButtonBackground.transform;
            _applyButtonRect = (RectTransform)_applyButtonBackground.transform;
            _undoButtonGroup = _undoButtonBackground.gameObject.AddComponent<CanvasGroup>();
            _applyButtonGroup = _applyButtonBackground.gameObject.AddComponent<CanvasGroup>();

            _isAllKeys = true;
            _selectedKeycapIndex = -1;
            gameObject.SetActive(false);
            RefreshPanel();
        }

        /// <summary>
        /// customization 키보드의 오른쪽 편집 패널을 열고 타건 입력을 활성 상태로 유지한다.
        /// customization의 키캡 및 축 상태와 InputEnabled를 사용하며 빈 키보드는 전체 범위로 연다.
        /// </summary>
        public void Open(KeyboardCustomization customization)
        {
            if (customization == null || _isOpen) return;

            if (_customization != customization)
            {
                _isAllKeys = true;
                _selectedKeycapIndex = -1;
                _canApply = false;
            }

            _customization = customization;
            if (!customization.HasAnyInstalledKeycaps() && customization.HasAnySwitchForKeycaps())
            {
                _isAllKeys = true;
                _selectedKeycapIndex = -1;
                _canApply = false;
                _hasPendingMaterial = false;
            }

            _input = customization.GetComponent<KeyboardInputController>();
            _previousInput = _input != null && _input.InputEnabled;
            if (_input != null) _input.SetInputEnabled(true);
            _isOpen = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            gameObject.SetActive(true);
            RefreshPanel();
        }

        /// <summary>
        /// 키캡 편집 패널을 닫고 진입 전 키보드 입력을 복원한다.
        /// 키 입력 감지를 종료하며 패널 활성 상태와 저장한 입력 설정을 되돌린다.
        /// </summary>
        public void Close()
        {
            if (!_isOpen) return;

            _isOpen = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (_input != null) _input.SetInputEnabled(_previousInput);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 미리보기 적용과 선택 재질 제거 버튼의 활성 상태를 갱신한다.
        /// canApply와 hasPendingMaterial을 사용해 적용 및 취소 가능 상태를 표시한다.
        /// </summary>
        public void SetCanApply(bool canApply, bool hasPendingMaterial)
        {
            _canApply = canApply;
            _hasPendingMaterial = hasPendingMaterial;
            RefreshPanel();
        }

        /// <summary>singleKeyOnly로 아티산 선택 상태와 안내를 갱신한다. WorkShop0에서는 전체 적용을 허용하고 다른 씬에서는 단일 키 모드로 전환한다.</summary>
        public void SetSingleKeyOnly(bool singleKeyOnly)
        {
            // WorkShop0에서는 아티산도 일반 키캡처럼 적용 범위를 선택할 수 있다.
            _artisanSelected = singleKeyOnly;
            _singleKeyOnly = singleKeyOnly && !Game.Workshop.CustomizationOnlyWorkshop.IsActive;
            if (_singleKeyOnly && _isAllKeys) SetTarget(false);
            else RefreshPanel();
        }

        /// <summary>message의 배치 실패 이유를 상태 영역에 표시한다. 선택 재질과 입력 대기 상태는 변경하지 않는다.</summary>
        public void ShowPlacementFailure(string message)
        {
            _statusLabel.text = string.IsNullOrEmpty(message) ? "이 위치에는 적용할 수 없습니다. 다른 키를 누르세요." : message;
        }

        /// <summary>
        /// 현재 범위 선택과 단일 키 인덱스를 반환한다.
        /// 전체 키 범위는 true이며 단일 키 선택은 0부터 시작하는 인덱스를 사용한다.
        /// </summary>
        public bool TryGetTarget(out bool allKeys, out int keycapIndex)
        {
            allKeys = _isAllKeys;
            keycapIndex = _isAllKeys ? -1 : _selectedKeycapIndex;
            return _isAllKeys || IsSelectedIndexValid();
        }

        /// <summary>
        /// 화면 좌표가 현재 열린 키캡 편집 패널 안에 있는지 반환한다.
        /// screenPosition과 Canvas 카메라를 사용해 오버레이 바깥 클릭을 구분한다.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (!_isOpen) return false;
            Camera camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(_panel, screenPosition, camera);
        }

        /// <summary>현재 단일 키 재질 입력 대기 상태로 state에 대상 키 선택을 추가한다. 재질이나 선택 인덱스는 변경하지 않는다.</summary>
        public void CollectControlHints(WorkshopControlHintState state)
        {
            if (_isOpen && !_isAllKeys && _hasPendingMaterial && Keyboard.current != null)
                state.Add("키보드 입력", "재질 적용 키 선택");
        }

        void Update()
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (!_isOpen) return;

            if (!_isAllKeys)
                CaptureSingleKeyInput();
        }

        void OnDisable()
        {
            Close();
        }

        /// <summary>
        /// 전체 키캡 범위를 선택하고 적용 범위 변경 이벤트를 전달한다.
        /// 현재 단일 키 번호를 보존하며 범위 강조와 안내 UI를 갱신한다.
        /// </summary>
        private void HandleAllKeysClicked()
        {
            SetTarget(true);
        }

        /// <summary>
        /// 단일 키 범위를 선택하고 키 입력에 따른 적용을 시작한다.
        /// 선택 대상은 초기화하며 다음 물리 키 입력으로 대상 키를 지정한다.
        /// </summary>
        private void HandleSingleKeyClicked()
        {
            SetTarget(false);
        }

        /// <summary>
        /// allKeys 범위를 선택하고 다음 키 입력의 적용 대상을 갱신한다.
        /// 단일 키 범위에서는 대상 번호를 비우고 전체 범위에서는 전체 대상을 알린다.
        /// </summary>
        private void SetTarget(bool allKeys)
        {
            if (_singleKeyOnly && allKeys) return;
            if (_isAllKeys == allKeys)
            {
                if (!allKeys)
                {
                    _selectedKeycapIndex = -1;
                    RefreshPanel();
                    TargetChanged?.Invoke(false, -1);
                }
                return;
            }

            _isAllKeys = allKeys;
            if (!_isAllKeys) _selectedKeycapIndex = -1;
            RefreshPanel();
            TargetChanged?.Invoke(_isAllKeys, _isAllKeys ? -1 : _selectedKeycapIndex);
        }

        /// <summary>
        /// 단일 키 범위에서 재질 미리보기가 준비된 경우에만 키 입력을 감지한다.
        /// 키 매핑과 축 설치 상태를 확인하며 유효한 키만 대상 변경과 즉시 적용 이벤트로 전달한다.
        /// </summary>
        private void CaptureSingleKeyInput()
        {
            if (!_isOpen || _isAllKeys || !_hasPendingMaterial)
                return;
            if (!Application.isFocused || Keyboard.current == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard.escapeKey.wasPressedThisFrame)
                return;

            foreach (var keyControl in keyboard.allKeys)
            {
                if (!WindowsSpecialKeyInput.WasPressedThisFrame(keyboard, keyControl.keyCode)) continue;
                if (_customization.TryGetKeycapIndex(keyControl.keyCode, out int keycapIndex))
                {
                    if (!_customization.HasSwitchForKeycap(keycapIndex))
                    {
                        _statusLabel.text = "축이 설치되지 않은 키에는 키캡을 씌울 수 없습니다. 다른 키를 입력하세요.";
                        return;
                    }

                    CompleteKeyInput(keycapIndex);
                    return;
                }

                _statusLabel.text = "해당 키에 연결된 키캡이 없습니다. 다른 키를 입력하세요.";
                return;
            }
        }

        /// <summary>
        /// keycapIndex를 단일 키 대상으로 지정하고 변경 및 즉시 적용을 알린다.
        /// 대상 변경을 먼저 전달해 선택 재질의 미리보기를 갱신한 뒤 적용 이벤트를 보낸다.
        /// </summary>
        private void CompleteKeyInput(int keycapIndex)
        {
            _selectedKeycapIndex = keycapIndex;
            RefreshPanel();
            TargetChanged?.Invoke(false, _selectedKeycapIndex);
            KeyInputCaptured?.Invoke(_selectedKeycapIndex);
        }

        /// <summary>
        /// 미리보기 제거 요청을 보내며 선택된 재질이 없으면 아무 동작도 하지 않는다.
        /// _hasPendingMaterial 상태를 사용해 버튼 클릭을 제한한다.
        /// </summary>
        private void HandleUndoClicked()
        {
            if (!_hasPendingMaterial) return;
            UndoRequested?.Invoke();
        }

        /// <summary>
        /// 적용 요청을 보내며 유효한 미리보기 대상에서만 이벤트를 발생시킨다.
        /// 범위와 단일 키 인덱스를 검사해 잘못된 적용을 차단한다.
        /// </summary>
        private void HandleApplyClicked()
        {
            if (!CanApply()) return;
            ApplyRequested?.Invoke();
        }

        /// <summary>
        /// 전체 또는 단일 키 적용에 맞춰 패널 배치, 버튼 상태와 안내를 갱신한다.
        /// 현재 범위와 인덱스 유효성 및 미리보기 상태를 사용한다.
        /// </summary>
        private void RefreshPanel()
        {
            if (_panel == null) return;

            _allKeysButtonBackground.color = _isAllKeys ? _selectedTargetColor : _targetColor;
            _allKeysButtonBackground.GetComponent<KeyboardCustomizationTestButton>().enabled = !_singleKeyOnly;
            _allKeysButtonLabel.text = _singleKeyOnly ? "전체 적용 불가" : "전체 키캡";
            _singleKeyButtonBackground.color = _isAllKeys ? _targetColor : _selectedTargetColor;
            _allKeysButtonLabel.color = _isAllKeys ? Color.white : _mutedColor;
            _singleKeyButtonLabel.color = _isAllKeys ? _mutedColor : Color.white;
            _instructionLabel.text = _singleKeyOnly
                ? "아티산은 하나씩, 최대 3개까지 배치할 수 있습니다."
                : _artisanSelected
                ? "아티산은 1u 키에만 적용됩니다.\n나머지 키는 기존 키캡 또는 빈 상태를 유지합니다."
                : _isAllKeys
                ? "축이 설치된 키에 키캡을 씌우거나 한 개를 선택하세요."
                : "재질을 선택한 뒤 축이 설치된 키를 누르세요.";
            _statusRect.anchoredPosition = new Vector2(16f, -161f);
            _undoButtonRect.anchoredPosition = new Vector2(16f, -208f);
            _applyButtonRect.anchoredPosition = new Vector2(183f, -208f);
            _panel.sizeDelta = new Vector2(390f, 264f);
            _applyLabel.text = _isAllKeys
                ? "전체 키캡 적용하기"
                : IsSelectedIndexValid()
                    ? _selectedKeycapIndex.ToString() + "번 키 적용하기"
                    : "키 입력 대기";

            bool canApply = CanApply();
            bool hasInstalledKeycaps = _customization != null && _customization.HasAnyInstalledKeycaps();
            _stateLabel.text = canApply ? "미리보기 중 · 아직 적용되지 않음"
                : hasInstalledKeycaps ? "적용 완료 · 설치된 키캡"
                : "키캡 미설치";
            _stateLabel.color = canApply ? new Color32(255, 200, 96, 255)
                : hasInstalledKeycaps ? _activeButtonColor : _mutedColor;
            _applyButtonGroup.interactable = canApply;
            _undoButtonGroup.interactable = _hasPendingMaterial;
            _applyButtonGroup.blocksRaycasts = true;
            _undoButtonGroup.blocksRaycasts = true;
            _applyButtonBackground.color = canApply ? _activeButtonColor : new Color32(29, 62, 65, 255);
            _undoButtonBackground.color = _hasPendingMaterial ? _inactiveButtonColor : new Color32(29, 42, 52, 255);
            _applyLabel.color = canApply ? new Color32(5, 34, 35, 255) : _mutedColor;
            _statusLabel.text = !_isAllKeys && !IsSelectedIndexValid()
                ? _hasPendingMaterial
                    ? "키를 입력하면 선택한 재질이 즉시 적용됩니다."
                    : "재질을 선택한 뒤 키를 누르면 바로 적용됩니다."
                : _isAllKeys && _customization != null && !_customization.HasAnySwitchForKeycaps()
                    ? "축이 설치된 키가 없어 키캡을 씌울 수 없습니다."
                    : canApply
                        ? "적용하기를 눌러 확정하세요. 메뉴를 나가면 취소됩니다."
                        : _isAllKeys
                            ? "재질을 선택한 후 적용하기를 누르세요."
                            : _hasPendingMaterial
                                ? "키를 입력하면 선택한 재질이 즉시 적용됩니다."
                                : "재질을 선택한 뒤 키를 누르면 바로 적용됩니다.";
        }

        /// <summary>
        /// 현재 범위와 키 인덱스에서 미리보기 적용이 가능한지 반환한다.
        /// _canApply와 단일 키 인덱스 유효성을 검사한다.
        /// </summary>
        private bool CanApply()
        {
            return _canApply && (_isAllKeys || IsSelectedIndexValid());
        }

        /// <summary>
        /// 단일 키 번호가 현재 키보드의 키캡 배열 안에 있는지 반환한다.
        /// _selectedKeycapIndex와 KeycapCount를 사용한다.
        /// </summary>
        private bool IsSelectedIndexValid()
        {
            return _customization != null && _selectedKeycapIndex >= 0
                && _selectedKeycapIndex < _customization.KeycapCount;
        }

        /// <summary>
        /// parent 아래 왼쪽 위 기준 RectTransform을 만들고 반환한다.
        /// name, 위치와 크기를 사용해 패널 요소를 배치한다.
        /// </summary>
        private RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>
        /// parent에 왼쪽 위 기준 텍스트 요소를 만들고 반환한다.
        /// text, 위치, 크기, fontSize와 color를 사용해 패널 문구를 표시한다.
        /// </summary>
        private Text Label(Transform parent, string text, float x, float y, float width, float height,
            int fontSize, Color color, FontStyle fontStyle = FontStyle.Normal)
        {
            Text label = Rect(text, parent, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = _font;
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// C# Clicked 이벤트를 사용하는 배경 버튼과 라벨을 만들고 반환한다.
        /// parent, label, 배치, 색상과 onClick 동작을 사용한다.
        /// </summary>
        private Image CreateButton(Transform parent, string name, string label, float x, float y,
            float width, float height, Color color, int fontSize, Action onClick, out Text buttonLabel)
        {
            RectTransform rect = Rect(name, parent, x, y, width, height);
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = color;
            rect.gameObject.AddComponent<KeyboardCustomizationTestButton>().Clicked += onClick;
            buttonLabel = Label(rect, label, 5f, 0f, width - 10f, height, fontSize, _textColor, FontStyle.Bold);
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            return background;
        }
    }
}
