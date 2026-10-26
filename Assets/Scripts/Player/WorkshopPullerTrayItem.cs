using System;

using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Player
{
    public sealed class WorkshopPullerTrayItem : MonoBehaviour, IPointerDownHandler
    {
        private bool _extractsSwitches;
        private bool _isGuidePaper;
        private bool _isHammer;

        public bool ExtractsSwitches => _extractsSwitches;
        public bool IsGuidePaper => _isGuidePaper;
        public bool IsHammer => _isHammer;
        public event Action<WorkshopPullerTrayItem> Pressed;

        /// <summary>extractsSwitches로 이 카드가 접근할 기존 풀러 종류를 지정한다. 도구와 재고 상태는 변경하지 않는다.</summary>
        public void Initialize(bool extractsSwitches)
        {
            _extractsSwitches = extractsSwitches;
        }

        /// <summary>이 카드를 조립 안내 종이의 도구함 출고 항목으로 지정한다. 카드 종류만 변경하며 종이의 위치와 상태는 유지한다.</summary>
        public void InitializeGuidePaper()
        {
            _isGuidePaper = true;
        }

        /// <summary>이 카드를 망치 도구함 출고 항목으로 지정한다. 카드 종류만 변경하며 망치 실물의 상태는 유지한다.</summary>
        public void InitializeHammer()
        {
            _isHammer = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            eventData.eligibleForClick = false;
            Pressed?.Invoke(this);
        }
    }
}
