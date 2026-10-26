using System;

using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class KeyboardEditButton : Button
    {
        public event Action<KeyboardEditButton> Clicked;

        /// <summary>
        /// 활성 버튼의 왼쪽 클릭을 C# 이벤트로 전달한다.
        /// eventData의 버튼 종류를 확인하며 구독자에게 클릭한 버튼을 전달한다.
        /// </summary>
        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable())
            {
                return;
            }

            if (AudioManager.instance != null) AudioManager.instance.PlayButtonSound();
            base.OnPointerClick(eventData);
            Clicked?.Invoke(this);
        }

        /// <summary>
        /// 활성 버튼의 제출 입력을 C# 이벤트로 전달한다.
        /// eventData를 기본 버튼에 전달하며 구독자에게 선택한 버튼을 전달한다.
        /// </summary>
        public override void OnSubmit(BaseEventData eventData)
        {
            if (!IsActive() || !IsInteractable())
            {
                return;
            }

            if (AudioManager.instance != null) AudioManager.instance.PlayButtonSound();
            base.OnSubmit(eventData);
            Clicked?.Invoke(this);
        }
    }
}
