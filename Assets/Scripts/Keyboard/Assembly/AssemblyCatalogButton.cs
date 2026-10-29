using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KeyboardModeling.Assembly
{
    public sealed class AssemblyCatalogButton : Button
    {
        public event Action<AssemblyCatalogButton> Clicked;
        public event Action<AssemblyCatalogButton, Vector2> Pressed;

        /// <summary>
        /// eventData의 왼쪽 버튼 Down을 화면 좌표와 함께 C# 이벤트로 전달한다.
        /// 누른 순간 재고 예약과 아이콘 드래그를 시작할 수 있도록 Click 이벤트와 구분한다.
        /// </summary>
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            base.OnPointerDown(eventData);
            if (eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
                Pressed?.Invoke(this, eventData.position);
        }

        /// <summary>
        /// eventData의 왼쪽 클릭을 받아 카탈로그 선택 이벤트를 전달한다.
        /// 활성 상태와 드래그 여부를 검사하며 소리나 3D 부품은 변경하지 않는다.
        /// </summary>
        public override void OnPointerClick(PointerEventData eventData)
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging
                || !IsActive() || !IsInteractable())
                return;

            Clicked?.Invoke(this);
        }

        /// <summary>
        /// eventData의 제출 입력을 활성 카탈로그 버튼의 C# 이벤트로 전달한다.
        /// 선택된 버튼을 구독자에게 전달하며 부품 상태는 변경하지 않는다.
        /// </summary>
        public override void OnSubmit(BaseEventData eventData)
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (IsActive() && IsInteractable())
                Clicked?.Invoke(this);
        }
    }
}
