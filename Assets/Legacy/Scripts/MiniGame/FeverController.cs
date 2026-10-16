using System;

using UnityEngine;

using Game.Session;

namespace KeyboardModeling
{
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class FeverController : MonoBehaviour
    {
        public enum PauseReason { None, KeyboardUnavailable, InputDisabled, GamePaused }

        private const int MAX_CHARGE = 1000;
        private const int HIT_CHARGE = 1;
        private const int CLEAR_CHARGE = 500;
        private const float BASE_DURATION = 5f;
        private const float BASE_MULTIPLIER = 2f;

        [Header("Dependencies")]
        [SerializeField] private MiniGameController _miniGameController;
        [Tooltip("강화가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;

        [Header("Runtime State")]
        private int _chargeUnits;
        private float _duration = BASE_DURATION;
        private float _multiplier = BASE_MULTIPLIER;
        private float _remaining;
        private PauseReason _lastPauseReason;
        public bool IsFeverActive => _remaining > 0f;
        public float Charge => (float)_chargeUnits / MAX_CHARGE;
        public float Remaining => _remaining;
        public float Duration => _duration;
        public float DamageMultiplier => IsFeverActive ? _multiplier : 1f;
        public PauseReason CurrentPauseReason => !_miniGameController.HasKeyboard
            ? PauseReason.KeyboardUnavailable
            : !_miniGameController.InputAvailable || !isActiveAndEnabled
                ? PauseReason.InputDisabled
                : Time.timeScale == 0f ? PauseReason.GamePaused : PauseReason.None;
        public bool IsSuspended => CurrentPauseReason != PauseReason.None;
        public event Action StateChanged;
        public event Action Ended;

        void OnEnable()
        {
            _miniGameController.Completed += HandleMiniGameCompleted;
            _miniGameController.StateChanged += HandleInputStateChanged;
            HandleInputStateChanged();
        }

        void OnDisable()
        {
            _miniGameController.Completed -= HandleMiniGameCompleted;
            _miniGameController.StateChanged -= HandleInputStateChanged;
            StateChanged?.Invoke();
        }

        void Update()
        {
            if (_lastPauseReason != CurrentPauseReason)
                HandleInputStateChanged();

            if (!IsFeverActive || IsSuspended)
                return;

            _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);
            if (_remaining == 0f)
            {
                _chargeUnits = 0;
                Ended?.Invoke();
                StateChanged?.Invoke();
            }
        }

        /// <summary>
        /// 미니게임 완료 알림을 받아 정수 충전량을 500 증가시킨다.
        /// 타격 충전량과 합산하여 최대치에 도달하면 현재 강화 값을 확정하고 피버를 시작한다.
        /// </summary>
        private void HandleMiniGameCompleted()
        {
            AddCharge(CLEAR_CHARGE);
        }

        /// <summary>
        /// 실제 피해가 적용된 직접 타격 알림을 받아 정수 충전량을 1 증가시킨다.
        /// 피버 또는 입력 정지 중에는 충전하지 않고 최대치에 도달하면 피버 상태를 변경한다.
        /// </summary>
        public void AddHitCharge()
        {
            AddCharge(HIT_CHARGE);
        }

        /// <summary>
        /// units를 현재 정수 충전량에 합산하여 1000으로 제한한다.
        /// 피버와 정지 상태에서는 무시하고 최대치에 도달하면 세션의 지속시간과 배율을 확정하여 피버를 시작한다.
        /// </summary>
        private void AddCharge(int units)
        {
            if (IsFeverActive || IsSuspended)
                return;

            _chargeUnits = Mathf.Min(MAX_CHARGE, _chargeUnits + units);
            if (_chargeUnits == MAX_CHARGE)
            {
                _duration = _gameSession != null ? _gameSession.Upgrades.GetFeverDuration() : BASE_DURATION;
                _multiplier = _gameSession != null ? _gameSession.Upgrades.GetFeverDamageMultiplier() : BASE_MULTIPLIER;
                _remaining = _duration;
            }
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 미니게임의 입력 대상 변경 또는 정지 상태 변경을 HUD에 알린다.
        /// 현재 정지 사유를 보관하고 StateChanged를 전달하며 충전량과 남은 시간은 유지한다.
        /// </summary>
        private void HandleInputStateChanged()
        {
            _lastPauseReason = CurrentPauseReason;
            StateChanged?.Invoke();
        }
    }
}
