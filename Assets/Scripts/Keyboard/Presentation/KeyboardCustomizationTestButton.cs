using System;

using UnityEngine;
using UnityEngine.EventSystems;

public sealed class KeyboardCustomizationTestButton : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
{
    public event Action Clicked;

    /// <summary>
    /// eventData의 누름 대상을 이 버튼으로 유지해 부모 오버레이가 클릭을 가져가지 않도록 한다.
    /// 상태를 변경하지 않으며 실제 동작은 OnPointerClick에서 실행한다.
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
    }

    /// <summary>eventData의 왼쪽 클릭을 받아 연결된 C# 이벤트를 호출한다. 반환값 없이 UI 동작을 실행한다.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
    }
}
