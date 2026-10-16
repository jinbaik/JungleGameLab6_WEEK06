using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class EnemyScoreComboBinding : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnemyRoundController _round;
        [SerializeField] private EnemyKeyboardProvider _provider;
        [SerializeField] private KeyboardScoreCombo _scoreCombo;

        void OnEnable()
        {
            _round.RoundStarted += HandleRoundStarted;
            _round.RoundEnded += HandleRoundEnded;
            _provider.KeyboardChanged += HandleKeyboardChanged;
            _scoreCombo.SetTrackingEnabled(_round.IsRunning);
            _scoreCombo.SetKeyboard(_round.IsRunning ? _provider.Keyboard : null);
        }

        void OnDisable()
        {
            _round.RoundStarted -= HandleRoundStarted;
            _round.RoundEnded -= HandleRoundEnded;
            _provider.KeyboardChanged -= HandleKeyboardChanged;
            _scoreCombo.SetTrackingEnabled(false);
            _scoreCombo.SetKeyboard(null);
        }

        /// <summary>
        /// 새 라운드 시작에 맞춰 기존 점수와 콤보를 초기화한다.
        /// 연결된 스코어 컴포넌트의 상태를 초기화하고 타건과 파괴 수집을 시작한다.
        /// </summary>
        private void HandleRoundStarted()
        {
            _scoreCombo.ResetState();
            _scoreCombo.SetTrackingEnabled(true);
        }

        /// <summary>
        /// 라운드 종료에 맞춰 점수 수집과 콤보 타이머를 멈춘다.
        /// 점수와 콤보 값은 스코어 컴포넌트에 유지하고 현재 키보드 연결을 해제한다.
        /// </summary>
        private void HandleRoundEnded()
        {
            _scoreCombo.SetTrackingEnabled(false);
            _scoreCombo.SetKeyboard(null);
        }

        /// <summary>
        /// 공급된 keyboard를 기존 점수와 콤보의 입력 대상으로 연결한다.
        /// 누적 상태를 유지하고 교체된 키보드의 타건과 키캡 파괴를 수집하도록 변경한다.
        /// </summary>
        private void HandleKeyboardChanged(KeyboardInputController keyboard)
        {
            _scoreCombo.SetKeyboard(keyboard);
        }
    }
}
