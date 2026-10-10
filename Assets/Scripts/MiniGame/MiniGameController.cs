using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling
{
    [DefaultExecutionOrder(-100)]
    public sealed class MiniGameController : MonoBehaviour
    {
        public enum MiniGameKind { Idle = 0, Hacking = 1, KeyMash = 2 }
        private static readonly string[] _commands =
        {
            "> firewall --restore", "> isolate suspicious-host", "> revoke session-token",
            "> patch security-layer", "> scan --deep", "> verify checksum"
        };

        [Header("Schedule")]
        [SerializeField] private KeyboardInputController _keyboard;
        [SerializeField, Min(0.1f)] private float _eventInterval = 15f;
        [SerializeField] private FeverController _feverController;
        private MiniGameKind _currentGame;
        private float _nextEventRemaining;
        private bool CanProcessInput => isActiveAndEnabled && _keyboard != null && _keyboard.InputEnabled && _keyboard.isActiveAndEnabled;
        private bool IsProgressPaused => !CanProcessInput || Time.timeScale == 0f || (_feverController != null && _feverController.IsFeverActive);
        public MiniGameKind CurrentGame => _currentGame;
        public float NextEventRemaining => _nextEventRemaining;
        public bool IsSuspended => _currentGame != MiniGameKind.Idle && IsProgressPaused;
        public bool InputAvailable => CanProcessInput;
        public bool HasKeyboard => _keyboard != null;
        public bool IsEventTimerPaused => IsProgressPaused;
        public event Action StateChanged;
        public event Action Completed;

        [Header("Hacking")]
        [SerializeField, Range(0.01f, 1f)] private float _progressPerPress = 0.08f;
        [SerializeField, Min(0f)] private float _progressDecayPerSecond = 0.06f;
        private float _hackingProgress;
        private string _commandLog = "";
        public float HackingProgress => _hackingProgress;
        public string CommandLog => _commandLog;

        [Header("Target Key Mash")]
        [SerializeField, Range(0.01f, 1f)] private float _keyMashProgressPerPress = 0.08f;
        [SerializeField, Min(0f)] private float _keyMashDecayPerSecond = 0.06f;
        [SerializeField, Min(0f)] private float _keyMashDecayDelay = 0.4f;
        private Key _targetKey;
        private float _keyMashProgress;
        private float _keyMashDecayDelayRemaining;
        public Key TargetKey => _targetKey;
        public float KeyMashProgress => _keyMashProgress;
        public string TargetKeyLabel => _targetKey switch
        {
            Key.None => "",
            Key.Space => "SPACE",
            Key.Enter => "ENTER",
            Key.Digit0 => "0",
            Key.Digit1 => "1",
            Key.Digit2 => "2",
            Key.Digit3 => "3",
            Key.Digit4 => "4",
            Key.Digit5 => "5",
            Key.Digit6 => "6",
            Key.Digit7 => "7",
            Key.Digit8 => "8",
            Key.Digit9 => "9",
            Key.Backquote => "`  ~",
            Key.Minus => "-  _",
            Key.Equals => "=  +",
            Key.LeftBracket => "[  {",
            Key.RightBracket => "]  }",
            Key.Backslash => "\\  |",
            Key.Semicolon => ";  :",
            Key.Quote => "'  \"",
            Key.Comma => ",  <",
            Key.Period => ".  >",
            Key.Slash => "/  ?",
            _ => _targetKey.ToString().ToUpperInvariant()
        };
        public event Action TargetKeyPressed;

        void Awake()
        {
            _nextEventRemaining = _eventInterval;
        }

        void OnEnable()
        {
            if (_feverController != null)
                _feverController.Ended += HandleFeverEnded;
            if (_keyboard != null)
            {
                _keyboard.KeyPressed += HandleKeyPressed;
            }
        }

        void OnDisable()
        {
            if (_feverController != null)
                _feverController.Ended -= HandleFeverEnded;
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
            if (IsProgressPaused)
            {
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
            else if (_currentGame == MiniGameKind.KeyMash)
            {
                UpdateKeyMashProgress(Time.deltaTime);
            }
        }

        /// <summary>
        /// 대기 시간이 끝나면 두 미니게임 중 하나를 무작위로 시작한다.
        /// 현재 입력 상태를 확인하고 진행도, 목표 키 및 명령 로그를 초기화하여 실행 상태를 변경한다.
        /// </summary>
        private void BeginMiniGame()
        {
            if (_currentGame != MiniGameKind.Idle || IsProgressPaused)
                return;
            _currentGame = UnityEngine.Random.Range(0, 2) == 0 ? MiniGameKind.Hacking : MiniGameKind.KeyMash;
            _hackingProgress = 0f;
            _commandLog = "> intrusion detected\n> awaiting countermeasures...";
            _keyMashProgress = 0f;
            _keyMashDecayDelayRemaining = 0f;
            _targetKey = Key.None;
            if (_currentGame == MiniGameKind.KeyMash)
                SelectTargetKey();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 키보드의 새 눌림을 현재 미니게임에 전달한다.
        /// 입력이 가능한 상태에서 key가 Escape가 아니면 해킹 또는 목표 키 연타 진행도를 높인다.
        /// </summary>
        private void HandleKeyPressed(Key key)
        {
            if (IsProgressPaused || key == Key.Escape)
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
            else if (_currentGame == MiniGameKind.KeyMash)
            {
                if (key != _targetKey)
                    return;
                _keyMashProgress = Mathf.Min(1f, _keyMashProgress + _keyMashProgressPerPress);
                _keyMashDecayDelayRemaining = _keyMashDecayDelay;
                TargetKeyPressed?.Invoke();
                if (_keyMashProgress >= 1f)
                    CompleteMiniGame();
            }
            StateChanged?.Invoke();
        }

        /// <summary>
        /// deltaTime을 사용하여 정답 입력 이후 감소 대기 시간과 연타 진행도를 갱신한다.
        /// 대기 시간이 끝난 구간에만 초당 감소량을 적용하고 진행도를 0 이상으로 유지한다.
        /// </summary>
        private void UpdateKeyMashProgress(float deltaTime)
        {
            float decayTime = Mathf.Max(0f, deltaTime - _keyMashDecayDelayRemaining);
            _keyMashDecayDelayRemaining = Mathf.Max(0f, _keyMashDecayDelayRemaining - deltaTime);
            _keyMashProgress = Mathf.Max(0f, _keyMashProgress - _keyMashDecayPerSecond * decayTime);
        }

        /// <summary>
        /// 현재 키보드에 바인딩된 영문, 숫자, 기호, Space 및 Enter 중 목표 키 하나를 선택한다.
        /// 키캡 체력과 무관하게 선택하며 게임이 끝날 때까지 _targetKey를 유지한다.
        /// </summary>
        private void SelectTargetKey()
        {
            List<Key> candidates = new List<Key>();
            foreach (Key key in _keyboard.GetKeycapLayout().Keys)
            {
                if (IsTargetKeyCandidate(key))
                    candidates.Add(key);
            }
            if (candidates.Count == 0)
                throw new InvalidOperationException("Key mash requires a bound letter, digit, symbol, Space or Enter key.");
            _targetKey = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// key가 목표 키 풀에 포함되는 물리 키인지 검사하여 결과를 반환한다.
        /// 영문, 상단 숫자, 기호, Space와 Enter를 허용하고 Tab 및 Escape는 제외한다.
        /// </summary>
        private static bool IsTargetKeyCandidate(Key key)
        {
            return (key >= Key.A && key <= Key.Z) || (key >= Key.Digit1 && key <= Key.Digit0)
                || key == Key.Space || key == Key.Enter || key == Key.Backquote
                || key == Key.Minus || key == Key.Equals || key == Key.LeftBracket || key == Key.RightBracket
                || key == Key.Backslash || key == Key.Semicolon || key == Key.Quote
                || key == Key.Comma || key == Key.Period || key == Key.Slash;
        }

        /// <summary>
        /// 실행 중인 미니게임을 클리어하고 다음 이벤트 대기 시간을 초기화한다.
        /// 현재 게임을 대기로 변경하고 Completed를 즉시 전달하여 같은 입력의 공격 전에 완료 보상을 적용한다.
        /// </summary>
        private void CompleteMiniGame()
        {
            _currentGame = MiniGameKind.Idle;
            _nextEventRemaining = _eventInterval;
            Completed?.Invoke();
        }

        /// <summary>
        /// 피버 종료 알림을 받아 다음 미니게임의 대기 시간을 초기화한다.
        /// 설정된 이벤트 간격을 남은 시간에 저장하고 StateChanged로 화면 갱신을 요청한다.
        /// </summary>
        private void HandleFeverEnded()
        {
            _nextEventRemaining = _eventInterval;
            StateChanged?.Invoke();
        }

        /// <summary>
        /// 현재 미니게임이 실행 중이면 클리어 보상 없이 종료한다.
        /// 진행도와 목표 키를 초기화하고 다음 이벤트 대기 시간을 재설정하며,
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

            _targetKey = Key.None;
            _keyMashProgress = 0f;
            _keyMashDecayDelayRemaining = 0f;

            StateChanged?.Invoke();
        }
    }
}
