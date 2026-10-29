using System;

using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class KeyboardLEDHexInput : InputField
    {
        public event Action<string> TextEdited;
        public event Action EditingFinished;

        /// <summary>
        /// eventData로 선택된 입력란을 갱신하고 실제로 편집된 HEX 문자열을 C# 이벤트로 전달한다.
        /// 코드에서 수행한 알림 없는 텍스트 동기화는 변경 이벤트를 발생시키지 않는다.
        /// </summary>
        public override void OnUpdateSelected(BaseEventData eventData)
        {
            string previous = text;
            base.OnUpdateSelected(eventData);
            if (previous != text) TextEdited?.Invoke(text);
        }

        /// <summary>
        /// eventData로 입력란 선택을 해제하고 HEX 입력 완료를 C# 이벤트로 알린다.
        /// 패널이 유효한 색상 문자열을 다시 표시할 수 있도록 완료 상태를 전달한다.
        /// </summary>
        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            EditingFinished?.Invoke();
        }
    }
}
