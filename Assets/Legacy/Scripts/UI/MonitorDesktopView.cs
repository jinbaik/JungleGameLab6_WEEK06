using System;

using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MonitorDesktopView : MonoBehaviour, IPointerClickHandler
{
    [Header("Desktop Input")]
    [SerializeField] private CanvasGroup _desktopCanvasGroup;
    private bool _isInputEnabled;
    public event Action ShopOpenRequested;

    /// <summary>
    /// eventData의 왼쪽 더블 클릭으로 상점 앱 열기를 요청한다.
    /// 바탕화면 입력이 허용된 경우에만 ShopOpenRequested를 전달하며 창은 직접 생성하지 않는다.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isInputEnabled && eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2)
            ShopOpenRequested?.Invoke();
    }

    /// <summary>
    /// isEnabled로 바탕화면의 앱 입력 허용 상태를 변경한다.
    /// 배경 표시는 유지하면서 CanvasGroup의 상호작용과 레이캐스트만 설정한다.
    /// </summary>
    public void SetInputEnabled(bool isEnabled)
    {
        _isInputEnabled = isEnabled;
        _desktopCanvasGroup.interactable = isEnabled;
        _desktopCanvasGroup.blocksRaycasts = isEnabled;
    }
}
