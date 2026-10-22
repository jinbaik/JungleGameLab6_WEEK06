using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KeyboardEditButton))]
    public sealed class KeyboardEditSidebarButtonUI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        private static readonly Color _borderColor = new Color32(50, 70, 85, 255);
        private static readonly Color _backgroundColor = new Color32(26, 39, 51, 255);
        private static readonly Color _hoverBorderColor = new Color32(91, 139, 153, 255);
        private static readonly Color _hoverBackgroundColor = new Color32(38, 55, 70, 255);
        private static readonly Color _selectedBorderColor = new Color32(39, 158, 152, 255);
        private static readonly Color _selectedBackgroundColor = new Color32(16, 54, 56, 255);
        private static readonly Color _accentColor = new Color32(55, 222, 200, 255);
        private static readonly Color _focusColor = new Color32(140, 234, 221, 255);
        private static readonly Color _labelColor = new Color32(215, 229, 236, 255);
        private static readonly Color _iconColor = new Color32(148, 169, 183, 255);
        private static readonly Color _trailingColor = new Color32(110, 134, 151, 255);

        [Header("Sidebar Visuals")]
        [SerializeField] private Image _border;
        [SerializeField] private Image _background;
        [SerializeField] private KeyboardEditSidebarIcon _icon;
        [SerializeField] private Text _label;
        [SerializeField] private Image _selectionBar;
        [SerializeField] private KeyboardEditSidebarIcon _indicator;
        [SerializeField] private bool _isMode;

        [Header("Runtime State")]
        private bool _selected;
        private bool _hovered;
        private bool _pressed;
        private bool _focused;

        void OnEnable()
        {
            RefreshVisuals();
        }

        void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            _focused = false;
            transform.localScale = Vector3.one;
        }

        /// <summary>
        /// 실제 편집 상태에 맞춰 버튼의 선택 강조와 오른쪽 표시를 갱신한다.
        /// selected를 저장하며 배경, 아이콘, 선택 막대와 모드 점 또는 체크 표시를 변경한다.
        /// </summary>
        public void SetSelected(bool selected)
        {
            _selected = selected;
            RefreshVisuals();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RefreshVisuals();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            RefreshVisuals();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            _pressed = true;
            RefreshVisuals();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            _pressed = false;
            RefreshVisuals();
        }

        public void OnSelect(BaseEventData eventData)
        {
            _focused = !(eventData is PointerEventData);
            RefreshVisuals();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _focused = false;
            RefreshVisuals();
        }

        /// <summary>
        /// 저장된 선택 및 포인터 상태로 좌측 버튼의 시각 요소를 갱신한다.
        /// 모드 버튼에는 선택 점을, 부품 버튼에는 열림 여부에 맞는 화살표나 체크를 표시한다.
        /// </summary>
        private void RefreshVisuals()
        {
            _border.color = _focused ? _focusColor
                : _selected ? _selectedBorderColor
                : _hovered ? _hoverBorderColor : _borderColor;
            _background.color = _selected ? _selectedBackgroundColor
                : _hovered ? _hoverBackgroundColor : _backgroundColor;
            _icon.color = _selected ? _accentColor : _iconColor;
            _label.color = _selected ? new Color32(242, 255, 253, 255) : _labelColor;
            _selectionBar.enabled = _selected;
            _selectionBar.color = _accentColor;
            _indicator.enabled = !_isMode || _selected;
            _indicator.color = _selected ? _accentColor : _trailingColor;
            if (!_isMode)
                _indicator.SetChecked(_selected);

            transform.localScale = _pressed ? Vector3.one * 0.99f : Vector3.one;
        }
    }
}
