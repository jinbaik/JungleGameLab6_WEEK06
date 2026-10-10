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

        private const int CLEARS_REQUIRED = 2;
        private const float BASE_DURATION = 5f;
        private const float BASE_MULTIPLIER = 2f;

        [Header("Dependencies")]
        [SerializeField] private MiniGameController _miniGameController;
        [Tooltip("강화가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;

        [Header("Runtime State")]
        private int _clears;
        private float _duration = BASE_DURATION;
        private float _multiplier = BASE_MULTIPLIER;
        private float _remaining;
        private PauseReason _lastPauseReason;
        public bool IsFeverActive => _remaining > 0f;
        public float Charge => (float)_clears / CLEARS_REQUIRED;
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
                _clears = 0;
                Ended?.Invoke();
                StateChanged?.Invoke();
            }
        }

        /// <summary>
        /// 미니게임 완료 알림을 받아 충전량을 한 단계 증가시킨다.
        /// 두 번 완료하면 세션의 현재 강화 값을 조회해 지속시간과 배율을 확정하고 피버를 시작한다.
        /// </summary>
        private void HandleMiniGameCompleted()
        {
            if (IsFeverActive)
                return;

            _clears = Mathf.Min(CLEARS_REQUIRED, _clears + 1);
            if (_clears == CLEARS_REQUIRED)
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
