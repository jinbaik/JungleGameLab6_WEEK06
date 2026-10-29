using System;

using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Player
{
    public sealed class WorkshopPullerTrayItem : MonoBehaviour, IPointerDownHandler
    {
        private bool _extractsSwitches;

        public bool ExtractsSwitches => _extractsSwitches;
        public event Action<WorkshopPullerTrayItem> Pressed;

        /// <summary>extractsSwitches로 이 카드가 접근할 기존 풀러 종류를 지정한다. 도구와 재고 상태는 변경하지 않는다.</summary>
        public void Initialize(bool extractsSwitches)
        {
            _extractsSwitches = extractsSwitches;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            eventData.eligibleForClick = false;
            Pressed?.Invoke(this);
        }
    }
}
