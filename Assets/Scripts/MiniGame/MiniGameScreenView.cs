using UnityEngine;
using UnityEngine.UI;

namespace KeyboardModeling
{
    public sealed class MiniGameScreenView : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private MiniGameController _controller;
        private float _refreshRemaining;

        [Header("Screen Panels")]
        [SerializeField] private GameObject _idlePanel;
        [SerializeField] private GameObject _hackingPanel;
        [SerializeField] private GameObject _typingPanel;
        [SerializeField] private Text _status;
        [SerializeField] private Text _idleTitle;
        [SerializeField] private Text _idleDetails;

        [Header("Hacking")]
        [SerializeField] private Image _hackingFill;
        [SerializeField] private Text _hackingPercent;
        [SerializeField] private Text _commandLog;

        [Header("Typing")]
        [SerializeField] private Text _wordCount;
        [SerializeField] private Text _targetWord;
        [SerializeField] private Text _typedWord;
        [SerializeField] private Text _typingFeedback;

        void OnEnable()
        {
            _controller.StateChanged += RefreshView;
            RefreshView();
        }

        void OnDisable()
        {
            _controller.StateChanged -= RefreshView;
        }

        void Update()
        {
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
            RefreshTypingPanel(game);
            RefreshStatus(game);
            RefreshIdleTitle();
            RefreshIdleDetails();
            RefreshHackingFill();
            RefreshHackingPercent();
            RefreshCommandLog();
            RefreshWordCount();
            RefreshTargetWord();
            RefreshTypedWord();
            RefreshTypingFeedback();
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
        /// 단어 입력 패널의 표시 여부를 갱신한다.
        /// game을 사용하여 단어 입력 상태에서만 _typingPanel을 활성화한다.
        /// </summary>
        private void RefreshTypingPanel(MiniGameController.MiniGameKind game)
        {
            _typingPanel.SetActive(game == MiniGameController.MiniGameKind.Typing);
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
        /// 단어 정답 수를 갱신한다.
        /// controller의 정답 수와 목표 개수를 사용하여 _wordCount에 진행 상황을 표시한다.
        /// </summary>
        private void RefreshWordCount()
        {
            _wordCount.text = $"WORDS ACCEPTED / {_controller.CompletedWords:00} / {_controller.WordsToClear:00}";
        }

        /// <summary>
        /// 목표 단어를 갱신한다.
        /// controller의 TargetWord를 사용하여 _targetWord의 내용을 변경한다.
        /// </summary>
        private void RefreshTargetWord()
        {
            _targetWord.text = _controller.TargetWord;
        }

        /// <summary>
        /// 현재 입력 중인 단어를 갱신한다.
        /// controller의 입력과 목표 길이를 사용하여 _typedWord에 입력 내용과 빈 칸을 표시한다.
        /// </summary>
        private void RefreshTypedWord()
        {
            _typedWord.text = _controller.TypedWord.PadRight(_controller.TargetWord.Length, '_');
        }

        /// <summary>
        /// 단어 입력 결과 안내를 갱신한다.
        /// controller의 TypingFeedback을 사용하여 _typingFeedback의 내용을 변경한다.
        /// </summary>
        private void RefreshTypingFeedback()
        {
            _typingFeedback.text = _controller.TypingFeedback;
        }
    }
}
