using System;

using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class EnemyRoundController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private EnemyTypingController _enemy;
        [SerializeField] private EnemyKeyboardProvider _provider;

        [Header("Round")]
        [SerializeField, Min(0.01f)] private float _duration = 30f;
        [SerializeField] private bool _startOnPlay;
        private float _remainingTime;
        private bool _isRunning;
        public bool IsRunning => _isRunning;
        public float RemainingTime => _remainingTime;
        public event Action RoundStarted;
        public event Action RoundEnded;

        void OnEnable()
        {
            _provider.KeyboardChanged += HandleKeyboardChanged;
        }

        void Start()
        {
            if (_startOnPlay)
                StartRound();
        }

        void Update()
        {
            if (!_isRunning || Time.deltaTime <= 0f)
                return;
            _remainingTime = Mathf.Max(0f, _remainingTime - Time.deltaTime);
            if (_remainingTime == 0f)
            {
                EndRound();
                return;
            }
            _provider.Tick(Time.deltaTime);
            if (_isRunning)
                _enemy.Tick(Time.deltaTime);
        }

        void OnDisable()
        {
            EndRound();
            _provider.KeyboardChanged -= HandleKeyboardChanged;
        }

        /// <summary>
        /// Inspector의 제한시간으로 새 적 라운드를 시작한다.
        /// 이전 실행을 종료하고 제한시간을 초기화한 뒤 키보드 공급과 적의 공격을 시작한다.
        /// </summary>
        [ContextMenu("Start Round")]
        public void StartRound()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;
            EndRound();
            _remainingTime = _duration;
            _isRunning = true;
            _provider.StartSupply();
            if (!_isRunning)
                return;
            _enemy.StartAttacking();
            RoundStarted?.Invoke();
        }

        /// <summary>
        /// 라운드를 종료하고 실행 종료 사실을 알린다.
        /// 입력값 없이 공격과 공급 예약을 취소하며 점수나 파괴 결과를 집계하지 않는다.
        /// </summary>
        [ContextMenu("End Round")]
        public void EndRound()
        {
            if (!_isRunning)
                return;
            _isRunning = false;
            _enemy.StopAttacking();
            _provider.StopSupply();
            _enemy.SetKeyboard(null);
            RoundEnded?.Invoke();
        }

        /// <summary>
        /// keyboard 변경을 현재 적의 공격 대상에 반영한다.
        /// 실행 중인 라운드의 키보드만 연결하고 종료 상태에서는 대상을 해제한다.
        /// </summary>
        private void HandleKeyboardChanged(KeyboardInputController keyboard)
        {
            _enemy.SetKeyboard(_isRunning ? keyboard : null);
        }

    }
}
