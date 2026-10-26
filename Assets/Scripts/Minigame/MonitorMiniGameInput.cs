using System;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;

namespace Game.Player
{
    public sealed class MonitorMiniGameInput : MonoBehaviour
    {
        [Header("Connections")]
        [SerializeField] private PlayerInteractionController _player;
        [SerializeField] private bool _useWorkshopFocus;
        [SerializeField] private MonitorKeyboardHolder _holder;
        [SerializeField] private MonitorMiniGameManager _manager;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private bool _autoStartOnFocus;
        [SerializeField, Min(0)] private int _initialGameIndex;
        private KeyboardInputController _keyboard;
        private Keyboard _textDevice;
        private PlayerInteractionController _subscribedPlayer;
        private bool _previousInput;
        private bool _previousAttack;
        public bool HasFocus => _keyboard != null;
        public event Action<bool> FocusChanged;

        [Header("Backspace Repeat")]
        [SerializeField, Min(0.1f)] private float _backspaceRepeatDelay = 0.4f;
        [SerializeField, Min(0.01f)] private float _backspaceRepeatInterval = 0.05f;
        private bool _backspaceHeld;
        private float _backspaceRepeatRemaining;

        void Reset()
        {
            SetSceneReferences();
        }

        void OnEnable()
        {
            SetSceneReferences();
        }

        void Start()
        {
            SetSceneReferences();
        }

        void OnDisable()
        {
            if (_subscribedPlayer != null)
                _subscribedPlayer.MonitorFocusChanged -= HandleFocusChanged;
            _subscribedPlayer = null;
            Disconnect();
        }

        /// <summary>
        /// 모니터 계층의 매니저와 Canvas 및 거치대를 찾고 같은 씬의 플레이어와 메인 카메라를 연결한다.
        /// 모니터 계층과 현재 씬의 루트 오브젝트를 사용해 Inspector 참조를 할당하며 실행 중에는 포커스 이벤트 구독도 갱신한다.
        /// </summary>
        [ContextMenu("Auto Assign References")]
        public void SetSceneReferences()
        {
            _manager = GetComponentInChildren<MonitorMiniGameManager>(true);
            _canvas = GetComponentInChildren<Canvas>(true);
            for (Transform parent = transform; parent != null; parent = parent.parent)
            {
                _holder = parent.GetComponentInChildren<MonitorKeyboardHolder>(true);
                if (_holder != null) break;
            }
            if (_canvas == null || !gameObject.scene.IsValid()) return;

            if (gameObject.scene.isLoaded)
            {
                Camera sceneCamera = null;
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    if (!_useWorkshopFocus && _player == null)
                        _player = root.GetComponentInChildren<PlayerInteractionController>(true);
                    foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                    {
                        if (candidate.CompareTag("MainCamera"))
                        {
                            sceneCamera = candidate;
                            break;
                        }
                    }
                }
                _canvas.worldCamera = sceneCamera;
            }

            if (_useWorkshopFocus) return;
            if (!Application.isPlaying || !isActiveAndEnabled
                || (_player != null && _subscribedPlayer == _player)) return;
            if (_subscribedPlayer != null)
            {
                _subscribedPlayer.MonitorFocusChanged -= HandleFocusChanged;
                Disconnect();
            }
            _subscribedPlayer = _player;
            if (_subscribedPlayer != null)
                _subscribedPlayer.MonitorFocusChanged += HandleFocusChanged;
            else
                Debug.LogWarning("모니터와 같은 씬에 PlayerInteractionController를 배치하세요.", this);
        }

        /// <summary>
        /// Workshop 전용 연결에서 focused에 따라 기존 모니터 입력을 시작하거나 종료한다.
        /// 기존 키보드 입력과 공격 상태를 보관 및 복원하며 Player 이벤트 경로는 유지한다.
        /// </summary>
        public void SetWorkshopFocus(bool focused)
        {
            if (!_useWorkshopFocus) return;
            if (focused && isActiveAndEnabled) HandleFocusChanged(_holder);
            else Disconnect();
        }

        void Update()
        {
            if (!CanReceiveKey(Key.Backspace) || _textDevice == null || !_textDevice.backspaceKey.isPressed)
            {
                _backspaceHeld = false;
                return;
            }
            if (!_backspaceHeld) return;
            _backspaceRepeatRemaining -= Time.deltaTime;
            while (_backspaceRepeatRemaining <= 0f)
            {
                _manager.SendKeyPressed(Key.Backspace);
                _backspaceRepeatRemaining += Mathf.Max(0.01f, _backspaceRepeatInterval);
            }
        }

        /// <summary>포커스된 holder의 키보드와 문자 입력을 구독하고 포커스 진입을 알린다. 자동 시작 설정이 켜져 있으면 초기 게임을 실행하고 종료 시 기존 입력 설정을 복원한다.</summary>
        private void HandleFocusChanged(MonitorKeyboardHolder holder)
        {
            Disconnect();
            if (holder == null || holder != _holder || holder.KeyboardBody == null) return;
            _keyboard = holder.KeyboardBody.GetComponent<KeyboardInputController>();
            if (_keyboard == null) return;
            _previousInput = _keyboard.InputEnabled;
            _previousAttack = _keyboard.AttackEnabled;
            _keyboard.SetInputEnabled(true);
            _keyboard.SetAttackEnabled(false);
            _keyboard.KeyPressed += HandleKeyPressed;
            _textDevice = Keyboard.current;
            if (_textDevice != null) _textDevice.onTextInput += HandleTextInput;
            FocusChanged?.Invoke(true);
            if (_autoStartOnFocus) _manager.StartGame(_initialGameIndex);
        }

