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

        [Header("Fever")]
        [SerializeField] private Slider _feverFill;
        [SerializeField] private Text _feverLabel;

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
        /// 미니게임과 피버 상태를 모니터 및 HUD에 표시한다.
        /// controller의 현재 게임, 입력, 진행도, 남은 시간을 읽어 패널, 텍스트와 게이지를 변경한다.
        /// </summary>
        private void RefreshView()
        {
            MiniGameController.MiniGameKind game = _controller.CurrentGame;
            bool fever = _controller.IsFeverActive;
            Color accent = fever ? new Color(1f, 0.56f, 0.16f) : new Color(0.25f, 0.95f, 0.72f);
            float feverValue = fever ? _controller.FeverRemaining / _controller.FeverDuration : _controller.FeverCharge;

            RefreshIdlePanel(game);
            RefreshHackingPanel(game);
            RefreshTypingPanel(game);
            RefreshStatus(game);
            RefreshIdleTitle(fever, accent);
            RefreshIdleDetails(fever);
            RefreshHackingFill();
            RefreshHackingPercent();
            RefreshCommandLog();
            RefreshWordCount();
            RefreshTargetWord();
            RefreshTypedWord();
            RefreshTypingFeedback();
            RefreshFeverFill(feverValue, accent);
            RefreshFeverLabel(fever);
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
        /// game을 사용하여 _status에 대기 또는 실행 상태를 표시한다.
        /// </summary>
        private void RefreshStatus(MiniGameController.MiniGameKind game)
        {
            _status.text = game == MiniGameController.MiniGameKind.Idle ? "SYSTEM ONLINE / STANDBY" : "EVENT ACTIVE";
        }

        /// <summary>
        /// 대기 화면의 제목과 색상을 갱신한다.
        /// fever와 accent를 사용하여 _idleTitle에 시스템 또는 피버 상태를 표시한다.
        /// </summary>
        private void RefreshIdleTitle(bool fever, Color accent)
        {
            _idleTitle.text = fever ? "FEVER ONLINE" : "SYSTEM SECURE";
            _idleTitle.color = accent;
        }

        /// <summary>
        /// 대기 화면의 상세 안내를 갱신한다.
        /// fever와 controller의 남은 시간을 사용하여 _idleDetails에 피버 또는 다음 이벤트 시간을 표시한다.
        /// </summary>
        private void RefreshIdleDetails(bool fever)
        {
            _idleDetails.text = fever
                ? $"SMASH DAMAGE x2\n{_controller.FeverRemaining:0.0}s REMAINING"
                : $"Next event in {Mathf.CeilToInt(_controller.NextEventRemaining):00}s\nClear two events to activate FEVER.";
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

        /// <summary>
        /// 모니터의 피버 Slider 값과 색상을 갱신한다.
        /// feverValue와 accent를 사용하여 충전 비율 또는 남은 시간 비율을 _feverFill에 표시한다.
        /// </summary>
        private void RefreshFeverFill(float feverValue, Color accent)
        {
            _feverFill.SetValueWithoutNotify(feverValue);
            _feverFill.targetGraphic.color = accent;
        }

        /// <summary>
        /// 모니터의 피버 상태 문구를 갱신한다.
        /// fever와 controller의 충전량 및 남은 시간을 사용하여 _feverLabel에 상태를 표시한다.
        /// </summary>
        private void RefreshFeverLabel(bool fever)
        {
            _feverLabel.text = fever ? $"FEVER / {_controller.FeverRemaining:0.0}s / DAMAGE x{_controller.DamageMultiplier}" : $"FEVER CHARGE / {_controller.FeverCharge * 100f:0}%";
        }
    }
}
