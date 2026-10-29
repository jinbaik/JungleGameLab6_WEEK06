using System;

using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MonitorGoalCloseButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Input")]
    [SerializeField] private CanvasGroup _popupCanvasGroup;
    public event Action Clicked;

    /// <summary>
    /// eventData의 왼쪽 클릭을 받아 조작 가능한 팝업의 닫기를 요청한다.
    /// CanvasGroup의 입력 상태를 확인하고 Clicked 이벤트를 전달한다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_popupCanvasGroup.interactable && eventData.button == PointerEventData.InputButton.Left)
        {
            AudioManager.instance.PlayButtonSound();
            Clicked?.Invoke();
        }
    }
}
