using System;

using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MonitorSettingsButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Input")]
    [SerializeField] private CanvasGroup _inputCanvasGroup;
    public event Action Clicked;

    /// <summary>
    /// eventData의 왼쪽 클릭을 조작 가능한 설정 버튼의 C# 이벤트로 전달한다.
    /// 입력 그룹이 잠겨 있으면 Clicked 이벤트를 발생시키지 않는다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_inputCanvasGroup.interactable && eventData.button == PointerEventData.InputButton.Left)
        {
            AudioManager.instance.PlayButtonSound();
            Clicked?.Invoke();
        }
    }
}
