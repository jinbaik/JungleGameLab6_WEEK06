using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Tutorials;

namespace Game.Player
{
    /// <summary>작업실 상태 제공자의 조작 목록을 도움말 아래에 표시한다. 입력과 게임 규칙을 실행하지 않는다.</summary>
    [DefaultExecutionOrder(20000)]
    public sealed class WorkshopControlHints : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private PlayerInteractionController _interaction;
        [SerializeField] private WorkshopViewController _workshopView;
        [SerializeField] private WorkshopKeycapPlacement _placement;
        [SerializeField] private WorkshopKeyboardPullerController _pullers;
        [SerializeField] private KeyboardEditOverlayUI _editOverlay;
        [SerializeField] private FirstEncounterTutorial _tutorial;
        [SerializeField] private RectTransform _helpButton;
        [SerializeField] private GameObject _legacyControlBar;

        [Header("Layout")]
        [SerializeField, Min(280f)] private float _width = 370f;
        [SerializeField, Min(0f)] private float _helpGap = 14f;
        [SerializeField, Min(16)] private int _fontSize = 18;
        [SerializeField, Min(0f)] private float _rowGap = 8f;

        [Header("Style")]
        [SerializeField] private Color _background = new Color32(24, 37, 48, 225);
        [SerializeField] private Color _border = new Color32(69, 91, 112, 220);
        [SerializeField] private Color _textColor = new Color32(236, 231, 214, 255);
        [SerializeField] private Color _accent = new Color32(115, 220, 227, 255);

        [Header("Focus Fade")]
        [SerializeField, Min(0f)] private float _focusFadeDistance = 140f;
        [SerializeField, Range(0f, 1f)] private float _focusMinimumOpacity = 0.25f;
        [SerializeField, Min(0f)] private float _focusFadeDuration = 0.2f;
        private readonly WorkshopControlHintState _state = new WorkshopControlHintState();
        private readonly WorkshopControlHintState _common = new WorkshopControlHintState();
        private readonly List<HintRow> _rows = new List<HintRow>();
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _panel;
        private CanvasGroup _panelGroup;
        private readonly Vector3[] _fadeCorners = new Vector3[4];
        private RectTransform _parent;
        private Text _title;
        private Font _font;
        private Sprite _sprite;
        private PointerEventData _pointer;
        private EventSystem _pointerEvents;
        private GameObject _capturedUI;
        private string _displayedTitle;
        private float _displayedWidth;

        private sealed class HintRow
        {
            public RectTransform Rect { get; set; }
            public RectTransform Badge { get; set; }
            public Text Input { get; set; }
            public Text Action { get; set; }
        }

        void Start()
        {
            _parent = (RectTransform)_helpButton.parent;
            _font = _helpButton.GetComponentInChildren<Text>().font;
            _sprite = _helpButton.GetComponent<Image>().sprite;
            _panel = CreateRect("WorkshopControlHintsPanel", _parent);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = Vector2.one;
            Image background = _panel.gameObject.AddComponent<Image>();
            background.sprite = _sprite;
            background.color = _background;
            background.raycastTarget = false;
            Outline outline = _panel.gameObject.AddComponent<Outline>();
            outline.effectColor = _border;
            outline.effectDistance = new Vector2(1f, -1f);
            _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();
            _panelGroup.interactable = false;
            _panelGroup.blocksRaycasts = false;
            _title = CreateText("StateTitle", _panel, _accent);
            _title.fontStyle = FontStyle.Bold;
            _title.rectTransform.anchoredPosition = new Vector2(16f, -14f);
            if (_interaction != null)
                _interaction.SetContextHintsEnabled(true);
            _legacyControlBar.SetActive(false);
            _panel.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            InGameCursor.PointerCanceled += HandlePointerCanceled;
            if (_panel != null && _interaction != null) _interaction.SetContextHintsEnabled(true);
        }

        void LateUpdate()
        {
            bool canShow = _workshopView != null ? _workshopView.CanShowControlHints
                : _interaction != null && _interaction.CanShowControlHints;
            if (!canShow)
            {
                HandlePointerCanceled();
                return;
            }
            _state.Clear();
            _common.Clear();
            if (_workshopView != null)
                _workshopView.CollectControlHints(_common);
            else
                _interaction.CollectControlHints(_common);
            if (_common.Rows.Count == 0) { Hide(); return; }
            bool isEditing = _workshopView != null ? _workshopView.IsKeyboardEditing : _interaction.IsKeyboardEditing;
            _state.Title = isEditing ? _editOverlay.ControlHintTitle : _common.Title;
            if (InGameCursor.CanProcessVirtualPointer && (!isEditing || _pullers.EditingKeyboard != null))
            {
                bool owned = isEditing && (_placement.CollectControlHints(_state) || _pullers.CollectControlHints(_state)
                    || _editOverlay.CollectControlHints(_state));
                if (!owned) CollectUIHints();
            }
            bool choosingKey = false;
            foreach (WorkshopControlHintState.Row row in _state.Rows) choosingKey |= row.Input == "키보드 입력";
            foreach (WorkshopControlHintState.Row row in _common.Rows)
                if (!choosingKey || row.Input != "키보드 입력") _state.Add(row.Input, row.Action);
            if (_tutorial.CanOpenHelp) _state.Add("F10", "도움말");
            Present();
            UpdatePanelOpacity(_workshopView != null && isEditing);
        }

