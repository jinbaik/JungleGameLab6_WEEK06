using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class KeycapButton : MonoBehaviour
{
    [Header("Press Action")]
    [SerializeField] private UnityEvent _onPressed = new UnityEvent();
    private bool _isPressed;

    public UnityEvent OnPressed => _onPressed;

    /// <summary>
    /// source에 설정된 기존 누름 동작을 새 키캡 버튼으로 전달한다.
    /// 이벤트 객체를 유지하여 프리팹 교체 후에도 사용자 지정 동작을 보존한다.
    /// </summary>
    public void PreservePressAction(KeycapButton source)
    {
        _onPressed = source._onPressed;
    }

    void OnDisable()
    {
        ResetPressState();
    }

    /// <summary>
    /// 키캡의 눌림 상태가 바뀌면 처음 눌린 순간의 기능을 실행한다.
    /// isPressed를 _isPressed에 저장하며 false에서 true로 바뀔 때만 OnPressed를 호출한다.
    /// </summary>
    public void SetPressed(bool isPressed)
    {
        if (!isActiveAndEnabled || _isPressed == isPressed)
            return;

        _isPressed = isPressed;
        if (isPressed)
            _onPressed.Invoke();
    }

    /// <summary>
    /// 기능을 실행하지 않고 키캡의 입력 상태를 초기화한다.
    /// 별도 입력 없이 _isPressed를 false로 바꾸어 다음 누름을 받을 수 있게 한다.
    /// </summary>
    public void ResetPressState()
    {
        _isPressed = false;
    }
}
