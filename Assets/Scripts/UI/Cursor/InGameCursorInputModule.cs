using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

/// <summary>잠긴 OS 커서와 독립적으로 가상 좌표의 Unity UI 클릭과 드래그 및 탐색을 처리한다.</summary>
[DisallowMultipleComponent]
public sealed class InGameCursorInputModule : StandaloneInputModule
{
    [Header("Virtual Pointer")]
    private readonly MouseState _mouseState = new MouseState();
    private InputAction _move;
    private InputAction _submit;
    private InputAction _cancel;
    private Vector2 _lastMove;
    private float _nextMoveTime;
    private float _repeatDelay;
    private float _repeatRate;
    private float _scrollDeltaPerTick;

    /// <summary>source의 탐색 바인딩을 독립 복제해 원본 UI 모듈을 중단해도 키보드와 게임패드 탐색을 유지한다.</summary>
    public void Initialize(InputSystemUIInputModule source)
    {
        _move = source.move?.action.Clone();
        _submit = source.submit?.action.Clone();
        _cancel = source.cancel?.action.Clone();
        _repeatDelay = source.moveRepeatDelay;
        _repeatRate = source.moveRepeatRate;
        _scrollDeltaPerTick = source.scrollDeltaPerTick;
    }

    /// <summary>가상 커서가 입력을 허용할 때만 이 모듈의 활성화를 요청한다. OS 잠금 상태는 검사하지 않는다.</summary>
    public override bool ShouldActivateModule() => enabled && InGameCursor.CanProcessVirtualPointer;

    /// <summary>가상 좌표를 사용하므로 실제 마우스 지원 여부와 관계없이 모듈 지원을 반환한다.</summary>
    public override bool IsModuleSupported() => true;

    /// <summary>레거시 Input 조회 없이 입력 갱신을 가상 커서 관리자에게 맡긴다.</summary>
    public override void UpdateModule() { }

    /// <summary>기존 UI 선택을 유지하며 독립 탐색 액션을 활성화한다. 레거시 마우스 API는 사용하지 않는다.</summary>
    public override void ActivateModule()
    {
        _move?.Enable();
        _submit?.Enable();
        _cancel?.Enable();
        _lastMove = Vector2.zero;
        _nextMoveTime = 0f;
        if (eventSystem.currentSelectedGameObject == null && eventSystem.firstSelectedGameObject != null)
            eventSystem.SetSelectedGameObject(eventSystem.firstSelectedGameObject, GetBaseEventData());
    }

    /// <summary>버튼 눌림과 드래그를 클릭 없이 종료하고 탐색 액션을 비활성화한다. 현재 UI 선택은 유지한다.</summary>
    public override void DeactivateModule()
    {
        foreach (PointerEventData pointer in m_PointerData.Values)
        {
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            if (pointer.dragging)
                ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            HandlePointerExitAndEnter(pointer, null);
        }
        m_PointerData.Clear();
        _move?.Disable();
        _submit?.Disable();
        _cancel?.Disable();
    }

    /// <summary>선택 UI의 텍스트 갱신과 탐색 후 가상 커서의 클릭·드래그·휠을 기존 Unity 처리로 전달한다.</summary>
    public override void Process()
    {
        if (!InGameCursor.CanProcessVirtualPointer || Mouse.current == null) return;
        bool used = SendUpdateEventToSelectedObject();
        if (!used && eventSystem.sendNavigationEvents) ProcessNavigation();
        ProcessMouseEvent();
    }

    /// <summary>복제된 탐색 액션으로 이동 반복과 확인·취소를 전달한다. 기존 선택과 액션 바인딩을 사용한다.</summary>
    private void ProcessNavigation()
    {
        Vector2 movement = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        bool used = false;
        if (movement.sqrMagnitude > 0.01f)
        {
            bool changed = Vector2.Dot(movement, _lastMove) <= 0f;
            if (changed || Time.unscaledTime >= _nextMoveTime)
            {
                AxisEventData data = GetAxisEventData(movement.x, movement.y, 0.5f);
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, data, ExecuteEvents.moveHandler);
                used = data.used;
                _nextMoveTime = Time.unscaledTime + (changed ? _repeatDelay : _repeatRate);
            }
        }
        _lastMove = movement;
        if (used) return;
        BaseEventData buttonData = GetBaseEventData();
        if (_submit != null && _submit.WasPressedThisFrame())
            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, buttonData, ExecuteEvents.submitHandler);
        if (!buttonData.used && _cancel != null && _cancel.WasPressedThisFrame())
            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, buttonData, ExecuteEvents.cancelHandler);
    }

    /// <summary>id의 포인터를 공통 가상 좌표와 실제 버튼으로 갱신하고 Unity 마우스 상태로 반환한다.</summary>
    protected override MouseState GetMousePointerEventData(int id)
    {
        bool created = GetPointerData(kMouseLeftId, out PointerEventData left, true);
        Vector2 previous = left.position;
        left.Reset();
        left.position = InGameCursor.Position;
        left.delta = created ? Vector2.zero : left.position - previous;
        float scrollUnits = 1f;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange)
            scrollUnits = 120f;
