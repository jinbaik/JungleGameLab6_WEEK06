using System;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KeyboardModeling
{
    public sealed class KeyboardInputController : MonoBehaviour
    {
        [Header("Input Mapping")]
        [SerializeField] private bool _inputEnabled = true;
        [SerializeField] private bool _blockShortcuts = true;
        [SerializeField] private KeyBinding[] _bindings = Array.Empty<KeyBinding>();
        private bool _hasFocus;
        private bool _captureInput;
        private Key _lastPressedKey;
        public event Action<Key> KeyPressed;

        [Header("Mini Game")]
        [SerializeField] private MiniGameController _miniGame;
        private KeycapButton[] _keycapButtons;

        [Header("Key Travel")]
        [SerializeField, Min(0f)] private float _pressDistance = 0.15f;
        [SerializeField, Min(0.01f)] private float _animationSpeed = 18f;
        private Vector3[] _restPositions;
        private float[] _pressAmounts;

        [Header("Debug")]
        [SerializeField] private bool _logInput = true;
        private readonly StringBuilder _pressedKeyNames = new StringBuilder();
        private bool[] _previousPressed;
        private string _currentPressedKeys = "None";
        private int _lastStatusFlags = -1;
        private float _nextStatusLogTime;

        public bool InputEnabled
        {
            get => _inputEnabled;
            set => SetInputEnabled(value);
        }

        public bool ShortcutCaptureActive => _captureInput;
        public Key LastPressedKey => _lastPressedKey;
        public string CurrentPressedKeys => _currentPressedKeys;

        void Awake()
        {
            _restPositions = new Vector3[_bindings.Length];
            _pressAmounts = new float[_bindings.Length];
            _previousPressed = new bool[_bindings.Length];
            _keycapButtons = new KeycapButton[_bindings.Length];
            for (int index = 0; index < _bindings.Length; index++)
            {
                _restPositions[index] = _bindings[index].Keycap.localPosition;
                _keycapButtons[index] = _bindings[index].Keycap.GetComponent<KeycapButton>();
            }
            _hasFocus = Application.isFocused;
        }

        void Update()
        {
            bool hasInputFocus = HasInputFocus();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            WindowsKeyboardCapture.SetCaptureEnabled(this, _inputEnabled && _blockShortcuts && hasInputFocus);
            _captureInput = _blockShortcuts && WindowsKeyboardCapture.IsCapturing;
            WindowsKeyboardCapture.SetDebugEnabled(_logInput);
            WindowsKeyboardCapture.LogCapturedEvents();
#else
            _captureInput = false;
#endif
            Keyboard keyboard = Keyboard.current;
            LogInputStatus(hasInputFocus, keyboard != null);
            if (!_inputEnabled || !hasInputFocus || (!_captureInput && keyboard == null))
            {
                RestoreKeys();
                return;
            }

            _pressedKeyNames.Clear();
            for (int index = 0; index < _bindings.Length; index++)
            {
                bool unityPressed = keyboard != null && (keyboard[_bindings[index].Key].isPressed || keyboard[_bindings[index].Key].wasPressedThisFrame);
                bool nativePressed = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if (_captureInput)
                    nativePressed = WindowsKeyboardCapture.IsPressed(_bindings[index].Key);
#endif
                bool isPressed = unityPressed || nativePressed;
                if (isPressed && !_previousPressed[index])
                    KeyPressed?.Invoke(_bindings[index].Key);
                if (isPressed)
                {
                    _lastPressedKey = _bindings[index].Key;
                    if (_pressedKeyNames.Length > 0)
                        _pressedKeyNames.Append(", ");
                    _pressedKeyNames.Append(_bindings[index].Key);

                    int damage = _miniGame != null ? _miniGame.DamageMultiplier : 1;
                    _bindings[index].Keycap.GetComponent<KeycapHealth>().TakeDamage(damage);
                }
                AnimateKey(index, isPressed, Time.unscaledDeltaTime);

                _keycapButtons[index].SetPressed(isPressed);
                if (!_inputEnabled || !isActiveAndEnabled)
                {
                    RestoreKeys();
                    return;
                }

                if (_logInput && isPressed != _previousPressed[index])
                    Debug.Log("[Keyboard][Model] " + (isPressed ? "DOWN " : "UP ") + _bindings[index].Key + " | Unity=" + unityPressed + " Hook=" + nativePressed + " | Target=" + _bindings[index].Keycap.name + " LocalY=" + _bindings[index].Keycap.localPosition.y.ToString("F4"), this);
                _previousPressed[index] = isPressed;
            }
            _currentPressedKeys = _pressedKeyNames.Length == 0 ? "None" : _pressedKeyNames.ToString();
        }

        void OnEnable()
        {
            _hasFocus = Application.isFocused;
            _lastStatusFlags = -1;
            if (_logInput)
                Debug.Log("[Keyboard] Controller enabled | Bindings=" + _bindings.Length, this);
        }

        void OnDisable()
        {
            if (_logInput)
                Debug.Log("[Keyboard] Controller disabled; releasing capture and keys.", this);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            WindowsKeyboardCapture.SetCaptureEnabled(this, false);
#endif
            RestoreKeys();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            if (!hasFocus)
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                WindowsKeyboardCapture.SetCaptureEnabled(this, false);
#endif
                RestoreKeys();
            }
        }

        /// <summary>
        /// 실제 키보드 입력 연동을 켜거나 끈다.
        /// inputEnabled를 _inputEnabled에 저장하고 비활성화하면 모든 키를 원위치로 복원한다.
        /// </summary>
        public void SetInputEnabled(bool inputEnabled)
        {
            _inputEnabled = inputEnabled;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!inputEnabled)
                WindowsKeyboardCapture.SetCaptureEnabled(this, false);
#endif
            if (!inputEnabled && _restPositions != null)
                RestoreKeys();
        }

        /// <summary>
        /// 모델 입력을 받는 창의 포커스 상태를 확인한다.
        /// 앱 상태와 에디터의 Game 창을 사용하여 입력을 받을 수 있는지 반환한다.
        /// </summary>
        private bool HasInputFocus()
        {
#if UNITY_EDITOR
            EditorWindow focusedWindow = EditorWindow.focusedWindow;
            bool gameViewFocused = EditorApplication.isPlaying && !EditorApplication.isPaused && focusedWindow != null && focusedWindow.GetType().Name == "GameView";
#if UNITY_EDITOR_WIN
            return gameViewFocused && WindowsKeyboardCapture.HasApplicationFocus();
#else
            return gameViewFocused;
#endif
#else
            return _hasFocus && Application.isFocused;
#endif
        }

        /// <summary>
        /// 입력 경로의 상태 변화와 주기적인 진단 정보를 Console에 출력한다.
        /// hasInputFocus, hasKeyboard와 설정을 사용하여 포커스, 장치, 훅 상태 및 현재 키를 기록한다.
        /// </summary>
        private void LogInputStatus(bool hasInputFocus, bool hasKeyboard)
        {
            if (!_logInput)
                return;
            int flags = (_inputEnabled ? 1 : 0) | (_blockShortcuts ? 2 : 0) | (hasInputFocus ? 4 : 0) | (hasKeyboard ? 8 : 0) | (_captureInput ? 16 : 0);
            if (flags == _lastStatusFlags && Time.unscaledTime < _nextStatusLogTime)
                return;
            _lastStatusFlags = flags;
            _nextStatusLogTime = Time.unscaledTime + 2f;
            string focusedWindow = "Player";
            int hookEvents = 0;
#if UNITY_EDITOR
            EditorWindow window = EditorWindow.focusedWindow;
            focusedWindow = window == null ? "None" : window.GetType().Name;
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            hookEvents = WindowsKeyboardCapture.EventCount;
#endif
            Debug.Log("[Keyboard][Status] Enabled=" + _inputEnabled + " Focus=" + hasInputFocus + " Window=" + focusedWindow + " UnityKeyboard=" + hasKeyboard + " BlockShortcuts=" + _blockShortcuts + " HookActive=" + _captureInput + " HookEvents=" + hookEvents + " Bindings=" + _bindings.Length + " Held=" + _currentPressedKeys, this);
        }

        /// <summary>
        /// 지정한 키캡을 눌림 또는 복귀 위치로 부드럽게 이동한다.
        /// index, isPressed, deltaTime과 Inspector의 이동 설정을 사용하여 키캡 위치와 눌림량을 변경한다.
        /// </summary>
        private void AnimateKey(int index, bool isPressed, float deltaTime)
        {
            _pressAmounts[index] = Mathf.MoveTowards(_pressAmounts[index], isPressed ? 1f : 0f, _animationSpeed * deltaTime);
            _bindings[index].Keycap.localPosition = _restPositions[index] + Vector3.down * (_pressDistance * _pressAmounts[index]);
        }

        /// <summary>
        /// 모든 키캡의 눌림 상태를 해제한다.
        /// 초기 위치 배열과 매핑된 키캡을 사용하여 위치, 눌림량, 기능 실행 상태를 초기화한다.
        /// </summary>
        private void RestoreKeys()
        {
            _captureInput = false;
            _currentPressedKeys = "None";
            for (int index = 0; index < _bindings.Length; index++)
            {
                if (_logInput && _previousPressed[index])
                    Debug.Log("[Keyboard][Model] RESET " + _bindings[index].Key, this);
                _previousPressed[index] = false;
                _bindings[index].Keycap.localPosition = _restPositions[index];
                _pressAmounts[index] = 0f;
                _keycapButtons[index].ResetPressState();
            }
        }

        [Serializable]
        private sealed class KeyBinding
        {
            [Header("Physical Key")]
            [SerializeField] private Key _key;
            [SerializeField] private Transform _keycap;

            public Key Key => _key;
            public Transform Keycap => _keycap;
        }
    }
}
