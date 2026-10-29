using UnityEngine;
using UnityEngine.EventSystems;

using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed class WorkshopKeycapCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private WorkshopKeycapPlacement _placement;
        private AssemblyCatalogEntry _entry;

        /// <summary>
        /// placement와 entry를 카드의 드래그 대상으로 연결한다.
        /// 재고와 키보드 상태는 변경하지 않으며 반환값은 없다.
        /// </summary>
        public void Initialize(WorkshopKeycapPlacement placement, AssemblyCatalogEntry entry)
        {
            _placement = placement;
            _entry = entry;
        }

        /// <summary>state에 이 카드의 가능한 출고 드래그를 추가한다. 재고 부족이나 입력 중단 상태에는 false를 반환한다.</summary>
        public bool CollectControlHints(WorkshopControlHintState state)
        {
            if (!_placement.CanBeginInventoryDrag(_entry)) return false;
            state.Add("좌클릭 드래그", _entry.IsSwitch ? "스위치 꺼내기" : "키캡 꺼내기");
            return true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            eventData.eligibleForClick = false;
            _placement.BeginInventoryDrag(_entry, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // 이동은 Update에서 같은 프레임의 공통 커서 좌표로 한 번만 처리한다.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // UI 밖의 버튼 해제도 Update에서 처리하며 중단 이벤트는 설치를 확정하지 않는다.
        }
    }
}
