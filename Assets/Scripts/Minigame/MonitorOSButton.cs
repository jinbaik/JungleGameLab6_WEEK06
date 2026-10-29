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
            if (eventData.button != PointerEventData.InputButton.Left || (_doubleClick && eventData.clickCount != 2)) return;
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            Clicked?.Invoke(this);
        }
    }
}
