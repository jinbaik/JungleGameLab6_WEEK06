using System.Runtime.InteropServices;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// UI 전용 포인터 상태를 제공한다. Mouse나 Pointer를 상속하지 않아 카메라와 전투의 마우스 바인딩에 참여하지 않는다.
/// </summary>
[InputControlLayout(stateType = typeof(InGameCursorState))]
public sealed class InGameCursorDevice : InputDevice
{
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct InGameCursorState : IInputStateTypeInfo
{
    [FieldOffset(0)] private Vector2 _position;
    [FieldOffset(8)] private Vector2 _scroll;
    [FieldOffset(16)] private ushort _buttons;

    public FourCC format => new FourCC('I', 'G', 'C', 'R');

    [InputControl(name = "position", layout = "Vector2", offset = 0)]
    public Vector2 Position => _position;

    [InputControl(name = "scroll", layout = "Vector2", offset = 8)]
    public Vector2 Scroll => _scroll;

    [InputControl(name = "leftButton", layout = "Button", offset = 16, bit = 0)]
    [InputControl(name = "rightButton", layout = "Button", offset = 16, bit = 1)]
    [InputControl(name = "middleButton", layout = "Button", offset = 16, bit = 2)]
    [InputControl(name = "forwardButton", layout = "Button", offset = 16, bit = 3)]
    [InputControl(name = "backButton", layout = "Button", offset = 16, bit = 4)]
    public ushort Buttons => _buttons;

    /// <summary>position과 scroll 및 buttons로 UI 전용 포인터 상태를 만든다. 물리 마우스의 상속 제어나 상태 형식을 사용하지 않는다.</summary>
    public InGameCursorState(Vector2 position, Vector2 scroll, ushort buttons)
    {
        _position = position;
        _scroll = scroll;
        _buttons = buttons;
    }
}
