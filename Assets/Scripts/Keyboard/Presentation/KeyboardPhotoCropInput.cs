using System;

using UnityEngine;
using UnityEngine.EventSystems;

public sealed class KeyboardPhotoCropInput : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    private RectTransform _rectTransform;
    private Vector2 _previousPointerPosition;
    private bool _isDragging;

    public event Action<Vector2> Dragged;
    public event Action<Vector2, float> Zoomed;

    void Awake()
    {
        _rectTransform = transform as RectTransform;
    }

    /// <summary>
    /// 왼쪽 버튼 드래그 시작 위치를 crop 영역 좌표로 기록한다.
    /// eventData의 화면 좌표와 crop 영역 크기를 사용하며 드래그 상태를 갱신한다.
    /// </summary>
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !TryGetNormalizedPosition(eventData, out _previousPointerPosition))
            return;

        _isDragging = true;
    }

    /// <summary>
    /// 마우스 이동량을 crop 영역 기준 오프셋으로 전달한다.
    /// eventData 좌표에서 이동량을 계산하고 구독자에게 변경분을 알린다.
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging || !TryGetNormalizedPosition(eventData, out Vector2 currentPosition))
            return;

        Vector2 delta = currentPosition - _previousPointerPosition;
        _previousPointerPosition = currentPosition;
        Dragged?.Invoke(delta);
    }

    /// <summary>
    /// 왼쪽 버튼 드래그를 종료하고 내부 드래그 상태를 초기화한다.
    /// eventData는 포인터 이벤트 호환을 위해 받으며 반환값은 없다.
    /// </summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        _isDragging = false;
    }

    /// <summary>
    /// 마우스 휠 위치와 세기를 crop 배율 변경 요청으로 전달한다.
    /// eventData의 휠 방향과 crop 영역 내 포인터 좌표를 사용한다.
    /// </summary>
    public void OnScroll(PointerEventData eventData)
    {
        if (TryGetNormalizedPosition(eventData, out Vector2 pointerPosition))
            Zoomed?.Invoke(pointerPosition, eventData.scrollDelta.y);
    }

    /// <summary>
    /// 화면 포인터 좌표를 crop 영역 중심 기준 정규화 좌표로 변환한다.
    /// 변환한 좌표를 반환하며 영역 크기가 없으면 실패를 반환한다.
    /// </summary>
    private bool TryGetNormalizedPosition(PointerEventData eventData, out Vector2 normalizedPosition)
    {
        normalizedPosition = Vector2.zero;
        if (_rectTransform == null || _rectTransform.rect.width <= 0f || _rectTransform.rect.height <= 0f)
            return false;

        Camera eventCamera = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform, eventData.position, eventCamera, out Vector2 localPosition))
            return false;

        normalizedPosition = new Vector2(localPosition.x / _rectTransform.rect.width, localPosition.y / _rectTransform.rect.height);
        return true;
    }
}