        /// <summary>area의 화면 가장자리, cursorPosition, fadeDistance와 minimumOpacity를 사용해 목표 불투명도를 반환한다. screenCorners는 재사용하며 UI 상태는 변경하지 않는다.</summary>
        public static float GetProximityOpacity(RectTransform area, Vector2 cursorPosition, float fadeDistance,
            float minimumOpacity, Vector3[] screenCorners)
        {
            area.GetWorldCorners(screenCorners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(null, screenCorners[0]);
            float minX = first.x;
            float maxX = first.x;
            float minY = first.y;
            float maxY = first.y;
            for (int index = 1; index < screenCorners.Length; index++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, screenCorners[index]);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            float horizontalDistance = cursorPosition.x < minX ? minX - cursorPosition.x
                : cursorPosition.x > maxX ? cursorPosition.x - maxX : 0f;
            float verticalDistance = cursorPosition.y < minY ? minY - cursorPosition.y
                : cursorPosition.y > maxY ? cursorPosition.y - maxY : 0f;
            float distance = Mathf.Sqrt(horizontalDistance * horizontalDistance + verticalDistance * verticalDistance);
            float proximity = fadeDistance <= 0f ? (distance > 0f ? 1f : 0f)
                : Mathf.Clamp01(distance / fadeDistance);
            return Mathf.Lerp(minimumOpacity, 1f, proximity);
        }

        /// <summary>isEditing과 공통 가상 커서를 사용해 현재 표시 중인 안내 패널의 CanvasGroup alpha를 부드럽게 갱신한다. 일반 뷰와 숨김 상태는 기존 불투명도로 복구한다.</summary>
        private void UpdatePanelOpacity(bool isEditing)
        {
            if (_panelGroup == null || !_panel.gameObject.activeSelf) return;
            float target = isEditing
                ? GetProximityOpacity(_panel, InGameCursor.Position, _focusFadeDistance,
                    _focusMinimumOpacity, _fadeCorners)
                : 1f;
            float step = _focusFadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / _focusFadeDuration;
            _panelGroup.alpha = Mathf.MoveTowards(_panelGroup.alpha, target, step);
        }

        /// <summary>공통 커서 아래 최상위 UI와 현재 드래그 중인 UI로 가능한 조작을 추가한다. 뒤쪽 월드와 비활성 컨트롤은 조회하지 않는다.</summary>
        private void CollectUIHints()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;
            if (_pointerEvents != events)
            {
                _pointerEvents = events;
                _pointer = new PointerEventData(events);
            }
            _pointer.position = InGameCursor.Position;
            _hits.Clear();
            events.RaycastAll(_pointer, _hits);
            GameObject target = null;
            foreach (RaycastResult hit in _hits)
            {
                if (hit.gameObject.GetComponent<KeyboardEditOverlayUI>() != null) continue;
                target = hit.gameObject;
                break;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed) _capturedUI = null;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && target != null
                && (target.GetComponentInParent<Slider>() != null || target.GetComponentInParent<Scrollbar>() != null
                    || target.GetComponentInParent<KeyboardLEDColorPicker>() != null)) _capturedUI = target;
            if (_capturedUI != null && _capturedUI.activeInHierarchy) target = _capturedUI;
            if (target == null) return;
            WorkshopKeycapCardDrag card = target.GetComponentInParent<WorkshopKeycapCardDrag>();
            if (card != null)
            {
                card.CollectControlHints(_state);
            }
            else
            {
                Selectable selectable = target.GetComponentInParent<Selectable>();
                if (selectable != null && selectable.IsActive() && selectable.IsInteractable())
                {
                    if (selectable is Slider) _state.Add("좌클릭 드래그", "값 조절");
                    else if (selectable is Scrollbar) _state.Add("좌클릭 드래그", "목록 스크롤");
                    else if (selectable is InputField input)
                    {
                        _state.Add("좌클릭", "입력 칸 선택");
                        if (input.isFocused && !input.readOnly) _state.Add("키보드 입력", "텍스트 입력");
                    }
                    else if (selectable is Button)
                    {
                        Text label = selectable.GetComponentInChildren<Text>();
                        if (label != null && !string.IsNullOrWhiteSpace(label.text))
                            _state.Add("좌클릭", label.text.Replace("\n", " "));
                    }
                }
                KeyboardLEDColorPicker picker = target.GetComponentInParent<KeyboardLEDColorPicker>();
                if (picker != null && picker.isActiveAndEnabled)
                    picker.CollectControlHints(_state, InGameCursor.Position);
            }
            ScrollRect scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.isActiveAndEnabled && scroll.vertical && scroll.content != null
                && scroll.content.rect.height * scroll.content.localScale.y
                    > (scroll.viewport != null ? scroll.viewport.rect.height : ((RectTransform)scroll.transform).rect.height) + 1f)
                _state.Add("마우스 휠", "목록 스크롤");
        }

        /// <summary>변경된 제목과 행만 UI에 반영한다. 화면 폭과 행의 실제 높이로 패널을 배치하고 생성한 행은 재사용한다.</summary>
        private void Present()
        {
            if (_state.Rows.Count == 0) { Hide(); return; }
            float width = Mathf.Min(_width, _parent.rect.width - 56f);
            bool changed = !_panel.gameObject.activeSelf || _displayedWidth != width || _displayedTitle != _state.Title;
            _displayedWidth = width;
            _displayedTitle = _state.Title;
            if (_title.text != _state.Title) _title.text = _state.Title;
            _title.rectTransform.sizeDelta = new Vector2(width - 32f, _fontSize + 10f);
            while (_rows.Count < _state.Rows.Count) _rows.Add(CreateRow());
            for (int index = 0; index < _rows.Count; index++)
            {
                bool visible = index < _state.Rows.Count;
                HintRow row = _rows[index];
                if (row.Rect.gameObject.activeSelf != visible) { row.Rect.gameObject.SetActive(visible); changed = true; }
                if (!visible) continue;
                WorkshopControlHintState.Row value = _state.Rows[index];
                if (row.Input.text != value.Input) { row.Input.text = value.Input; changed = true; }
                if (row.Action.text != value.Action) { row.Action.text = value.Action; changed = true; }
            }
            if (changed) LayoutRows(width);
            _helpButton.GetWorldCorners(_corners);
            Vector3 bottomRight = _parent.InverseTransformPoint(_corners[3]);
            _panel.anchoredPosition = new Vector2(bottomRight.x - _parent.rect.xMax, bottomRight.y - _parent.rect.yMax - _helpGap);
            _panel.gameObject.SetActive(true);
        }

        /// <summary>widthと各行の文字高さで位置とパネル高さを計算する。文字の縮小や切り捨ては行わない。</summary>
        private void LayoutRows(float width)
        {
            float top = _fontSize + 36f;
            float inputWidth = 148f;
            for (int index = 0; index < _state.Rows.Count; index++)
            {
                HintRow row = _rows[index];
                row.Input.rectTransform.sizeDelta = new Vector2(inputWidth, 0f);
                row.Action.rectTransform.sizeDelta = new Vector2(width - inputWidth - 50f, 0f);
                float height = Mathf.Max(32f, Mathf.Max(row.Input.preferredHeight, row.Action.preferredHeight) + 10f);
                row.Rect.anchoredPosition = new Vector2(16f, -top);
                row.Rect.sizeDelta = new Vector2(width - 32f, height);
                row.Badge.sizeDelta = new Vector2(inputWidth, height);
                row.Input.rectTransform.sizeDelta = new Vector2(inputWidth, height);
                row.Action.rectTransform.anchoredPosition = new Vector2(inputWidth + 12f, 0f);
                row.Action.rectTransform.sizeDelta = new Vector2(width - inputWidth - 50f, height);
                top += height + _rowGap;
            }
            _panel.sizeDelta = new Vector2(width, top + 8f);
        }

        /// <summary>한 개의 입력 배지와 행동명을 생성해 반환한다. 목록 갱신 시 같은 오브젝트를 계속 사용한다.</summary>
        private HintRow CreateRow()
        {
            RectTransform rect = CreateRect("ControlRow", _panel);
            RectTransform badgeRect = CreateRect("InputBadge", rect);
            Image badge = badgeRect.gameObject.AddComponent<Image>();
            badge.sprite = _sprite;
            badge.color = new Color32(62, 78, 89, 220);
            badge.raycastTarget = false;
            Text input = CreateText("Input", rect, _textColor);
            input.fontSize = _fontSize - 2;
            input.alignment = TextAnchor.MiddleCenter;
            Text action = CreateText("Action", rect, _textColor);
            return new HintRow { Rect = rect, Badge = badgeRect, Input = input, Action = action };
        }

        /// <summary>name과 parent로 상단 왼쪽 기준 RectTransform을 생성해 반환한다. 기존 UI의 Transform은 변경하지 않는다.</summary>
        private RectTransform CreateRect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            return rect;
        }

        /// <summary>name과 parent 및 color로 기존 도움말 글꼴의 Text를 생성해 반환한다. 모든 글자는 레이캐스트를 통과시킨다.</summary>
        private Text CreateText(string name, Transform parent, Color color)
        {
            Text text = CreateRect(name, parent).gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = _fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        /// <summary>입력 취소 시 보관한 UI 점유와 표시를 비운다. 다시 표시할 때 현재 상태를 새로 조회한다.</summary>
        private void HandlePointerCanceled() { _capturedUI = null; Hide(); }

        /// <summary>현재 패널만 숨긴다. 기존 상호작용 상태나 입력은 변경하지 않는다.</summary>
        private void Hide()
        {
            if (_panelGroup != null) _panelGroup.alpha = 1f;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= HandlePointerCanceled;
            if (_interaction != null)
                _interaction.SetContextHintsEnabled(false);
            Hide();
        }

        void OnDestroy()
        {
            if (_panel != null) Destroy(_panel.gameObject);
        }
    }
}
