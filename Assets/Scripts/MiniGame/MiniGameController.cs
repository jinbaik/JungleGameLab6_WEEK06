using System;

using UnityEngine;
using UnityEngine.InputSystem;

using Game.Session;

namespace KeyboardModeling
{
    [DefaultExecutionOrder(-100)]
    public sealed class MiniGameController : MonoBehaviour
    {
        public enum MiniGameKind { Idle, Hacking, Typing }

        private static readonly string[] _words =
        {
            "SPACE", "SHIELD", "SYSTEM", "ACCESS", "RESET", "PATCH",
            "GUARD", "PROXY", "CACHE", "TOKEN", "INPUT", "SIGNAL"
        };
        private static readonly string[] _commands =
        {
            "> firewall --restore", "> isolate suspicious-host", "> revoke session-token",
            "> patch security-layer", "> scan --deep", "> verify checksum"
        };

        [Header("Schedule")]
        [SerializeField] private KeyboardInputController _keyboard;
        [SerializeField, Min(0.1f)] private float _eventInterval = 15f;
        private MiniGameKind _currentGame;
        private float _nextEventRemaining;
        private bool CanProcessInput => isActiveAndEnabled && _keyboard != null && _keyboard.InputEnabled && _keyboard.isActiveAndEnabled;
        public MiniGameKind CurrentGame => _currentGame;
        public float NextEventRemaining => _nextEventRemaining;
        public bool IsSuspended => _currentGame != MiniGameKind.Idle && !CanProcessInput;
        public event Action StateChanged;

        [Header("Hacking")]
        [SerializeField, Range(0.01f, 1f)] private float _progressPerPress = 0.08f;
        [SerializeField, Min(0f)] private float _progressDecayPerSecond = 0.06f;
        private float _hackingProgress;
        private string _commandLog = "";
        public float HackingProgress => _hackingProgress;
        public string CommandLog => _commandLog;

        [Header("Typing")]
        [SerializeField, Min(1)] private int _wordsToClear = 6;
        private int _completedWords;
        private int _wordIndex = -1;
        private string _typedWord = "";
        private string _typingFeedback = "";
        public int CompletedWords => _completedWords;
        public int WordsToClear => _wordsToClear;
        public string TargetWord => _wordIndex < 0 ? "" : _words[_wordIndex];
        public string TypedWord => _typedWord;
        public string TypingFeedback => _typingFeedback;

