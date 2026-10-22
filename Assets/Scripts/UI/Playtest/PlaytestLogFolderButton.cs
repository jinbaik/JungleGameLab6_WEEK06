using System;

using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UI
{
    public sealed class PlaytestLogFolderButton : MonoBehaviour, IPointerClickHandler
    {
        public event Action Clicked;

        /// <summary>
        /// eventData의 왼쪽 클릭을 로그 폴더 요청용 C# 이벤트로 전달한다.
        /// 씬에 배치된 버튼이 활성 상태일 때만 Clicked를 발생시킨다.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || eventData.button != PointerEventData.InputButton.Left) return;
            AudioManager.instance.PlayButtonSound();
            Clicked?.Invoke();
        }
    }
}
