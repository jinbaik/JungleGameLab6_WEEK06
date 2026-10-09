using UnityEngine;

using KeyboardModeling;

[DisallowMultipleComponent]
public sealed class KeyboardFeedbackManager : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private KeyboardInteractionController _interactionController;
    [SerializeField] private KeyboardInputController _previewKeyboard;
    private KeyboardInputController _keyboard;

    [Header("Live Options")]
    [SerializeField] private bool _enableHitStop = true;

    [Header("Hit Stop")]
    [SerializeField, Range(0.005f, 0.5f)] private float _hitStopDuration = 0.25f;
    [SerializeField, Min(0f)] private float _hitStopCooldown = 0.35f;
    private bool _isHitStopped;
    private float _savedTimeScale;
    private float _hitStopEnd;
    private float _nextHitStop;

    void OnEnable()
    {
        if (_interactionController != null)
        {
            _interactionController.ActiveKeyboardChanged += BindKeyboard;
            BindKeyboard(_interactionController.ActiveKeyboard);
        }
        else
            BindKeyboard(_previewKeyboard);
    }

    void Update()
    {
        if (_isHitStopped && (!_enableHitStop || Time.unscaledTime >= _hitStopEnd))
            EndHitStop();
        if (_keyboard != null)
            _keyboard.SetHitStopEnabled(_enableHitStop);
    }

    void OnDisable()
    {
        if (_interactionController != null)
            _interactionController.ActiveKeyboardChanged -= BindKeyboard;
        BindKeyboard(null);
        EndHitStop();
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) EndHitStop();
    }

    /// <summary>
    /// keyboard의 새 눌림 이벤트를 구독하고 이전 대상의 연결을 해제한다.
    /// 전달된 입력 컨트롤러에 히트스톱 사용 여부를 반영하고 대상 교체 시 시간 배율을 복원한다.
    /// </summary>
    private void BindKeyboard(KeyboardInputController keyboard)
    {
        if (_keyboard != null)
        {
            _keyboard.KeycapPressed -= HandleKeycapPressed;
            _keyboard.SetHitStopEnabled(false);
        }
        EndHitStop();
        _keyboard = keyboard;
        if (_keyboard == null) return;
        _keyboard.SetHitStopEnabled(_enableHitStop);
        _keyboard.KeycapPressed += HandleKeycapPressed;
    }

    /// <summary>
    /// 살아 있는 keycap의 새 눌림에서 히트스톱을 시작한다.
    /// 현재 옵션과 재발동 시간을 확인하고 기존 시간 배율을 저장한 뒤 지정 시간 동안 정지한다.
    /// </summary>
    private void HandleKeycapPressed(Transform keycap)
    {
        KeycapHealth health = keycap.GetComponent<KeycapHealth>();
        if (health != null && health.CurrentHP <= 0) return;
        if (_enableHitStop && !_isHitStopped && Time.timeScale > 0f && Time.unscaledTime >= _nextHitStop)
        {
            _savedTimeScale = Time.timeScale;
            _hitStopEnd = Time.unscaledTime + _hitStopDuration;
            _nextHitStop = _hitStopEnd + _hitStopCooldown;
            _isHitStopped = true;
            Time.timeScale = 0f;
        }
    }

    /// <summary>
    /// 매니저가 시작한 히트스톱의 이전 시간 배율을 복원한다.
    /// 저장한 배율과 소유 상태를 사용하며 히트스톱 종료 상태로 변경한다.
    /// </summary>
    private void EndHitStop()
    {
        if (!_isHitStopped) return;
        if (Time.timeScale == 0f) Time.timeScale = _savedTimeScale;
        _isHitStopped = false;
    }
}
