using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class KeyboardScoreCombo : MonoBehaviour
{
    private const int BASE_SCORE = 10;
    private const int KEYCAP_BREAK_SCORE = 10;

    [Header("Input")]
    [SerializeField] private KeyboardInputController _keyboard;
    [SerializeField] private bool _includeAutomaticHits;
    private KeyboardInputController _boundKeyboard;
    private KeycapHealth[] _keycaps = Array.Empty<KeycapHealth>();
    private IReadOnlyDictionary<Key, KeycapHealth> _keycapLayout;
    private readonly HashSet<KeycapHealth> _scoredKeycaps = new HashSet<KeycapHealth>();
    private KeyboardInputController _scoredKeyboard;
    private bool _keyboardBreakSettled;

    [Header("Runtime State")]
    private long _score;
    private int _combo;
    private float _remainingTime;
    private bool _isTimerPaused;
    private bool _isTracking = true;
    private float _bonusRemaining;

    public long Score => _score;
    public int Combo => _combo;
    public float Multiplier => _combo >= 150 ? 2.5f : _combo >= 100 ? 2f : _combo >= 50 ? 1.5f : 1f;
    public Color StageColor => _combo >= 150 ? Color.red : _combo >= 100 ? new Color(1f, 0.5f, 0f) : _combo >= 50 ? Color.yellow : Color.white;
    public float TimeRatio => _combo == 0 ? 0f : _remainingTime / Duration;
    private float Duration => _combo >= 150 ? 5f : _combo >= 100 ? 6f : _combo >= 50 ? 8f : 10f;
    public bool IsTimerPaused => _isTimerPaused;
    public float BonusRemaining => _bonusRemaining;

    public KeyboardInputController Keyboard
    {
        get => _keyboard;
        set => SetKeyboard(value);
    }

    public event Action StateChanged;
    public event Action<float> TimerChanged;

    void OnEnable()
    {
        SubscribeKeyboard();
    }

    void OnDisable()
    {
        UnsubscribeKeyboard();
    }

    void Update()
    {
        if (Game.Tournament.TournamentController.IsReturnPaused) return;
        if (_isTracking) _bonusRemaining = Mathf.Max(0f, _bonusRemaining - Time.deltaTime);
        if (!_isTracking || _combo == 0 || _isTimerPaused)
        {
            return;
        }

        if (_keyboard == null || (!_includeAutomaticHits && !_keyboard.InputEnabled))
        {
            return;
        }

        _remainingTime = Mathf.Max(0f, _remainingTime - Time.deltaTime);
        if (_remainingTime == 0f)
        {
            _combo = 0;
            StateChanged?.Invoke();
        }
        TimerChanged?.Invoke(TimeRatio);
    }

    /// <summary>
    /// keyboard를 점수와 콤보를 추적할 현재 키보드로 변경한다.
    /// 이전 구독을 정리하고 새 입력과 파괴 이벤트를 연결하며 누적 점수와 콤보는 유지한다.
    /// </summary>
    public void SetKeyboard(KeyboardInputController keyboard)
    {
        UnsubscribeKeyboard();
        _keyboard = keyboard;
        SubscribeKeyboard();
    }

    /// <summary>
    /// 콤보 타이머 감소의 일시정지 여부를 설정한다.
    /// isPaused 일시정지 여부를 _isTimerPaused 상태에 반영한다.
    /// </summary>
    public void SetTimerPaused(bool isPaused)
    {
        _isTimerPaused = isPaused;
    }

    /// <summary>
    /// 현재 누적된 콤보와 잔여 유지시간을 초기화한다.
    /// _combo와 _remainingTime을 0으로 재설정하고 상태 및 타이머 변경 이벤트를 호출한다.
    /// </summary>
    public void ResetCombo()
    {
        _combo = 0;
        _remainingTime = 0f;
        _isTimerPaused = false;
        StateChanged?.Invoke();
        TimerChanged?.Invoke(0f);
    }

    /// <summary>
    /// enabled로 입력 점수와 파괴 콤보의 수집 여부를 변경한다.
    /// 수집을 멈추면 이벤트와 콤보 타이머를 중단하고 현재 점수와 콤보 상태를 유지한다.
    /// </summary>
    public void SetTrackingEnabled(bool enabled)
    {
        if (_isTracking == enabled)
            return;
        _isTracking = enabled;
        if (enabled)
            SubscribeKeyboard();
        else
            UnsubscribeKeyboard();
    }

    /// <summary>
    /// 새 게임을 위해 점수와 콤보 및 유지시간을 초기화한다.
    /// 입력값 없이 상태를 0으로 변경하고 타이머 일시정지를 해제하며 연결된 화면에 상태와 빈 게이지를 알린다.
    /// </summary>
    public void ResetState()
    {
        _score = 0;
        _bonusRemaining = 0f;
        ResetCombo();
    }

    /// <summary>완파된 현재 키보드의 미정산 키캡을 일반 파괴와 동일한 점수·콤보로 한 번 정산한다. 실제 체력과 파괴 이벤트는 변경하지 않아 능력은 추가 발동하지 않는다.</summary>
    public void AddKeyboardBreakScore()
    {
        if (!_isTracking || !isActiveAndEnabled || _boundKeyboard == null || _keyboardBreakSettled) return;
        _keyboardBreakSettled = true;
        foreach (KeycapHealth keycap in _keycaps)
            if (keycap != null && keycap.gameObject.activeSelf) HandleKeycapBroken(keycap);
    }

    /// <summary>duration초 동안 점수 2배를 적용하며 효과 중 재발동하면 남은 시간을 갱신한다.</summary>
    public void ActivateBonus(float duration)
    {
        _bonusRemaining = duration;
    }

    /// <summary>입력값 없이 보너스 효과와 콤보를 초기화하고 누적 점수는 유지한다.</summary>
    public void ResetBattleEffects()
    {
        _bonusRemaining = 0f;
        ResetCombo();
    }

    /// <summary>
    /// 설정된 현재 키보드의 입력과 키캡 파괴를 구독한다.
    /// 수집 상태와 자동 타격 설정을 사용하여 키별 체력 매핑을 갱신하며 키보드가 공급되지 않은 동안에는 구독하지 않는다.
    /// </summary>
    private void SubscribeKeyboard()
    {
        if (!isActiveAndEnabled || !_isTracking || _keyboard == null || _boundKeyboard != null)
            return;
        if (_scoredKeyboard != _keyboard)
        {
            _scoredKeyboard = _keyboard;
            _scoredKeycaps.Clear();
            _keyboardBreakSettled = false;
        }
        _boundKeyboard = _keyboard;
        _keycaps = _keyboard.GetComponentsInChildren<KeycapHealth>(true);
        _keycapLayout = _keyboard.GetKeycapLayout();
        _boundKeyboard.KeyPressed += HandleKeyPressed;
        if (_includeAutomaticHits)
            _boundKeyboard.AutomaticKeycapHit += HandleAutomaticKeycapHit;
        foreach (KeycapHealth keycap in _keycaps)
            keycap.Broken += HandleKeycapBroken;
    }

    /// <summary>
    /// 이전에 연결한 키보드의 입력과 파괴 이벤트를 해제한다.
    /// 저장된 키보드와 키캡 참조를 정리하여 교체되거나 종료된 대상의 중복 수집을 방지한다.
    /// </summary>
    private void UnsubscribeKeyboard()
    {
        if (_boundKeyboard != null)
        {
            _boundKeyboard.KeyPressed -= HandleKeyPressed;
            _boundKeyboard.AutomaticKeycapHit -= HandleAutomaticKeycapHit;
        }
        foreach (KeycapHealth keycap in _keycaps)
        {
            if (keycap != null)
                keycap.Broken -= HandleKeycapBroken;
        }
        _boundKeyboard = null;
        _keycaps = Array.Empty<KeycapHealth>();
        _keycapLayout = null;
    }

    /// <summary>
    /// key에 연결된 키캡의 현재 체력이 남아 있을 때만 현재 콤보 배율로 누적 점수를 증가시킨다.
    /// 공격과 파괴 전에 체력을 확인하여 마지막 파괴 입력까지 점수를 지급하고 상태 변경을 알린다.
    /// </summary>
    private void HandleKeyPressed(Key key)
    {
        if (!_keycapLayout.TryGetValue(key, out KeycapHealth keycap) || keycap == null || keycap.CurrentHP <= 0)
        {
            return;
        }

        AddHitScore();
    }

    /// <summary>
    /// 성공한 자동 타격의 keycap 알림을 기존 타건 점수로 처리한다.
    /// 현재 콤보 배율을 사용하며 플레이어 입력이나 추가 피해 요청은 발생시키지 않는다.
    /// </summary>
    private void HandleAutomaticKeycapHit(Transform keycap)
    {
        AddHitScore();
    }

    /// <summary>
    /// 현재 타건에 콤보 배율을 적용하여 점수를 더한다.
    /// 수집 상태와 BASE_SCORE 및 Multiplier를 사용하여 누적 점수를 변경하고 화면에 알린다.
    /// </summary>
    private void AddHitScore()
    {
        if (!_isTracking || !isActiveAndEnabled)
            return;
        _score += (int)(BASE_SCORE * Multiplier) * (_bonusRemaining > 0f ? 2 : 1);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// keycap의 실제 파괴 또는 완파 정산을 받아 중복 없이 현재 콤보 배율의 파괴 점수를 지급한다.
    /// KEYCAP_BREAK_SCORE와 Multiplier로 점수·콤보·유지시간을 갱신하며 완파를 유발한 키의 이벤트 순서에도 중복 정산하지 않는다.
    /// </summary>
    private void HandleKeycapBroken(KeycapHealth keycap)
    {
        keycap.Broken -= HandleKeycapBroken;
        if (!_isTracking || !isActiveAndEnabled || !_scoredKeycaps.Add(keycap))
            return;
        _score += (int)(KEYCAP_BREAK_SCORE * Multiplier) * (_bonusRemaining > 0f ? 2 : 1);
        _combo++;
        _remainingTime = Duration;
        StateChanged?.Invoke();
        TimerChanged?.Invoke(TimeRatio);
    }
}
