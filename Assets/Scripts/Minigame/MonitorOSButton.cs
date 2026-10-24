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

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || (_doubleClick && eventData.clickCount != 2)) return;
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            Clicked?.Invoke(this);
        }
    }
}
