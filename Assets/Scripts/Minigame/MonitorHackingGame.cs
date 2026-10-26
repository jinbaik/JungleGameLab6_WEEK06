using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class MonitorHackingGame : MonitorMiniGame
    {
        private const int MAX_LOG_LINES = 12;

        private static readonly Key[] BYPASS_KEYS = new[] { Key.Space, Key.Enter, Key.Tab };
        private static readonly string[] STAGE_NAMES = new[] { "FIREWALL PORT 80", "SECURITY GATEWAY", "CORE MAINFRAME" };

        private enum Phase
        {
            Ready,
            BruteForce,
            Bypass,
            Success
        }

        [Header("UI References")]
        [SerializeField] private Text _titleLabel;
        [SerializeField] private Text _stageLabel;
        [SerializeField] private Text _progressBarLabel;
        [SerializeField] private Text _terminalLogLabel;
        [SerializeField] private Text _actionPromptLabel;
        [SerializeField] private Text _statsLabel;

        [Header("Enhanced UI")]
        [SerializeField] private Image _progressBarFill;
        [SerializeField] private Text _progressPercentText;
        [SerializeField] private Image _promptBoxBg;
        [SerializeField] private Text _targetHostLabel;
        [SerializeField] private Text _stageBadgeLabel;

        [Header("State")]
        private Phase _phase;
        private int _currentStage;
        private int _totalStages = 3;
        private int _requiredKeystrokes = 50;
        private int _stageKeystrokes;
        private int _totalKeystrokes;
        private float _elapsedTime;
        private float _promptBlinkTimer;
        private bool _promptBlinkVisible = true;
        private Key _currentBypassKey;
        private readonly List<string> _logLines = new List<string>();
        private readonly StringBuilder _stringBuilder = new StringBuilder();

        /// <summary>
        /// definition이 유효한 해킹 게임 정의인지 확인하여 실행 가능 여부를 반환한다.
        /// definition을 검사하며 내부 상태는 변경하지 않는다.
        /// </summary>
        public override bool CanRun(MonitorMiniGameDefinition definition)
        {
            return definition is MonitorHackingGameDefinition;
        }

        /// <summary>
        /// 연결된 SO 정의를 바탕으로 해킹 게임 진행 상태를 초기화하고 첫 스테이지를 시작한다.
        /// Definition을 사용하며 스테이지와 타이머 및 로그 목록을 갱신한다.
        /// </summary>
        protected override void OnStarted()
        {
            if (Definition is MonitorHackingGameDefinition hackingDef)
            {
                _totalStages = Mathf.Max(1, hackingDef.TotalStages);
                _requiredKeystrokes = Mathf.Max(10, hackingDef.KeystrokesPerStage);
            }

            if (_statsLabel != null)
                _statsLabel.gameObject.SetActive(false);

            ResetGame();
        }

        /// <summary>
        /// 게임이 중지될 때 화면 텍스트와 타이머 상태를 정리한다.
        /// 입력값과 반환값은 없으며 UI 라벨들을 대기 상태 문자열로 갱신한다.
        /// </summary>
        protected override void OnStopped()
        {
            _phase = Phase.Ready;
            if (_actionPromptLabel != null)
                _actionPromptLabel.text = "SYSTEM STANDBY";
        }

        /// <summary>
        /// 실행 중인 해킹 게임의 진행 시간과 프롬프트 깜빡임 타이머를 누적한다.
        /// deltaTime을 사용하여 _elapsedTime과 _promptBlinkTimer를 갱신한다.
        /// </summary>
        public override void Tick(float deltaTime)
        {
            if (!IsRunning) return;

            if (_phase == Phase.BruteForce || _phase == Phase.Bypass)
            {
                _elapsedTime += deltaTime;
                _promptBlinkTimer += deltaTime;
                if (_promptBlinkTimer >= 0.35f)
                {
                    _promptBlinkTimer = 0f;
                    _promptBlinkVisible = !_promptBlinkVisible;
                    RefreshPrompt();
                }
                RefreshStats();
            }
        }

        /// <summary>
        /// 입력된 키에 따라 브루트포스 연타, 바이패스 인증, 또는 재시작 처리를 수행한다.
        /// key를 사용하며 게임 진행 단계와 누적 타수 상태를 변경한다.
        /// </summary>
        public override void HandleKeyPressed(Key key)
        {
            if (!IsRunning) return;

            if (_phase == Phase.Success)
            {
                if (key == Key.Space || key == Key.Enter)
                    ResetGame();
                return;
            }

            if (_phase == Phase.BruteForce)
            {
                ProcessBruteForceKeystroke(key);
                return;
            }

            if (_phase == Phase.Bypass)
            {
                ProcessBypassKeystroke(key);
            }
        }

        /// <summary>
        /// 브루트포스 단계에서 키 입력을 처리하고 목표 타수에 도달하면 바이패스 단계로 전환한다.
        /// key를 사용하며 _stageKeystrokes, _totalKeystrokes 및 로그를 갱신한다.
        /// </summary>
        private void ProcessBruteForceKeystroke(Key key)
        {
            _stageKeystrokes++;
            _totalKeystrokes++;

            // 실제 터미널 느낌의 헥사코드와 네트워크 명령어로 구성된 해킹 로그를 추가한다.
            AddLog(GenerateHackerLog(key));

            if (_stageKeystrokes >= _requiredKeystrokes)
            {
                _phase = Phase.Bypass;
                _currentBypassKey = BYPASS_KEYS[_currentStage % BYPASS_KEYS.Length];
                _promptBlinkTimer = 0f;
                _promptBlinkVisible = true;
                AddLog("<color=#FF3366>----------------------------------------------------------------</color>");
                AddLog("<color=#FF3366>>> [CRITICAL] FIREWALL BARRIER LOCKED! OVERRIDE REQUIRED <<</color>");
                AddLog($"<color=#FFD700>>> HIT EMERGENCY BYPASS KEY: [{_currentBypassKey}] <<</color>");
            }

            RefreshScreen();
        }

        /// <summary>
        /// 입력된 키와 무작위 메모리 주소 및 시스템 명령어를 조합하여 사이버 해킹 로그를 생성한다.
        /// key를 사용하며 RichText 태그가 포함된 터미널 로그 문자열을 반환한다.
        /// </summary>
        private string GenerateHackerLog(Key key)
        {
            int templateIndex = UnityEngine.Random.Range(0, 12);
            int addr1 = UnityEngine.Random.Range(0x1000, 0xFFFF);
            int addr2 = UnityEngine.Random.Range(0x100000, 0xFFFFFF);
            int octet = UnityEngine.Random.Range(10, 254);
            int pid = UnityEngine.Random.Range(1024, 9999);

            return templateIndex switch
            {
                0 => $"<color=#00FFFF>[0x{addr1:X4}]</color> <color=#39FF14>[OK]</color> inject_payload({key}) -> buffer_overflow at 0x{addr2:X6}",
                1 => $"<color=#FFCC00>[NET]</color> routing through node <color=#00FFFF>10.240.{octet}.{addr1 % 255}</color> ... proxy [CHAINED]",
                2 => $"<color=#FF3366>[EXPLOIT]</color> CVE-2026-SYS_BREACH: kernel hook({key}) <color=#39FF14>[OVERRIDDEN]</color>",
                3 => $"<color=#00FFFF>[HASH]</color> brute_force({key}) -> SHA-512 round [{addr1}/65535] <color=#39FF14>[CRACKED]</color>",
                4 => $"<color=#39FF14>[ROOT]</color> sudo chmod 777 /sys/kernel/security -> <color=#39FF14>UID: 0 (ROOT)</color>",
                5 => $"<color=#FFCC00>[PORT]</color> syn_flood: 192.168.1.{octet}:<color=#00FFFF>{addr1 % 9000 + 1000}</color> -> ACK response 200",
                6 => $"<color=#00FFFF>[MEM]</color> dumping cryptographic key: <color=#39FF14>{addr1:X4}-{addr2 % 0xFFFF:X4}</color> [DUMPED]",
                7 => $"<color=#FF3366>[BYPASS]</color> disabling IDS watchdog daemon (PID: <color=#00FFFF>{pid}</color>) -> [SIGKILL]",
                8 => $"<color=#39FF14>[SHELL]</color> /bin/bash -i spawned at socket <color=#00FFFF>0x{addr1:X4}</color> [ATTACHED]",
                9 => $"<color=#FFCC00>[PACKET]</color> sniffing TCP stream [{addr1}]: auth token <color=#39FF14>[HIJACKED]</color>",
                10 => $"<color=#00FFFF>[RSA]</color> factoring public modulus (e=65537): private key <color=#39FF14>[RECOVERED]</color>",
                _ => $"<color=#39FF14>[CIPHER]</color> AES-256 IV vector matching: block [{addr1}] <color=#39FF14>[AUTHENTICATED]</color>"
            };
        }

        /// <summary>
        /// 바이패스 단계에서 요구된 특정 키 입력 여부를 검사하고 다음 스테이지 또는 최종 성공으로 전환한다.
        /// key를 _currentBypassKey와 비교하며 _currentStage와 _phase를 갱신한다.
        /// </summary>
        private void ProcessBypassKeystroke(Key key)
        {
            if (key != _currentBypassKey) return;

            _totalKeystrokes++;
            _currentStage++;

            if (_currentStage >= _totalStages)
            {
                _phase = Phase.Success;
                AddLog("<color=#39FF14>================================================================</color>");
                AddLog("<color=#39FF14>>> [ACCESS GRANTED] ROOT AUTHORITY CONFIRMED.</color>");
                AddLog("<color=#00FFFF>>> MAINFRAME CONTROL TAKEN // ALL ENCRYPTED DATA HARVESTED.</color>");
                AddLog("<color=#FFD700>>> PRESS [SPACE] TO RE-INITIALIZE INFILTRATION PROTOCOL.</color>");
            }
            else
            {
                _phase = Phase.BruteForce;
                _stageKeystrokes = 0;
                _promptBlinkTimer = 0f;
                _promptBlinkVisible = true;
                AddLog($"<color=#39FF14>>> [STAGE CLEARED] LAYER {_currentStage} BREACHED. ESCALATING TO LAYER {_currentStage + 1}...</color>");
            }

            RefreshScreen();
        }

        /// <summary>
        /// 해킹 게임의 모든 수치를 초기 상태로 되돌리고 1단계 브루트포스를 시작한다.
        /// 입력값과 반환값은 없으며 진행 상태 변수들을 초기화하고 화면을 갱신한다.
        /// </summary>
        private void ResetGame()
        {
            _phase = Phase.BruteForce;
            _currentStage = 0;
            _stageKeystrokes = 0;
            _totalKeystrokes = 0;
            _elapsedTime = 0f;
            _promptBlinkTimer = 0f;
            _promptBlinkVisible = true;

            _logLines.Clear();
            _logLines.Add("<color=#00FFFF>================== CYBER INFILTRATION PROTOCOL ==================</color>");
            _logLines.Add("<color=#39FF14>[CONNECTED]</color> TARGET HOST: 192.168.0.254 (SECURITY MAINFRAME)");
            _logLines.Add("<color=#FFCC00>[FIREWALL ACTIVE]</color> START BRUTE FORCE INJECTION (MASH ANY KEY)");

            RefreshScreen();
        }

        /// <summary>
        /// 로그 목록에 새 문자열을 추가하고 최대 줄 수를 초과하면 가장 오래된 줄을 제거한다.
        /// logLine을 _logLines 리스트에 추가하며 리스트 크기를 유지한다.
        /// </summary>
        private void AddLog(string logLine)
        {
            _logLines.Add(logLine);
            if (_logLines.Count > MAX_LOG_LINES)
            {
                _logLines.RemoveAt(0);
            }
        }

        /// <summary>
        /// 현재 게임 진행 단계와 수치에 맞춰 모든 UI 라벨을 갱신한다.
        /// 입력값과 반환값은 없으며 인스펙터에 연결된 UI 텍스트들을 변경한다.
        /// </summary>
        private void RefreshScreen()
        {
            if (_titleLabel != null)
                _titleLabel.text = "CYBER INFILTRATOR // TERMINAL v3.2";

            if (_stageLabel != null)
            {
                string stageName = _currentStage < STAGE_NAMES.Length ? STAGE_NAMES[_currentStage] : "CORE SYSTEM";
                _stageLabel.text = $"TARGET: [{stageName}] - LAYER {_currentStage + 1} / {_totalStages}";
            }

            if (_targetHostLabel != null)
            {
                _targetHostLabel.text = "<color=#39FF14>● LIVE</color>  HOST: 192.168.0.254  [SECURITY MAINFRAME]";
            }

            if (_stageBadgeLabel != null)
            {
                string stageName = _currentStage < STAGE_NAMES.Length ? STAGE_NAMES[_currentStage] : "CORE SYSTEM";
                _stageBadgeLabel.text = $"LAYER {_currentStage + 1} / {_totalStages} : {stageName}";
            }

            RefreshProgressBar();
            RefreshTerminalLog();
            RefreshPrompt();
            RefreshStats();
        }

        /// <summary>
        /// 현재 스테이지의 타수 진행도를 그래픽 게이지 바 및 수치 퍼센트로 변환하여 표시한다.
        /// _stageKeystrokes와 _requiredKeystrokes를 사용하여 프로그레스 바와 퍼센트 텍스트를 갱신한다.
        /// </summary>
        private void RefreshProgressBar()
        {
            float ratio = _requiredKeystrokes > 0 ? Mathf.Clamp01((float)_stageKeystrokes / _requiredKeystrokes) : 0f;
            if (_phase == Phase.Success) ratio = 1f;

            if (_progressBarFill != null)
            {
                _progressBarFill.fillAmount = ratio;
                RectTransform fillRect = _progressBarFill.rectTransform;
                fillRect.pivot = new Vector2(0f, 0.5f);
                fillRect.anchorMin = new Vector2(0f, 0f);
                fillRect.anchorMax = new Vector2(ratio, 1f);
                fillRect.anchoredPosition = Vector2.zero;
                fillRect.sizeDelta = Vector2.zero;
            }

            if (_progressPercentText != null)
            {
                _progressPercentText.text = $"{Mathf.RoundToInt(ratio * 100f)}%  [ {_stageKeystrokes} / {_requiredKeystrokes} ]";
            }
        }

        /// <summary>
        /// 누적된 사이버 로그들을 줄바꿈으로 연결하여 터미널 화면 텍스트로 표시한다.
        /// _logLines 목록을 사용하여 _terminalLogLabel을 변경한다.
        /// </summary>
        private void RefreshTerminalLog()
        {
            if (_terminalLogLabel == null) return;

            _stringBuilder.Clear();
            for (int i = 0; i < _logLines.Count; i++)
            {
                _stringBuilder.AppendLine(_logLines[i]);
            }
            _terminalLogLabel.text = _stringBuilder.ToString();
        }

        /// <summary>
        /// 현재 게임 단계에 따른 유저 행동 안내 문구를 깜빡임 효과와 함께 갱신한다.
        /// _phase, _currentBypassKey, _promptBlinkVisible을 사용하여 _actionPromptLabel을 변경한다.
        /// </summary>
        private void RefreshPrompt()
        {
            if (_actionPromptLabel == null) return;

            switch (_phase)
            {
                case Phase.BruteForce:
                    if (_promptBoxBg != null)
                        _promptBoxBg.color = new Color(0.04f, 0.16f, 0.12f, 0.85f);
                    _actionPromptLabel.text = _promptBlinkVisible ? "<color=#39FF14>>> MASH KEYBOARD TO INJECT PAYLOAD <<</color>" : "";
                    break;
                case Phase.Bypass:
                    if (_promptBoxBg != null)
                        _promptBoxBg.color = _promptBlinkVisible ? new Color(0.38f, 0.05f, 0.10f, 0.95f) : new Color(0.20f, 0.03f, 0.05f, 0.95f);
                    _actionPromptLabel.text = _promptBlinkVisible ? $"<color=#FFD700>>> [CRITICAL] HIT [{_currentBypassKey}] TO OVERRIDE! <<</color>" : "";
                    break;
                case Phase.Success:
                    if (_promptBoxBg != null)
                        _promptBoxBg.color = new Color(0.06f, 0.24f, 0.14f, 0.95f);
                    _actionPromptLabel.text = "<color=#39FF14>>> [ ACCESS GRANTED ] PRESS [SPACE] TO REPLAY <<</color>";
                    break;
                default:
                    _actionPromptLabel.text = "";
                    break;
            }
        }

        /// <summary>
        /// 통계 라벨의 내용을 비운다.
        /// _statsLabel을 사용하여 화면의 통계 텍스트를 제거한다.
        /// </summary>
        private void RefreshStats()
        {
            if (_statsLabel != null)
                _statsLabel.text = "";
        }
    }
}