        /// <summary>실제 거치 키보드의 key를 현재 게임에 전달하고 Backspace면 반복 삭제 대기를 시작한다. 튜토리얼 또는 일시정지 중 입력은 무시하며 타건 연출은 기존 컨트롤러가 처리한다.</summary>
        private void HandleKeyPressed(Key key)
        {
            if (!CanReceiveKey(key)) return;
            _manager.SendKeyPressed(key);
            if (key == Key.Backspace)
            {
                _backspaceHeld = true;
                _backspaceRepeatRemaining = _backspaceRepeatDelay;
            }
        }

        /// <summary>장치가 입력한 character를 포커스된 게임에 전달한다. 키보드 입력이 꺼졌거나 튜토리얼 및 일시정지 중이면 무시한다.</summary>
        private void HandleTextInput(char character)
        {
            if (CanReceiveInput() && CanReceiveCharacter(character)) _manager.SendTextInput(character);
        }

        /// <summary>
        /// key에 대응하는 거치 키보드의 축과 입력 허용 상태를 검사해 반환한다.
        /// 연결된 키보드만 조회하며 축이 없거나 포커스가 없으면 false를 반환한다.
        /// </summary>
        public bool CanReceiveKey(Key key)
        {
            return CanReceiveInput() && _keyboard.IsSwitchInstalled(key);
        }

        /// <summary>
        /// character를 현재 배열의 키 또는 두벌식 구성 키로 변환해 축 장착 여부를 반환한다.
        /// 대소문자와 기호 및 한글 조합을 검사하며 대응 키를 확인할 수 없는 문자는 전달하지 않는다.
        /// </summary>
        private bool CanReceiveCharacter(char character)
        {
            if (_textDevice == null) return false;
            if ((_textDevice.leftShiftKey.isPressed && !CanReceiveKey(Key.LeftShift))
                || (_textDevice.rightShiftKey.isPressed && !CanReceiveKey(Key.RightShift))) return false;
            if (character >= '\uac00' && character <= '\ud7a3')
            {
                int syllable = character - '\uac00';
                string initial = "rRseEfaqQtTdwWczxvg";
                string[] vowels = { "k", "o", "i", "O", "j", "p", "u", "P", "h", "hk", "ho", "hl", "y", "n", "nj", "np", "nl", "b", "m", "ml", "l" };
                string[] finals = { "", "r", "R", "rt", "s", "sw", "sg", "e", "f", "fr", "fa", "fq", "ft", "fx", "fv", "fg", "a", "q", "qt", "t", "T", "d", "w", "c", "z", "x", "v", "g" };
                return CanReceiveCharacters(initial[syllable / 588] + vowels[syllable / 28 % 21] + finals[syllable % 28]);
            }
            const string JAMO = "ㄱㄲㄳㄴㄵㄶㄷㄸㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅃㅄㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
            string[] jamoKeys = { "r", "R", "rt", "s", "sw", "sg", "e", "E", "f", "fr", "fa", "fq", "ft", "fx", "fv", "fg", "a", "q", "Q", "qt", "t", "T", "d", "w", "W", "c", "z", "x", "v", "g", "k", "o", "i", "O", "j", "p", "u", "P", "h", "hk", "ho", "hl", "y", "n", "nj", "np", "nl", "b", "m", "ml", "l" };
            int jamoIndex = JAMO.IndexOf(character);
            if (jamoIndex >= 0) return CanReceiveCharacters(jamoKeys[jamoIndex]);
            const string SHIFTED = "~!@#$%^&*()_+{}|:\"<>?";
            const string UNSHIFTED = "`1234567890-=[]\\;',./";
            int shiftedIndex = SHIFTED.IndexOf(character);
            char keyCharacter = shiftedIndex >= 0 ? UNSHIFTED[shiftedIndex] : char.ToLowerInvariant(character);
            if (keyCharacter == ' ') return CanReceiveKey(Key.Space);
            var control = _textDevice.FindKeyOnCurrentKeyboardLayout(keyCharacter.ToString());
            return control != null && CanReceiveKey(control.keyCode);
        }

        /// <summary>
        /// characters를 구성하는 모든 영문 물리 키의 축 장착 여부를 반환한다.
        /// 두벌식 조합에 필요한 키를 검사하며 입력이나 조합 상태는 변경하지 않는다.
        /// </summary>
        private bool CanReceiveCharacters(string characters)
        {
            foreach (char character in characters)
            {
                Key key = (Key)((int)Key.A + char.ToLowerInvariant(character) - 'a');
                if (!CanReceiveKey(key)) return false;
            }
            return true;
        }

        /// <summary>현재 키보드와 앱 포커스 및 튜토리얼 상태로 게임 입력 허용 여부를 반환한다. 게임이나 키보드 상태는 변경하지 않는다.</summary>
        private bool CanReceiveInput()
        {
            return _keyboard != null && _keyboard.isActiveAndEnabled && _keyboard.InputEnabled
                && Application.isFocused && Time.timeScale > 0f && !(Game.Tutorials.FirstEncounterTutorial.IsBlocking || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked);
        }

        /// <summary>키보드 및 문자 입력 구독을 해제하고 게임을 종료한다. 입력값과 반환값 없이 포커스 진입 전에 보관한 타건 및 공격 설정을 복원한다.</summary>
        private void Disconnect()
        {
            _backspaceHeld = false;
            if (_textDevice != null) _textDevice.onTextInput -= HandleTextInput;
            _textDevice = null;
            _manager.StopGame();
            if (_keyboard != null)
            {
                _keyboard.KeyPressed -= HandleKeyPressed;
                _keyboard.SetInputEnabled(_previousInput);
                _keyboard.SetAttackEnabled(_previousAttack);
            }
            _keyboard = null;
            FocusChanged?.Invoke(false);
        }
    }
}
