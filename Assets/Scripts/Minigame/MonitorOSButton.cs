using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class MonitorOSButton : MonoBehaviour, IPointerClickHandler
    {
        [Header("Interaction")]
        [SerializeField] private bool _doubleClick = true;
        private bool _interactable = true;

        [Header("App Display")]
        [SerializeField] private Image _icon;
        [SerializeField] private Text _label;
        private Image _notificationDot;

        public MonitorMiniGameDefinition Definition { get; private set; }
        public event Action<MonitorOSButton> Clicked;

        /// <summary>definition의 이름과 아이콘을 앱 버튼에 표시한다. 실행할 SO를 보관하며 게임 실행은 Clicked 구독자가 처리한다.</summary>
        public void Configure(MonitorMiniGameDefinition definition)
        {
            Definition = definition;
            _label.text = definition.DisplayName;
            _icon.sprite = definition.Icon;
            _icon.enabled = definition.Icon != null;
        }

        /// <summary>
        /// sprite를 아이콘 오른쪽 위의 작은 빨간 알림 점에 적용한다.
        /// 클릭을 가로채지 않는 Image를 한 번 생성하고 기본 상태는 숨김으로 둔다.
        /// </summary>
        public void ConfigureNotificationDot(Sprite sprite)
        {
            if (_notificationDot != null) return;
            var dot = new GameObject("NewMailDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dot.transform.SetParent(_icon.transform, false);
            RectTransform rect = (RectTransform)dot.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-3f, -3f);
            rect.sizeDelta = new Vector2(24f, 24f);
            _notificationDot = dot.GetComponent<Image>();
            _notificationDot.sprite = sprite;
            _notificationDot.color = new Color(0.95f, 0.12f, 0.12f);
            _notificationDot.raycastTarget = false;
            dot.SetActive(false);
        }

        /// <summary>
        /// visible에 따라 구성된 메일 알림 점의 표시를 변경한다.
        /// 버튼의 클릭 영역과 앱 실행 상태는 유지한다.
        /// </summary>
        public void SetNotificationDot(bool visible)
        {
            if (_notificationDot != null) _notificationDot.gameObject.SetActive(visible);
        }

        /// <summary>
        /// interactable에 따라 버튼의 클릭 허용 여부와 라벨의 투명도를 변경한다.
        /// 앱 정의와 이벤트 구독은 유지하며 비활성 상태의 클릭을 차단한다.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            Color color = _label.color;
            color.a = interactable ? 1f : 0.35f;
            _label.color = color;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_interactable) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (AudioManager.instance != null) AudioManager.instance.PlayButtonSound();
            if (_doubleClick && eventData.clickCount != 2) return;
            Clicked?.Invoke(this);
        }
    }
}