#endif
        left.scrollDelta = Mouse.current.scroll.ReadValue() / scrollUnits * _scrollDeltaPerTick;
        left.button = PointerEventData.InputButton.Left;
        eventSystem.RaycastAll(left, m_RaycastResultCache);
        left.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResultCache);
        m_RaycastResultCache.Clear();
        GetPointerData(kMouseRightId, out PointerEventData right, true);
        CopyFromTo(left, right);
        right.button = PointerEventData.InputButton.Right;
        GetPointerData(kMouseMiddleId, out PointerEventData middle, true);
        CopyFromTo(left, middle);
        middle.button = PointerEventData.InputButton.Middle;
        _mouseState.SetButtonState(PointerEventData.InputButton.Left, GetPressState(Mouse.current.leftButton), left);
        _mouseState.SetButtonState(PointerEventData.InputButton.Right, GetPressState(Mouse.current.rightButton), right);
        _mouseState.SetButtonState(PointerEventData.InputButton.Middle, GetPressState(Mouse.current.middleButton), middle);
        return _mouseState;
    }

    /// <summary>button의 현재 프레임 눌림과 해제를 Unity 클릭 상태로 변환해 반환한다.</summary>
    private static PointerEventData.FramePressState GetPressState(ButtonControl button)
    {
        if (button.wasPressedThisFrame && button.wasReleasedThisFrame) return PointerEventData.FramePressState.PressedAndReleased;
        if (button.wasPressedThisFrame) return PointerEventData.FramePressState.Pressed;
        return button.wasReleasedThisFrame ? PointerEventData.FramePressState.Released : PointerEventData.FramePressState.NotChanged;
    }

    /// <summary>scrollDelta를 원본 UI 모듈의 휠 배율로 나눠 틱 단위로 반환한다. UI의 기존 스크롤 감도를 유지한다.</summary>
    public override Vector2 ConvertPointerEventScrollDeltaToTicks(Vector2 scrollDelta)
    {
        return Mathf.Approximately(_scrollDeltaPerTick, 0f) ? Vector2.zero : scrollDelta / _scrollDeltaPerTick;
    }

    /// <summary>pointerEvent의 레이캐스트 대상에 호버 이동을 알린다. OS 잠금과 관계없이 가상 좌표를 사용한다.</summary>
    protected override void ProcessMove(PointerEventData pointerEvent)
    {
        HandlePointerExitAndEnter(pointerEvent, pointerEvent.pointerCurrentRaycast.gameObject);
    }

    /// <summary>pointerEvent의 가상 좌표와 기존 임계값으로 드래그 시작과 이동을 알린다. 클릭 해제와 드롭은 Unity 처리를 사용한다.</summary>
    protected override void ProcessDrag(PointerEventData pointerEvent)
    {
        if (!pointerEvent.IsPointerMoving() || pointerEvent.pointerDrag == null) return;
        float threshold = eventSystem.pixelDragThreshold;
        if (!pointerEvent.dragging && (!pointerEvent.useDragThreshold
            || (pointerEvent.position - pointerEvent.pressPosition).sqrMagnitude >= threshold * threshold))
        {
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.beginDragHandler);
            pointerEvent.dragging = true;
        }
        if (!pointerEvent.dragging) return;
        if (pointerEvent.pointerPress != pointerEvent.pointerDrag)
        {
            ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);
            pointerEvent.eligibleForClick = false;
            pointerEvent.pointerPress = null;
            pointerEvent.rawPointerPress = null;
        }
        ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.dragHandler);
    }

    protected override void OnDestroy()
    {
        _move?.Dispose();
        _submit?.Dispose();
        _cancel?.Dispose();
        base.OnDestroy();
    }
}
