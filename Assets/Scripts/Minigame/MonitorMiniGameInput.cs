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
            if (!CanReceiveInput() || _textDevice == null || !_textDevice.backspaceKey.isPressed)
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
            if (holder != _holder || holder.KeyboardBody == null) return;
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
            if (!CanReceiveInput()) return;
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
            if (CanReceiveInput()) _manager.SendTextInput(character);
        }

        /// <summary>현재 키보드와 앱 포커스 및 튜토리얼 상태로 게임 입력 허용 여부를 반환한다. 게임이나 키보드 상태는 변경하지 않는다.</summary>
        private bool CanReceiveInput()
        {
            return _keyboard != null && _keyboard.isActiveAndEnabled && _keyboard.InputEnabled
                && Application.isFocused && Time.timeScale > 0f && !Game.Tutorials.FirstEncounterTutorial.IsBlocking;
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
