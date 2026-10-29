using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed class WorkshopKeycapCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private WorkshopKeycapPlacement _placement;
        private AssemblyCatalogEntry _entry;
        private WorkshopKeycapDrawerTransition _tray;
        private WorkshopPcbCardDrag _structureDrag;
        private ScrollRect _scroll;
        private bool _scrolling;

        /// <summary>
        /// placement와 entry를 카드의 드래그 대상으로 연결하며 tray가 있으면 목록 안에서는 스크롤을 사용한다.
        /// 재고와 키보드 상태는 변경하지 않으며 반환값은 없다.
        /// </summary>
        public void Initialize(WorkshopKeycapPlacement placement, AssemblyCatalogEntry entry, WorkshopKeycapDrawerTransition tray = null, WorkshopPcbCardDrag structureDrag = null)
        {
            _placement = placement;
            _entry = entry;
            _tray = tray;
            _structureDrag = structureDrag;
        }

        /// <summary>state에 이 카드의 가능한 출고 드래그를 추가한다. 재고 부족이나 입력 중단 상태에는 false를 반환한다.</summary>
        public bool CollectControlHints(WorkshopControlHintState state)
        {
            if ((_tray != null && !_tray.CanInteractWithTray) || !CanBeginCardDrag()) return false;
            state.Add("좌클릭 드래그", _entry.IsStructure ? "부품 꺼내기" : _entry.IsSwitch ? "스위치 꺼내기" : "키캡 꺼내기");
            return true;
        }

        /// <summary>연결된 원본 entry의 출고 가능 여부를 기존 키캡 또는 구조 부품 처리기에 조회한다. 재고와 실물은 변경하지 않는다.</summary>
        private bool CanBeginCardDrag()
        {
            return _structureDrag != null ? _structureDrag.CanBeginStockDrag(_entry) : _placement.CanBeginInventoryDrag(_entry);
        }

        /// <summary>point에서 기존 종류별 출고 처리기를 호출한다. 목록 내부 스크롤 종료 뒤에만 예약과 실물 드래그를 시작한다.</summary>
        private void BeginCardDrag(Vector2 point)
        {
            if (_structureDrag != null) _structureDrag.BeginStockDrag(_entry, point);
            else _placement.BeginInventoryDrag(_entry, point);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            eventData.eligibleForClick = false;
            if (_tray == null) { BeginCardDrag(InGameCursor.Position); return; }
            if (!_tray.CanInteractWithTray || !CanBeginCardDrag()) return;
            _scroll = GetComponentInParent<ScrollRect>();
            _scrolling = true;
            _placement.SetInventoryScroll(this, true);
            _scroll.OnBeginDrag(eventData);
            _placement.ShowInventoryIcon(_entry, InGameCursor.Position);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging && _structureDrag != null)
                _structureDrag.SelectPcbMaterial(_entry);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_scrolling) return;
            if (!_tray.IsTrayOpen || !_placement.Pullers.InputAllowed) { EndScroll(eventData); return; }
            Vector2 point = InGameCursor.Position;
            if (RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport, point, eventData.pressEventCamera))
            {
                _scroll.OnDrag(eventData);
                _placement.ShowInventoryIcon(_entry, point);
                return;
            }
            EndScroll(eventData);
            BeginCardDrag(point);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_scrolling) EndScroll(eventData);
        }

        void Update()
        {
            if (_scrolling && (!_tray.IsTrayOpen || !_placement.Pullers.InputAllowed || Mouse.current == null || !Mouse.current.leftButton.isPressed))
                EndScroll(new PointerEventData(EventSystem.current));
        }

        void OnDisable()
        {
            if (_scrolling) EndScroll(new PointerEventData(EventSystem.current));
        }

        /// <summary>
        /// 이 카드의 진행 중 목록 드래그를 취소하고 공통 입력 점유와 아이콘을 해제한다.
        /// 재고와 실물은 아직 출고하지 않았으므로 수량은 변경하지 않는다.
        /// </summary>
        public void CancelInventoryScroll()
        {
            if (_scrolling) EndScroll(new PointerEventData(EventSystem.current));
        }

        /// <summary>eventData로 목록 드래그를 종료하고 카드 아이콘을 숨긴다. 출고 후 해제는 기존 재고 Update만 처리한다.</summary>
        private void EndScroll(PointerEventData eventData)
        {
            _scrolling = false;
            _placement.SetInventoryScroll(this, false);
            _scroll.OnEndDrag(eventData);
            _scroll.StopMovement();
            _placement.ShowInventoryIcon(null, Vector2.zero);
        }
    }
}
