using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KeyboardModeling.Assembly
{
    public sealed class AssemblyCatalogScrollRect : ScrollRect
    {
        /// <summary>
        /// eventData의 목록 드래그 시작을 무시하여 부품의 즉시 드래그와 충돌하지 않게 한다.
        /// 목록 위치는 휠과 스크롤바로만 변경한다.
        /// </summary>
        public override void OnBeginDrag(PointerEventData eventData) { }

        /// <summary>
        /// eventData의 목록 끌기 입력을 무시한다.
        /// 기존 콘텐츠 위치와 부품 드래그 입력은 변경하지 않는다.
        /// </summary>
        public override void OnDrag(PointerEventData eventData) { }

        /// <summary>
        /// eventData의 목록 끌기 종료 입력을 무시한다.
        /// 부품 드래그 종료는 공용 드래그 입력에서 처리한다.
        /// </summary>
        public override void OnEndDrag(PointerEventData eventData) { }
    }
}
