using UnityEngine;
using UnityEngine.UI;

using KeyboardModeling;

namespace Game.UI
{
    public sealed class FeverHudView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private FeverController _controller;
        private float _refreshRemaining;

        [Header("Vertical Gauge")]
        [SerializeField] private Slider _gauge;
        [SerializeField] private Text _status;
        [SerializeField] private Text _charge;
        [SerializeField] private Text _remaining;
        [SerializeField] private Text _multiplier;
        [SerializeField] private Text _pauseGuide;
        [SerializeField] private Color _chargeColor = new Color(0.25f, 0.95f, 0.72f);
        [SerializeField] private Color _activeColor = new Color(1f, 0.56f, 0.16f);

        void OnEnable()
        {
            // 범용 HUD 프리팹은 피버가 없는 씬에서도 사용한다.
            if (_controller == null)
                _controller = FindFirstObjectByType<FeverController>();
            if (_controller == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _controller.StateChanged += RefreshView;
            RefreshView();
        }

        void OnDisable()
        {
            if (_controller != null)
                _controller.StateChanged -= RefreshView;
        }

        void Update()
        {
            _refreshRemaining -= Time.unscaledDeltaTime;
            if (_refreshRemaining > 0f)
                return;
            _refreshRemaining = 0.05f;
            RefreshView();
        }

        /// <summary>
        /// 피버 상태를 읽어 세로 게이지와 충전량, 남은 초, 현재 배율, 정지 안내를 갱신한다.
        /// 충전 중에는 충전 비율, 피버 중에는 남은 시간 비율을 표시하며 게임 상태는 변경하지 않는다.
        /// </summary>
        private void RefreshView()
        {
            bool active = _controller.IsFeverActive;
            Color accent = active ? _activeColor : _chargeColor;
            _gauge.SetValueWithoutNotify(active ? _controller.Remaining / _controller.Duration : _controller.Charge);
            _gauge.targetGraphic.color = accent;
            _status.text = active ? "FEVER ACTIVE" : "FEVER CHARGE";
            _status.color = accent;
            _charge.text = $"CHARGE\n{_controller.Charge * 100f:0}%";
            _remaining.text = $"TIME LEFT\n{_controller.Remaining:0.0}s";
            _multiplier.text = $"DAMAGE\nx{_controller.DamageMultiplier:0.0}";
            _pauseGuide.text = _controller.CurrentPauseReason switch
            {
                FeverController.PauseReason.KeyboardUnavailable => "PAUSED\nPlace a keyboard\nto resume.",
                FeverController.PauseReason.InputDisabled => "PAUSED\nReturn to smash\nto resume.",
                FeverController.PauseReason.GamePaused => "PAUSED\nResume the game\nto continue.",
                _ => active ? "Keep smashing!" : "+50% per clear\nTwo clears to start."
            };
        }
    }
}