        [Header("Fever")]
        [Tooltip("피버 강화를 구매하는 세션을 연결합니다. 강화가 없는 개발 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;
        private float _feverDuration = 5f;
        private float _feverDamageMultiplier = 2f;
        private int _feverClears;
        private float _feverRemaining;
        public bool IsFeverActive => _feverRemaining > 0f;
        public float FeverRemaining => _feverRemaining;
        public float FeverDuration => _feverDuration;
        public float FeverCharge => _feverClears * 0.5f;
        public float DamageMultiplier => IsFeverActive ? _feverDamageMultiplier : 1f;

        void Awake()
        {
            _nextEventRemaining = _eventInterval;
        }

        void OnEnable()
        {
            if (_keyboard != null)
            {
                _keyboard.KeyPressed += HandleKeyPressed;
            }
        }

        void OnDisable()
        {
            if (_keyboard != null)
            {
                _keyboard.KeyPressed -= HandleKeyPressed;
            }
        }

        /// <summary>
        /// keyboard1을 미니게임 입력 대상으로 교체한다.
        /// 이전 KeyPressed 구독을 해제하고 활성 상태이면 새 대상에 연결하며,
        /// 진행도와 시간을 유지한 채 StateChanged로 입력 대상 변경을 알린다.
        /// </summary>
        public void SetKeyBoard(KeyboardInputController keyboard1)
        {
            if (_keyboard == keyboard1)
                return;

            if (isActiveAndEnabled && _keyboard != null)
                _keyboard.KeyPressed -= HandleKeyPressed;

            _keyboard = keyboard1;

            if (isActiveAndEnabled && _keyboard != null)
                _keyboard.KeyPressed += HandleKeyPressed;

            StateChanged?.Invoke();
        }

        void Update()
        {
            if (!CanProcessInput)
            {
                return;
            }

            if (_feverRemaining > 0f)
            {
                _feverRemaining = Mathf.Max(0f, _feverRemaining - Time.deltaTime);
                if (_feverRemaining == 0f)
                {
                    _feverClears = 0;
                    _nextEventRemaining = _eventInterval;
                    StateChanged?.Invoke();
                }
                return;
            }

            if (_currentGame == MiniGameKind.Idle)
            {
                _nextEventRemaining = Mathf.Max(0f, _nextEventRemaining - Time.deltaTime);

                if (_nextEventRemaining == 0f)
                {
                    BeginMiniGame();
                }
            }
            else if (_currentGame == MiniGameKind.Hacking)
            {
                _hackingProgress = Mathf.Max(0f, _hackingProgress - _progressDecayPerSecond * Time.deltaTime);
            }
        }

        /// <summary>
        /// 대기 시간이 끝나면 두 미니게임 중 하나를 무작위로 시작한다.
        /// 현재 상태를 확인하고 진행도, 단어 및 명령 로그를 초기화하여 실행 상태를 변경한다.
        /// </summary>
        private void BeginMiniGame()
        {
            if (_currentGame != MiniGameKind.Idle || IsFeverActive)
                return;
            _currentGame = UnityEngine.Random.Range(0, 2) == 0 ? MiniGameKind.Hacking : MiniGameKind.Typing;
            _hackingProgress = 0f;
            _commandLog = "> intrusion detected\n> awaiting countermeasures...";
            _completedWords = 0;
            _typedWord = "";
            _typingFeedback = "Complete the word to submit automatically.";
            SelectNextWord();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 키보드의 새 눌림을 현재 미니게임에 전달한다.
        /// 입력이 가능한 상태에서 key가 Escape가 아니면 해킹 진행도를 높이거나 단어 입력과 삭제 상태를 변경한다.
        /// </summary>
        private void HandleKeyPressed(Key key)
        {
            if (!CanProcessInput || key == Key.Escape)
                return;
            if (_currentGame == MiniGameKind.Hacking)
            {
                _hackingProgress = Mathf.Min(1f, _hackingProgress + _progressPerPress);
                string[] lines = _commandLog.Split('\n');
                _commandLog = (lines.Length >= 4 ? string.Join("\n", lines, 1, lines.Length - 1) : _commandLog)
                    + "\n" + _commands[UnityEngine.Random.Range(0, _commands.Length)];
                if (_hackingProgress >= 1f)
                    CompleteMiniGame();
            }
            else if (_currentGame == MiniGameKind.Typing)
            {
                ProcessTypingKey(key);
            }
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 물리 영문 키와 Backspace 입력을 단어 버퍼에 반영한다.
        /// key로 완성한 문자열을 목표와 비교하여 정답 수를 늘리거나 목표만 교체하고 버퍼를 비운다.
        /// </summary>
        private void ProcessTypingKey(Key key)
        {
            if (key == Key.Backspace)
            {
                if (_typedWord.Length > 0)
                    _typedWord = _typedWord.Substring(0, _typedWord.Length - 1);
                return;
            }
            if (key < Key.A || key > Key.Z)
                return;
            _typedWord += (char)('A' + (key - Key.A));
            if (_typedWord.Length < TargetWord.Length)
                return;
            bool correct = _typedWord == TargetWord;
            if (correct)
                _completedWords++;
            _typingFeedback = correct ? "MATCH / word accepted" : "MISMATCH / new target, progress retained";
            _typedWord = "";
            if (_completedWords >= _wordsToClear)
                CompleteMiniGame();
            else
                SelectNextWord();
        }

        /// <summary>
        /// 직전 목표와 다른 5~6글자 영문 단어를 선택한다.
        /// 기존 단어 인덱스와 단어 목록을 사용하여 다음 목표 인덱스를 변경한다.
        /// </summary>
        private void SelectNextWord()
        {
            if (_wordIndex < 0)
            {
                _wordIndex = UnityEngine.Random.Range(0, _words.Length);
                return;
            }
            int next = UnityEngine.Random.Range(0, _words.Length - 1);
            _wordIndex = next >= _wordIndex ? next + 1 : next;
        }

        /// <summary>
        /// 실행 중인 미니게임을 클리어하고 다음 이벤트 대기 및 피버 보상을 적용한다.
        /// 클리어 횟수로 게이지를 50% 올리고 두 번째 클리어 시 세션의 강화 값을 확정하여 피버를 시작한다.
        /// </summary>
        private void CompleteMiniGame()
        {
            _currentGame = MiniGameKind.Idle;
            _nextEventRemaining = _eventInterval;
            _feverClears = Mathf.Min(2, _feverClears + 1);
            if (_feverClears == 2 && !IsFeverActive)
            {
                _feverDuration = _gameSession != null ? _gameSession.Upgrades.GetFeverDuration() : 5f;
                _feverDamageMultiplier = _gameSession != null ? _gameSession.Upgrades.GetFeverDamageMultiplier() : 2f;
                _feverRemaining = _feverDuration;
            }
        }

        /// <summary>
        /// 현재 미니게임이 실행 중이면 클리어 보상 없이 종료한다.
        /// 진행도와 입력 내용을 초기화하고 다음 이벤트 대기 시간을 재설정하며,
        /// 기존 피버 상태를 유지한 채 StateChanged로 변경을 알린다.
        /// </summary>
        public void CancelMiniGame()
        {
            if (_currentGame == MiniGameKind.Idle)
            {
                return;
            }

            _currentGame = MiniGameKind.Idle;
            _nextEventRemaining = _eventInterval;

            _hackingProgress = 0f;
            _commandLog = "";

            _completedWords = 0;
            _wordIndex = -1;
            _typedWord = "";
            _typingFeedback = "";

            StateChanged?.Invoke();
        }
    }
}
