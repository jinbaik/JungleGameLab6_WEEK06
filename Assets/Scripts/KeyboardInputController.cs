using System;
using System.Collections.Generic;
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
        private KeycapButton[] _keycapButtons;
        private KeycapHealth[] _keycapHealths;
        private KeyboardSpawnProfile _spawnProfile;

        public event Action<Key> KeyPressed;
        public event Action<KeycapHealth> AttackRequested;
        public event Action<Transform> KeycapPressed;

        [Header("Key Travel")]
        [SerializeField, Min(0f)] private float _pressDistance = 0.15f;
        [SerializeField, Min(0.01f)] private float _animationSpeed = 18f;
        private Vector3[] _restPositions;
        private float[] _pressAmounts;
        private float _lastPressDistance;
        private bool _hitStopEnabled;
        private bool[] _actuatedKeys;

        [Header("Debug")]
        [SerializeField] private bool _logInput = true;
        private readonly StringBuilder _pressedKeyNames = new StringBuilder();
        private bool[] _previousPressed;
        private string _currentPressedKeys = "None";
        private int _lastStatusFlags = -1;
        private float _nextStatusLogTime;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _pressSound;

        public bool InputEnabled
        {
            get => _inputEnabled;
            set => SetInputEnabled(value);
        }

        public bool ShortcutCaptureActive => _captureInput;
        public Key LastPressedKey => _lastPressedKey;
        public string CurrentPressedKeys => _currentPressedKeys;
        public KeyboardSpawnProfile SpawnProfile => _spawnProfile;

        /// <summary>
        /// profile의 키 식별자를 layout과 연결하여 새 키보드의 등급·체력·보상을 한 번 초기화한다.
        /// 모든 식별자를 먼저 검사하고 적용이 끝난 프로필을 SpawnProfile로 보관한다.
        /// </summary>
        public void InitializeSpawnProfile(KeyboardSpawnProfile profile, IReadOnlyDictionary<Key, KeycapHealth> layout)
        {
            if (_spawnProfile != null || profile.Keycaps.Count != layout.Count)
            {
                throw new InvalidOperationException("Spawn profile must initialize a matching new keyboard once.");
            }

            foreach (KeyboardSpawnProfile.KeycapData data in profile.Keycaps)
            {
                if (!layout.ContainsKey(data.Key))
                {
                    throw new InvalidOperationException($"Spawn profile key is missing: {data.Key}.");
                }
            }

            foreach (KeyboardSpawnProfile.KeycapData data in profile.Keycaps)
            {
                layout[data.Key].InitializeSpawnData(data);
            }

            _spawnProfile = profile;
        }

        /// <summary>
        /// 현재 키 바인딩을 사용하여 식별자별 키캡 체력의 읽기 전용 매핑을 반환한다.
        /// 중복 식별자와 체력 없는 바인딩은 생성 설정 오류로 거부한다.
        /// </summary>
        public IReadOnlyDictionary<Key, KeycapHealth> GetKeycapLayout()
        {
            Dictionary<Key, KeycapHealth> layout = new Dictionary<Key, KeycapHealth>();
            foreach (KeyBinding binding in _bindings)
            {
                KeycapHealth health = binding.Keycap.GetComponent<KeycapHealth>();
                if (health == null)
                {
                    throw new InvalidOperationException($"Key {binding.Key} requires KeycapHealth.");
                }

                layout.Add(binding.Key, health);
            }

            if (layout.Count == 0)
            {
                throw new InvalidOperationException("Playable keyboard requires key bindings.");
            }

            return layout;
        }

        /// <summary>현재 바인딩에서 영문, 숫자, 지정 기호 및 F1~F12에 해당하는 키캡 체력 목록을 반환한다.</summary>
        public List<KeycapHealth> GetDestructionKeycaps()
        {
            List<KeycapHealth> result = new List<KeycapHealth>();
            foreach (KeyBinding binding in _bindings)
            {
                Key key = binding.Key;
                bool required = (key >= Key.A && key <= Key.Z) || (key >= Key.Digit1 && key <= Key.Digit0) || (key >= Key.F1 && key <= Key.F12);
                required |= key == Key.Comma || key == Key.Period || key == Key.Slash || key == Key.Semicolon || key == Key.Quote || key == Key.LeftBracket || key == Key.RightBracket || key == Key.Minus || key == Key.Equals;
                if (required) result.Add(binding.Keycap.GetComponent<KeycapHealth>());
            }
            return result;
        }

        void Awake()
        {
            _actuatedKeys = new bool[_bindings.Length];
            _restPositions = new Vector3[_bindings.Length];
            _pressAmounts = new float[_bindings.Length];
            _previousPressed = new bool[_bindings.Length];
            _keycapButtons = new KeycapButton[_bindings.Length];
            _keycapHealths = new KeycapHealth[_bindings.Length];
            _lastPressDistance = _pressDistance;
            for (int index = 0; index < _bindings.Length; index++)
            {
                _restPositions[index] = _bindings[index].Keycap.localPosition;
                _keycapButtons[index] = _bindings[index].Keycap.GetComponent<KeycapButton>();
                _keycapHealths[index] = _bindings[index].Keycap.GetComponent<KeycapHealth>();
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

            bool pressedKeysChanged = false;
            for (int index = 0; index < _bindings.Length; index++)
            {
                bool unityPressed = keyboard != null && (keyboard[_bindings[index].Key].isPressed || keyboard[_bindings[index].Key].wasPressedThisFrame);
                bool nativePressed = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if (_captureInput)
                    nativePressed = WindowsKeyboardCapture.IsPressed(_bindings[index].Key);
#endif
                bool isPressed = unityPressed || nativePressed;
                bool stateChanged = isPressed != _previousPressed[index];
                bool pressedThisFrame = isPressed && stateChanged;
                pressedKeysChanged |= stateChanged;
                _previousPressed[index] = isPressed;
                if (isPressed)
                {
                    _lastPressedKey = _bindings[index].Key;
                }
                AnimateKey(index, isPressed || _actuatedKeys[index], _hitStopEnabled && Time.timeScale == 0f ? 0f : Time.unscaledDeltaTime);

                if (pressedThisFrame)
                {
                    float volumeScale = _keycapHealths[index] != null && _keycapHealths[index].CurrentHP == 0 ? 0.05f : 1f;
                    _audioSource.PlayOneShot(_pressSound, volumeScale);

                    KeycapPressed?.Invoke(_bindings[index].Keycap);
                    KeyPressed?.Invoke(_bindings[index].Key);
                    if (!_inputEnabled || !isActiveAndEnabled)
                    {
                        RestoreKeys();
                        return;
                    }
                }

                _keycapButtons[index].SetPressed(isPressed);
                if (!_inputEnabled || !isActiveAndEnabled)
                {
                    RestoreKeys();
                    return;
                }

                if (pressedThisFrame && _keycapButtons[index].isActiveAndEnabled && _keycapHealths[index] != null)
                {
                    AttackRequested?.Invoke(_keycapHealths[index]);
                    if (!_inputEnabled || !isActiveAndEnabled)
                    {
                        RestoreKeys();
                        return;
                    }
                }

                if (_logInput && stateChanged)
                    Debug.Log("[Keyboard][Model] " + (isPressed ? "DOWN " : "UP ") + _bindings[index].Key + " | Unity=" + unityPressed + " Hook=" + nativePressed + " | Target=" + _bindings[index].Keycap.name + " LocalY=" + _bindings[index].Keycap.localPosition.y.ToString("F4"), this);
            }
            _lastPressDistance = _pressDistance;
            if (pressedKeysChanged)
                RefreshPressedKeyNames();
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
        /// index, isPressed, deltaTime과 이동 설정을 사용하며 목표 위치에 도달한 키는 갱신하지 않는다.
        /// </summary>
        private void AnimateKey(int index, bool isPressed, float deltaTime)
        {
            float targetAmount = isPressed ? 1f : 0f;
            if (_pressAmounts[index] == targetAmount && (targetAmount == 0f || _lastPressDistance == _pressDistance))
                return;

            _pressAmounts[index] = Mathf.MoveTowards(_pressAmounts[index], targetAmount, _animationSpeed * deltaTime);
            _bindings[index].Keycap.localPosition = _restPositions[index] + Vector3.down * (_pressDistance * _pressAmounts[index]);
        }
        /// <summary>
        /// 키캡 애니메이션의 히트스톱 연동 여부를 설정한다.
        /// enabled를 저장하여 시간 배율이 0인 동안 연동된 키캡의 움직임을 멈춘다.
        /// </summary>
        public void SetHitStopEnabled(bool enabled)
        {
            _hitStopEnabled = enabled;
        }

        /// <summary>
        /// 기계팔에 눌린 키캡의 표시 상태를 변경한다.
        /// keycap과 pressed를 바인딩 및 표시 위치에 즉시 반영하며 물리 키 입력, 버튼 이벤트와 피해 요청은 추가로 발생시키지 않는다.
        /// </summary>
        public void SetKeycapActuated(Transform keycap, bool pressed)
        {
            for (int index = 0; index < _bindings.Length; index++)
            {
                if (_bindings[index].Keycap != keycap)
                    continue;
                _actuatedKeys[index] = pressed;
                float amount = pressed || _previousPressed[index] ? 1f : 0f;
                _pressAmounts[index] = amount;
                keycap.localPosition = _restPositions[index] + Vector3.down * (_pressDistance * amount);
                return;
            }
        }
        /// <summary>
        /// 현재 눌린 키 목록을 문자열로 갱신한다.
        /// _previousPressed와 _bindings를 사용하여 입력 변화 시에만 _currentPressedKeys를 다시 만든다.
        /// </summary>
        private void RefreshPressedKeyNames()
        {
            _pressedKeyNames.Clear();
            for (int index = 0; index < _bindings.Length; index++)
            {
                if (!_previousPressed[index])
                    continue;

                if (_pressedKeyNames.Length > 0)
                    _pressedKeyNames.Append(", ");
                _pressedKeyNames.Append(_bindings[index].Key);
            }
            _currentPressedKeys = _pressedKeyNames.Length == 0 ? "None" : _pressedKeyNames.ToString();
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
                _actuatedKeys[index] = false;
                if (_pressAmounts[index] != 0f)
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

