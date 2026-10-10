using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace KeyboardModeling
{
    public sealed class MiniGameScreenView : MonoBehaviour
    {
        private const float TARGET_PULSE_SCALE = 1.3f;

        [Header("State")]
        [SerializeField] private MiniGameController _controller;
        private float _refreshRemaining;

        [Header("Screen Panels")]
        [SerializeField] private GameObject _idlePanel;
        [SerializeField] private GameObject _hackingPanel;
        [FormerlySerializedAs("_typingPanel")]
        [SerializeField] private GameObject _keyMashPanel;
        [SerializeField] private Text _status;
        [SerializeField] private Text _idleTitle;
        [SerializeField] private Text _idleDetails;

        [Header("Hacking")]
        [SerializeField] private Image _hackingFill;
        [SerializeField] private Text _hackingPercent;
        [SerializeField] private Text _commandLog;

        [Header("Target Key Mash")]
        [FormerlySerializedAs("_targetWord")]
        [SerializeField] private Text _targetKey;
        [SerializeField] private Image _keyMashFill;
        [SerializeField] private Text _keyMashPercent;
        [SerializeField, Min(0.01f)] private float _pulseDuration = 0.16f;
        private Vector3 _targetKeyRestScale;
        private float _pulseRemaining;

        void Awake()
        {
            _targetKeyRestScale = _targetKey.rectTransform.localScale;
        }

        void OnEnable()
        {
            _controller.StateChanged += RefreshView;
            _controller.TargetKeyPressed += PulseTargetKey;
            RefreshView();
        }

        void OnDisable()
        {
            _controller.StateChanged -= RefreshView;
            _controller.TargetKeyPressed -= PulseTargetKey;
            ResetTargetPulse();
        }

        void Update()
        {
            UpdateTargetPulse();
            _refreshRemaining -= Time.deltaTime;
            if (_refreshRemaining <= 0f)
            {
                _refreshRemaining = 0.1f;
                RefreshView();
            }
        }

        /// <summary>
        /// 미니게임 상태를 모니터에 표시한다.
        /// controller의 현재 게임, 입력, 진행도, 남은 시간을 읽어 패널, 텍스트와 게이지를 변경한다.
        /// </summary>
        private void RefreshView()
        {
            MiniGameController.MiniGameKind game = _controller.CurrentGame;

            RefreshIdlePanel(game);
            RefreshHackingPanel(game);
            RefreshKeyMashPanel(game);
            RefreshStatus(game);
            RefreshIdleTitle();
            RefreshIdleDetails();
            RefreshHackingFill();
            RefreshHackingPercent();
            RefreshCommandLog();
            RefreshKeyMashProgress();
            if (game != MiniGameController.MiniGameKind.KeyMash)
                ResetTargetPulse();
        }

        /// <summary>
        /// 대기 패널의 표시 여부를 갱신한다.
        /// game을 사용하여 대기 상태에서만 _idlePanel을 활성화한다.
        /// </summary>
        private void RefreshIdlePanel(MiniGameController.MiniGameKind game)
        {
            _idlePanel.SetActive(game == MiniGameController.MiniGameKind.Idle);
        }

        /// <summary>
        /// 해킹 패널의 표시 여부를 갱신한다.
        /// game을 사용하여 해킹 상태에서만 _hackingPanel을 활성화한다.
        /// </summary>
        private void RefreshHackingPanel(MiniGameController.MiniGameKind game)
        {
            _hackingPanel.SetActive(game == MiniGameController.MiniGameKind.Hacking);
        }

        /// <summary>
        /// 목표 키 연타 패널의 표시 여부를 갱신한다.
        /// game을 사용하여 연타 상태에서만 _keyMashPanel을 활성화한다.
        /// </summary>
        private void RefreshKeyMashPanel(MiniGameController.MiniGameKind game)
        {
            _keyMashPanel.SetActive(game == MiniGameController.MiniGameKind.KeyMash);
        }

        /// <summary>
        /// 현재 이벤트 상태 문구를 갱신한다.
        /// game과 controller의 입력 중단 상태를 사용하여 _status에 대기, 실행 또는 재개 안내를 표시한다.
        /// </summary>
        private void RefreshStatus(MiniGameController.MiniGameKind game)
        {
            _status.text = _controller.IsSuspended
                ? _controller.InputAvailable
                    ? "EVENT PAUSED / PROGRESS RETAINED"
                    : "EVENT PAUSED / PLACE A KEYBOARD TO RESUME"
                : game == MiniGameController.MiniGameKind.Idle ? "SYSTEM ONLINE / STANDBY" : "EVENT ACTIVE";
        }

        /// <summary>
        /// 대기 화면의 제목과 색상을 갱신한다.
        /// 시스템 대기 문구와 기본 강조색을 _idleTitle에 표시한다.
        /// </summary>
        private void RefreshIdleTitle()
        {
            _idleTitle.text = "SYSTEM SECURE";
            _idleTitle.color = new Color(0.25f, 0.95f, 0.72f);
        }

        /// <summary>
        /// 대기 화면의 상세 안내를 갱신한다.
        /// controller의 다음 이벤트 시간과 타이머 정지 여부를 읽어 _idleDetails에 대기 안내를 표시한다.
        /// </summary>
        private void RefreshIdleDetails()
        {
            _idleDetails.text = _controller.IsEventTimerPaused
                ? "Next event waiting.\nSecurity system standing by."
                : $"Next event in {Mathf.CeilToInt(_controller.NextEventRemaining):00}s\nComplete events to restore security.";
        }

        /// <summary>
        /// 해킹 진행도 막대를 갱신한다.
        /// controller의 HackingProgress를 사용하여 _hackingFill의 채움량을 변경한다.
        /// </summary>
        private void RefreshHackingFill()
        {
            _hackingFill.fillAmount = _controller.HackingProgress;
        }

        /// <summary>
        /// 해킹 진행률 문구를 갱신한다.
        /// controller의 HackingProgress를 백분율로 변환하여 _hackingPercent에 표시한다.
        /// </summary>
        private void RefreshHackingPercent()
        {
            _hackingPercent.text = $"COUNTERMEASURE / {_controller.HackingProgress * 100f:0}%";
        }

        /// <summary>
        /// 해킹 명령 로그를 갱신한다.
        /// controller의 CommandLog를 사용하여 _commandLog의 내용을 변경한다.
        /// </summary>
        private void RefreshCommandLog()
        {
            _commandLog.text = _controller.CommandLog;
        }

        /// <summary>
        /// 목표 물리 키와 연타 진행도를 화면에 표시한다.
        /// controller의 목표 문구와 0부터 1까지의 진행도를 읽어 텍스트와 게이지를 변경한다.
        /// </summary>
        private void RefreshKeyMashProgress()
        {
            _targetKey.text = _controller.TargetKeyLabel;
            _keyMashFill.fillAmount = _controller.KeyMashProgress;
            _keyMashPercent.text = $"CHARGE / {_controller.KeyMashProgress * 100f:0}%";
        }

        /// <summary>
        /// 정답 키 입력 알림으로 목표 텍스트를 기본 크기의 130%로 확대한다.
        /// 펄스 시간을 초기화하여 연속 입력에서도 확대 비율이 누적되지 않도록 한다.
        /// </summary>
        private void PulseTargetKey()
        {
            _pulseRemaining = _pulseDuration;
            _targetKey.rectTransform.localScale = _targetKeyRestScale * TARGET_PULSE_SCALE;
        }

        /// <summary>
        /// Time.deltaTime과 남은 펄스 시간으로 목표 텍스트를 기본 크기로 복원한다.
        /// 미니게임 정지 중에는 연출 시간을 유지하고 연타 상태에서만 크기를 변경한다.
        /// </summary>
        private void UpdateTargetPulse()
        {
            if (_controller.CurrentGame != MiniGameController.MiniGameKind.KeyMash || _controller.IsSuspended || _pulseRemaining <= 0f)
                return;
            _pulseRemaining = Mathf.Max(0f, _pulseRemaining - Time.deltaTime);
            float pulse = Mathf.SmoothStep(0f, 1f, _pulseRemaining / _pulseDuration);
            _targetKey.rectTransform.localScale = _targetKeyRestScale * Mathf.Lerp(1f, TARGET_PULSE_SCALE, pulse);
        }

        /// <summary>
        /// 남은 펄스 시간을 비우고 목표 텍스트를 기본 크기로 복원한다.
        /// 게임 종료나 화면 비활성화 후에도 확대 상태가 남지 않도록 표시 상태만 변경한다.
        /// </summary>
        private void ResetTargetPulse()
        {
            _pulseRemaining = 0f;
            _targetKey.rectTransform.localScale = _targetKeyRestScale;
        }
    }
}
