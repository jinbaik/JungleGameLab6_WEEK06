using System;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Game.Session;
using Game.Settings;

namespace KeyboardModeling
{
    /// <summary>
    /// 실제 키보드 장치 입력을 감지하여 바인딩된 각 키캡의 눌림/복귀 이동 애니메이션(Key travel)을 재생하고,
    /// 타격 요청 및 키 입력 이벤트를 발행하는 입력 처리 전담 컴포넌트이다.
    /// </summary>
    public sealed class KeyboardInputController : MonoBehaviour
    {
        public static event Action<KeyboardInputController, Key> PlaytestKeyProcessing;

        /// <summary>
        /// 새 실행의 관찰용 입력 이벤트 구독을 초기화한다.
        /// 도메인 재로드가 꺼진 에디터에서 이전 로깅 구독을 남기지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaytestObservers() { PlaytestKeyProcessing = null; }

        [Header("Input Mapping")]
        [SerializeField] private bool _inputEnabled = true;
        [SerializeField] private KeyBinding[] _bindings = Array.Empty<KeyBinding>();
        private bool _attackEnabled = true;
        private bool _alphabetKeysRequired;
        private bool _allKeysRequired;
        private bool _hasFocus;
        private Key _lastPressedKey;
        private KeycapButton[] _keycapButtons;
        private KeycapHealth[] _keycapHealths;
        private MeshRenderer[] _keycapShellRenderers;
        private KeyboardSpawnProfile _spawnProfile;
        private readonly Dictionary<Transform, KeyboardSwitch> _switchesByKeycap = new Dictionary<Transform, KeyboardSwitch>();
        private readonly Dictionary<Key, KeyboardSwitch> _switchesByKey = new Dictionary<Key, KeyboardSwitch>();
        private bool _switchBindingsCached;
        private bool _usesLegacySwitchSound;

        [Header("Battle Manual Input")]
        private readonly HashSet<Key> _manualHeldKeys = new HashSet<Key>();
        private readonly HashSet<Key> _approvedManualKeys = new HashSet<Key>();
        private int _maximumManualKeys;
        private bool _manualInputSuspended = true;

        public event Action<Key> KeyPressed;
        public event Action KeycapsRestored;
        public event Action<bool> AttackModeChanged;
        public event Action<KeycapHealth> AttackRequested;
        public event Action<Transform> KeycapPressed;
        public event Action<Transform> ManualKeycapHit;
        public event Action<Transform> AutomaticKeycapHit;
        public event Action<Transform> AreaKeycapHit;

        [Header("Key Travel")]
        [SerializeField, Min(0f)] private float _pressDistance = 0.15f;
        [SerializeField, Min(0.01f)] private float _animationSpeed = 18f;
        private Vector3[] _restPositions;
        private float[] _pressAmounts;
        private float _lastPressDistance;
        private bool _hitStopEnabled;
        private bool[] _actuatedKeys;
        private float[] _waveOffsets;
        private readonly Dictionary<Transform, int> _waveBindingIndices = new Dictionary<Transform, int>();

        [Header("Switch Stem Travel")]
        [SerializeField, Min(0f), Tooltip("축의 기존 위치에서 추가로 올릴 높이입니다. 키보드 모델의 로컬 단위를 사용합니다.")]
        private float _stemHeightOffset = 0.08f;
        [SerializeField, Min(0f), Tooltip("키캡 파괴 후 키를 완전히 눌렀을 때 축이 내려가는 거리입니다. 키보드 모델의 로컬 단위를 사용합니다.")]
        private float _stemPressDistance = 0.2f;
        [SerializeField, Min(0.01f), Tooltip("축의 세로 길이 배율입니다. 1은 원래 길이이며 가로 두께는 유지합니다.")]
        private float _stemLengthMultiplier = 1.5f;
        private Transform[][] _switchStems;
        private Vector3[][] _stemRestPositions;
        private Vector3[][] _stemRestScales;

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
        [SerializeField] private FmodSwitchSound _fmodSwitchSound;
        private GameSettings _gameSettings;

        private enum FmodSwitchSound
        {
            Disabled,
            Blue,
            Brown,
            Sans,
            Red,
            Pop,
            Typewriter,
            FireWorks,
            Animal
        }

        public bool InputEnabled
        {
            get => _inputEnabled;
            set => SetInputEnabled(value);
        }

        public bool AttackEnabled => _attackEnabled;

        public Key LastPressedKey => _lastPressedKey;
        public string CurrentPressedKeys => _currentPressedKeys;
        public KeyboardSpawnProfile SpawnProfile => _spawnProfile;

        /// <summary>
        /// UI와 기본 설정에 사용할 공통 선택 타입을 반환한다.
        /// 개별 키의 실제 타건음과 분리된 선택값이며 Disabled이면 null을 반환한다.
        /// </summary>
        public SwitchType? CurrentSwitchSound => _fmodSwitchSound switch
        {
            FmodSwitchSound.Blue => SwitchType.Blue,
            FmodSwitchSound.Red => SwitchType.Red,
            FmodSwitchSound.Brown => SwitchType.Brown,
            FmodSwitchSound.Sans => SwitchType.Sans,
            FmodSwitchSound.Pop => SwitchType.Pop,
            FmodSwitchSound.Typewriter => SwitchType.Typewriter,
            FmodSwitchSound.FireWorks => SwitchType.FireWorks,
            FmodSwitchSound.Animal => SwitchType.Animal,
            _ => null
        };

        /// <summary>
        /// profile의 키 식별자를 layout과 연결하여 키캡의 등급·체력·보상·외피 재질을 초기화한다.
        /// 모든 식별자를 검사한 뒤 적용된 profile을 _spawnProfile에 저장한다.
        /// </summary>
        public void InitializeSpawnProfile(
            KeyboardSpawnProfile profile,
            IReadOnlyDictionary<Key, KeycapHealth> layout)
        {
            if (_spawnProfile != null || profile.Keycaps.Count != layout.Count)
            {
                throw new InvalidOperationException(
                    "Spawn profile must initialize a matching new keyboard once.");
            }

            foreach (KeyboardSpawnProfile.KeycapData data in profile.Keycaps)
            {
                if (!layout.ContainsKey(data.Key))
                {
                    throw new InvalidOperationException(
                        $"Spawn profile key is missing: {data.Key}.");
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

        /// <summary>
        /// 실제 입력 바인딩에 정의된 필수 키와 키캡 슬롯의 읽기 전용 매핑을 반환한다.
        /// 누락 및 중복 슬롯은 설정 오류로 알리며 설치 상태와 기본 외형은 변경하지 않는다.
        /// </summary>
        public IReadOnlyDictionary<Key, Transform> GetRequiredKeycapSlots()
        {
            var layout = new Dictionary<Key, Transform>();
            var slots = new HashSet<Transform>();
            foreach (KeyBinding binding in _bindings)
            {
                if (binding.Key == Key.None || binding.Keycap == null || !slots.Add(binding.Keycap))
                    throw new InvalidOperationException("필수 키캡 슬롯 바인딩이 누락되었거나 중복되었습니다.");
                layout.Add(binding.Key, binding.Keycap);
            }
            if (layout.Count == 0) throw new InvalidOperationException("필수 키캡 슬롯 정의가 없습니다.");
            return new System.Collections.ObjectModel.ReadOnlyDictionary<Key, Transform>(layout);
        }

        /// <summary>
        /// key에 바인딩된 물리 키캡 Transform을 찾아 반환한다.
        /// 바인딩이 없으면 false와 null을 반환한다.
        /// </summary>
        public bool TryGetKeycapTransform(Key key, out Transform keycap)
        {
            foreach (KeyBinding binding in _bindings)
            {
                if (binding.Key != key)
                    continue;

                keycap = binding.Keycap;
                return keycap != null;
            }

            keycap = null;
            return false;
        }

        /// <summary>
        /// key에 연결된 축의 장착 상태를 반환한다.
        /// 기존 스위치 바인딩과 활성 상태를 조회하며 입력 및 전투 상태는 변경하지 않는다.
        /// </summary>
        public bool IsSwitchInstalled(Key key)
        {
            return TryGetSwitch(key, out KeyboardSwitch keyboardSwitch)
                && keyboardSwitch.gameObject.activeInHierarchy;
        }

        /// <summary>토너먼트의 완파 대상을 A~Z로 제한하며 기존 씬의 대상 설정은 변경하지 않는다.</summary>
        public void UseAlphabetDestructionKeys() { _alphabetKeysRequired = true; }

        /// <summary>전체 바인딩을 파괴 추적 대상으로 지정하며 영문 키 제한을 해제한다.</summary>
        public void UseAllDestructionKeys() { _allKeysRequired = true; _alphabetKeysRequired = false; }

        /// <summary>현재 바인딩과 영문 키 제한 상태로 완파 대상 키캡 목록을 생성하여 반환한다.</summary>
        public List<KeycapHealth> GetDestructionKeycaps()
        {
            List<KeycapHealth> result = new List<KeycapHealth>();
            foreach (KeyBinding binding in _bindings)
            {
                Key key = binding.Key;
                if (_alphabetKeysRequired && (key < Key.A || key > Key.Z)) continue;
                bool required = (key >= Key.A && key <= Key.Z) || (key >= Key.Digit1 && key <= Key.Digit0) || (key >= Key.F1 && key <= Key.F12);
                required |= key == Key.Comma || key == Key.Period || key == Key.Slash || key == Key.Semicolon || key == Key.Quote || key == Key.LeftBracket || key == Key.RightBracket || key == Key.Minus || key == Key.Equals || key == Key.Backquote;
                if (_allKeysRequired || required) result.Add(binding.Keycap.GetComponent<KeycapHealth>());
            }
            return result;
        }

        /// <summary>현재 키캡과 축 스템 위치를 타건 복원 기준으로 갱신한다. 바인딩과 축별 사운드는 유지하며 Awake 이전이면 이후 기본 캐시를 사용한다.</summary>
        public void RefreshKeyRestPositions()
        {
            if (_restPositions == null) return;
            for (int index = 0; index < _bindings.Length; index++)
            {
                _restPositions[index] = _bindings[index].Keycap.localPosition;
                CacheSwitchStems(index);
            }
        }

        void Awake()
        {
            CacheSwitchBindings();
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive)
            {
                // WorkShop0에서는 타건을 유지하고 공격 입력만 비활성화한다.
                // SetAttackEnabled(true);
                SetAttackEnabled(false);
            }
            GameSession session = FindAnyObjectByType<GameSession>();
            _gameSettings = session != null ? session.Settings : null;
            if (_gameSettings != null)
            {
                _gameSettings.RegisterSfxSource(_audioSource);
                foreach (AudioSource source in GetComponentsInChildren<AudioSource>(true))
                    _gameSettings.RegisterSfxSource(source);
            }
            _actuatedKeys = new bool[_bindings.Length];
            _waveOffsets = new float[_bindings.Length];
            _restPositions = new Vector3[_bindings.Length];
            _switchStems = new Transform[_bindings.Length][];
            _stemRestPositions = new Vector3[_bindings.Length][];
            _stemRestScales = new Vector3[_bindings.Length][];
            _pressAmounts = new float[_bindings.Length];
            _previousPressed = new bool[_bindings.Length];
            _keycapButtons = new KeycapButton[_bindings.Length];
            _keycapHealths = new KeycapHealth[_bindings.Length];
            _keycapShellRenderers = new MeshRenderer[_bindings.Length];
            _lastPressDistance = _pressDistance;
            for (int index = 0; index < _bindings.Length; index++)
            {
                _restPositions[index] = _bindings[index].Keycap.localPosition;
                _waveBindingIndices[_bindings[index].Keycap] = index;
                _keycapButtons[index] = _bindings[index].Keycap.GetComponent<KeycapButton>();
                _keycapHealths[index] = _bindings[index].Keycap.GetComponent<KeycapHealth>();
                _keycapShellRenderers[index] = _bindings[index].Keycap.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                CacheSwitchStems(index);
                UpdateSwitchStems(index);
            }
            _hasFocus = Application.isFocused;
        }


        void Update()
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking || Game.Tournament.TournamentController.IsReturnPaused
                || InGameCursor.IsSystemDialogOpen || Game.Logging.PlaytestResearchSession.IsUploadConfirmationOpen)
            {
                SuspendManualInput();
                return;
            }
            bool hasInputFocus = HasInputFocus();
            Keyboard keyboard = Keyboard.current;
            LogInputStatus(hasInputFocus, keyboard != null);
            if (!_inputEnabled || !hasInputFocus || keyboard == null)
            {
                SuspendManualInput();
                RestoreKeys();
                return;
            }

            if (_maximumManualKeys > 0)
            {
                if (_manualInputSuspended) SynchronizeManualInput(keyboard);
                // 모든 물리 키의 해제를 먼저 반영하며 파괴된 키캡도 승인 자리를 유지한다.
                foreach (KeyBinding binding in _bindings)
                {
                    if (WindowsSpecialKeyInput.IsPressed(keyboard, binding.Key)) continue;
                    _manualHeldKeys.Remove(binding.Key);
                    _approvedManualKeys.Remove(binding.Key);
                }
            }

            bool pressedKeysChanged = false;
            for (int index = 0; index < _bindings.Length; index++)
            {
                bool unityPressed = WindowsSpecialKeyInput.IsPressed(keyboard, _bindings[index].Key);
                bool isPressed = unityPressed;
                bool stateChanged = isPressed != _previousPressed[index];
                bool pressedThisFrame = isPressed && stateChanged;
                pressedKeysChanged |= stateChanged;
                _previousPressed[index] = isPressed;
                bool acceptedPress = pressedThisFrame && (_maximumManualKeys == 0 || TryApproveManualInput(index));
                bool gameplayPressed = isPressed && (_maximumManualKeys == 0 || _approvedManualKeys.Contains(_bindings[index].Key));
                if (isPressed)
                {
                    _lastPressedKey = _bindings[index].Key;
                }
                AnimateKey(index, gameplayPressed || _actuatedKeys[index], _hitStopEnabled && Time.timeScale == 0f ? 0f : Time.unscaledDeltaTime);

                if (acceptedPress)
                {
                    PlaytestKeyProcessing?.Invoke(this, _bindings[index].Key);
                    PlayPressSound(_bindings[index].Keycap, _keycapHealths[index]);

                    KeycapPressed?.Invoke(_bindings[index].Keycap);
                    KeyPressed?.Invoke(_bindings[index].Key);
                    if (!_inputEnabled || !isActiveAndEnabled)
                    {
                        RestoreKeys();
                        return;
                    }
                }

                if (_maximumManualKeys == 0 || acceptedPress || !gameplayPressed)
                    _keycapButtons[index].SetPressed(gameplayPressed);
                if (!_inputEnabled || !isActiveAndEnabled)
                {
                    RestoreKeys();
                    return;
                }

                if (_attackEnabled && acceptedPress && _keycapHealths[index] != null &&
                    (_keycapHealths[index].CurrentHP == 0 || _keycapButtons[index].isActiveAndEnabled))
                {
                    AttackRequested?.Invoke(_keycapHealths[index]);
                    if (!_inputEnabled || !isActiveAndEnabled)
                    {
                        RestoreKeys();
                        return;
                    }
                }

                if (_logInput && stateChanged)
                    Debug.Log("[Keyboard][Model] " + (isPressed ? "DOWN " : "UP ") + _bindings[index].Key + " | Unity=" + unityPressed + " | Target=" + _bindings[index].Keycap.name + " LocalY=" + _bindings[index].Keycap.localPosition.y.ToString("F4"), this);
            }
            _lastPressDistance = _pressDistance;
            if (pressedKeysChanged)
                RefreshPressedKeyNames();
        }

        /// <summary>
        /// 플레이어 전투 입력에 maximumKeys개의 동시 승인 제한을 설정한다.
        /// 현재 누른 키를 기준 상태로 수집해 생성 전부터 유지된 키는 재입력으로 처리하지 않는다.
        /// </summary>
        public void ConfigureBattleManualInput(int maximumKeys)
        {
            _maximumManualKeys = Mathf.Max(1, maximumKeys);
            SuspendManualInput();
            if (Keyboard.current != null) SynchronizeManualInput(Keyboard.current);
        }

        /// <summary>
        /// index의 새 물리 키 누름이 유효한 전투 대상이고 빈 자리가 있는지 검사해 승인 여부를 반환한다.
        /// 거부된 누름도 유지 목록에 기록하며 승인 자리는 실제 키 해제 전까지 반환하지 않는다.
        /// </summary>
        private bool TryApproveManualInput(int index)
        {
            Key key = _bindings[index].Key;
            if (!_manualHeldKeys.Add(key)) return false;
            if (!_attackEnabled || _keycapHealths[index] == null || _keycapHealths[index].CurrentHP <= 0
                || !_keycapButtons[index].isActiveAndEnabled || _approvedManualKeys.Count >= _maximumManualKeys)
                return false;
            return _approvedManualKeys.Add(key);
        }

        /// <summary>
        /// keyboard의 현재 눌린 바인딩을 수집하고 이전 승인을 비운다.
        /// 입력 재개 및 키보드 생성 시 유지 중인 키를 새 누름으로 승인하지 않도록 기준 상태를 저장한다.
        /// </summary>
        private void SynchronizeManualInput(Keyboard keyboard)
        {
            _approvedManualKeys.Clear();
            _manualHeldKeys.Clear();
            foreach (KeyBinding binding in _bindings)
                if (WindowsSpecialKeyInput.IsPressed(keyboard, binding.Key))
                    _manualHeldKeys.Add(binding.Key);
            _manualInputSuspended = false;
        }

        /// <summary>
        /// 입력 중단 시 승인과 눌림 추적을 비우고 다음 유효 입력 프레임의 기준 상태 수집을 예약한다.
        /// 입력과 반환값 없이 포커스 및 라운드 전환 후 남는 승인 자리를 제거한다.
        /// </summary>
        private void SuspendManualInput()
        {
            _approvedManualKeys.Clear();
            _manualHeldKeys.Clear();
            _manualInputSuspended = true;
        }

        /// <summary>
        /// keycap에 명시적으로 연결된 활성 축의 현재 데이터로 FMOD 이벤트를 선택한다.
        /// health로 기본 타격음의 파괴 볼륨을 유지하며 실물 축이 있는 키보드의 빈 위치에는 공통 축 소리를 사용하지 않는다.
        /// </summary>
        private void PlayPressSound(Transform keycap, KeycapHealth health)
        {
            SwitchType? type = null;
            if (TryGetSwitch(keycap, out KeyboardSwitch keyboardSwitch) &&
                keyboardSwitch.gameObject.activeInHierarchy && keyboardSwitch.CurrentData != null)
                type = keyboardSwitch.CurrentData.Type;
            else if (_usesLegacySwitchSound)
                type = CurrentSwitchSound;

            if (type.HasValue)
            {
                var sound = type.Value switch
                {
                    SwitchType.Blue => FMODEvents.instance.BlueSwitch,
                    SwitchType.Brown => FMODEvents.instance.BrownSwitch,
                    SwitchType.Sans => FMODEvents.instance.SansSwitch,
                    SwitchType.Red => FMODEvents.instance.RedSwitch,
                    SwitchType.Pop => FMODEvents.instance.PopSwitch,
                    SwitchType.Typewriter => FMODEvents.instance.TypewriterSwitch,
                    SwitchType.FireWorks => FMODEvents.instance.FireWorksSwitch,
                    SwitchType.Animal => FMODEvents.instance.AnimalSwitch,
                    _ => default(FMODUnity.EventReference)
                };
                if (!sound.IsNull) AudioManager.instance.PlayKeyboardOneShot(sound, transform.position, gameObject);
                return;
            }
            float volume = health != null && health.CurrentHP == 0 ? 0.05f : 1f;
            _audioSource.PlayOneShot(_pressSound, volume);
        }

        /// <summary>
        /// 명시적인 키 바인딩 참조를 읽어 키와 키캡별 스위치 조회 사전을 한 번 생성한다.
        /// 축 종류는 저장하지 않아 교체와 복원 후 최신 데이터를 사용하며 실물 축이 없는 구형 키보드만 공통 설정을 허용한다.
        /// </summary>
        private void CacheSwitchBindings()
        {
            if (_switchBindingsCached) return;
            _switchesByKey.Clear();
            _switchesByKeycap.Clear();
            foreach (KeyBinding binding in _bindings)
            {
                if (binding.Keycap == null || binding.KeyboardSwitch == null) continue;
                _switchesByKey[binding.Key] = binding.KeyboardSwitch;
                _switchesByKeycap[binding.Keycap] = binding.KeyboardSwitch;
            }
            bool hasCustomization = GetComponent<KeyboardCustomization>() != null;
            _usesLegacySwitchSound = !hasCustomization && _switchesByKeycap.Count == 0
                && GetComponentsInChildren<KeyboardSwitch>(true).Length == 0;
            if (hasCustomization && _switchesByKeycap.Count != _bindings.Length)
                Debug.LogWarning($"[Keyboard] {name}: 키별 스위치 연결을 확인하십시오 ({_switchesByKeycap.Count}/{_bindings.Length}).", this);
            _switchBindingsCached = true;
        }

        /// <summary>
        /// 물리 입력 key에 명시적으로 연결된 스위치를 keyboardSwitch에 전달하고 연결 여부를 반환한다.
        /// 설치 여부와 관계없이 참조만 제공하며 호출자가 현재 활성 상태와 데이터를 확인한다.
        /// </summary>
        public bool TryGetSwitch(Key key, out KeyboardSwitch keyboardSwitch)
        {
            CacheSwitchBindings();
            return _switchesByKey.TryGetValue(key, out keyboardSwitch) && keyboardSwitch != null;
        }

        /// <summary>
        /// keycap 또는 그 하위 타격 대상으로부터 바인딩된 스위치를 반환한다.
        /// 현재 키보드 내부의 부모 경로만 확인하며 거리나 별도 배열 순서로 축을 추정하지 않는다.
        /// </summary>
        public bool TryGetSwitch(Transform keycap, out KeyboardSwitch keyboardSwitch)
        {
            CacheSwitchBindings();
            for (Transform current = keycap; current != null && current != transform; current = current.parent)
                if (_switchesByKeycap.TryGetValue(current, out keyboardSwitch) && keyboardSwitch != null) return true;
            keyboardSwitch = null;
            return false;
        }

        /// <summary>
        /// 입력 없이 FMOD 축 사운드를 Disabled로 변경한다.
        /// 설치된 축이 없는 키보드의 사운드 선택값을 null로 표시하게 한다.
        /// </summary>
        public void DisableSwitchSound()
        {
            _fmodSwitchSound = FmodSwitchSound.Disabled;
        }

        /// <summary>
        /// type 입력을 UI와 기본 설정의 공통 선택값으로 변환한다. 실제 타건은 키별 축 데이터를 사용한다.
        /// 변경 성공 여부를 반환하며 알 수 없는 타입은 기존 사운드를 유지한다.
        /// </summary>
        public bool SetSwitchSound(SwitchType type)
        {
            FmodSwitchSound sound;
            switch (type)
            {
                case SwitchType.Blue: sound = FmodSwitchSound.Blue; break;
                case SwitchType.Red: sound = FmodSwitchSound.Red; break;
                case SwitchType.Brown: sound = FmodSwitchSound.Brown; break;
                case SwitchType.Sans: sound = FmodSwitchSound.Sans; break;
                case SwitchType.Pop: sound = FmodSwitchSound.Pop; break;
                case SwitchType.Typewriter: sound = FmodSwitchSound.Typewriter; break;
                case SwitchType.FireWorks: sound = FmodSwitchSound.FireWorks; break;
                case SwitchType.Animal: sound = FmodSwitchSound.Animal; break;
                default: return false;
            }

            _fmodSwitchSound = sound;
            return true;
        }

        /// <summary>
        /// 주먹이 충돌한 keycap을 ManualKeycapHit로 전달한다.
        /// 직접 타격 피드백만 알리며 추가 키 입력이나 피해 요청은 발생시키지 않는다.
        /// </summary>
        public void NotifyManualKeycapHit(Transform keycap)
        {
            ManualKeycapHit?.Invoke(keycap);
        }

        /// <summary>
        /// 살아 있는 범위 타격 대상 keycap의 효과음과 피드백 이벤트를 전달한다.
        /// 대상 Transform으로 AreaKeycapHit를 호출하며 추가 키 입력이나 데미지를 발생시키지 않는다.
        /// </summary>
        public void NotifyAreaKeycapHit(Transform keycap)
        {
            PlayPressSound(keycap, keycap.GetComponent<KeycapHealth>());
            AreaKeycapHit?.Invoke(keycap);
        }

        /// <summary>
        /// 자동공격 키캡의 기존 효과음과 전용 피드백 이벤트를 전달한다.
        /// keycap으로 AutomaticKeycapHit를 호출하며 플레이어 입력, 미니게임 진행과 추가 피해 요청은 발생시키지 않는다.
        /// </summary>
        public void NotifyAutomaticKeycapHit(Transform keycap)
        {
            PlayPressSound(keycap, keycap.GetComponent<KeycapHealth>());
            AutomaticKeycapHit?.Invoke(keycap);
        }

        void OnEnable()
        {
            SuspendManualInput();
            _hasFocus = Application.isFocused;
            _lastStatusFlags = -1;
            if (_logInput)
                Debug.Log("[Keyboard] Controller enabled | Bindings=" + _bindings.Length, this);
        }

        void OnDisable()
        {
            SuspendManualInput();
            if (_logInput)
                Debug.Log("[Keyboard] Controller disabled; releasing keys.", this);
            RestoreKeys();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            SuspendManualInput();
            _hasFocus = hasFocus;
            if (!hasFocus)
            {
                RestoreKeys();
            }
        }

        /// <summary>
        /// 실제 키보드 입력 연동을 켜거나 끈다.
        /// inputEnabled를 _inputEnabled에 저장하고 비활성화하면 모든 키를 원위치로 복원한다.
        /// </summary>
        public void SetInputEnabled(bool inputEnabled)
        {
            if (_inputEnabled != inputEnabled)
            {
                SuspendManualInput();
                if (inputEnabled && Keyboard.current != null) SynchronizeManualInput(Keyboard.current);
            }
            _inputEnabled = inputEnabled;
            if (!inputEnabled && _restPositions != null)
                RestoreKeys();
        }

        /// <summary>
        /// 키 입력으로 공격 요청을 발생시킬지 설정한다.
        /// attackEnabled를 저장하며 false일 때 입력 사운드와 키 눌림 처리는 유지한다.
        /// </summary>
        public void SetAttackEnabled(bool attackEnabled)
        {
            // 공용 UI나 복원 코드가 요청해도 WorkShop0에서는 전투 모드를 켜지 않는다.
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive) attackEnabled = false;
            if (_attackEnabled == attackEnabled) return;
            _attackEnabled = attackEnabled;
            AttackModeChanged?.Invoke(attackEnabled);
        }

        /// <summary>
        /// 전체 바인딩의 키캡 체력과 표시를 복원하고 눌림 상태를 초기화한다.
        /// 캐시된 키캡들을 사용하며 위치와 스위치 축 및 완파 추적 수량을 갱신한 뒤 KeycapsRestored를 알린다.
        /// </summary>
        public void RestoreKeycaps()
        {
            foreach (KeycapHealth health in _keycapHealths)
            {
                if (health != null)
                    health.RestoreHealth();
            }

            RestoreKeys();
            KeyboardDestruction destruction = GetComponent<KeyboardDestruction>();
            if (destruction != null)
                destruction.RefreshKeycapTracking();
            KeycapsRestored?.Invoke();
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
            return gameViewFocused && Application.isFocused;
#else
            return _hasFocus && Application.isFocused;
#endif
        }

        /// <summary>
        /// 입력 경로의 상태 변화와 주기적인 진단 정보를 Console에 출력한다.
        /// hasInputFocus, hasKeyboard와 설정을 사용하여 포커스, 장치 및 현재 키를 기록한다.
        /// </summary>
        private void LogInputStatus(bool hasInputFocus, bool hasKeyboard)
        {
            if (!_logInput)
                return;
            int flags = (_inputEnabled ? 1 : 0) | (hasInputFocus ? 4 : 0) | (hasKeyboard ? 8 : 0);
            if (flags == _lastStatusFlags && Time.unscaledTime < _nextStatusLogTime)
                return;
            _lastStatusFlags = flags;
            _nextStatusLogTime = Time.unscaledTime + 2f;
            string focusedWindow = "Player";
#if UNITY_EDITOR
            EditorWindow window = EditorWindow.focusedWindow;
            focusedWindow = window == null ? "None" : window.GetType().Name;
#endif
            Debug.Log("[Keyboard][Status] Enabled=" + _inputEnabled + " Focus=" + hasInputFocus + " Window=" + focusedWindow + " UnityKeyboard=" + hasKeyboard + " Bindings=" + _bindings.Length + " Held=" + _currentPressedKeys, this);
        }

        /// <summary>
        /// 지정한 키캡을 눌림 또는 복귀 위치로 부드럽게 이동한다.
        /// index, isPressed, deltaTime과 이동 설정을 사용하며 목표 위치에 도달한 키는 갱신하지 않는다.
        /// </summary>
        private void AnimateKey(int index, bool isPressed, float deltaTime)
        {
            float targetAmount = isPressed ? 1f : 0f;
            if (_pressAmounts[index] == targetAmount && (targetAmount == 0f || _lastPressDistance == _pressDistance))
            {
                UpdateSwitchStems(index);
                return;
            }

            _pressAmounts[index] = Mathf.MoveTowards(_pressAmounts[index], targetAmount, _animationSpeed * deltaTime);
            _bindings[index].Keycap.localPosition = _restPositions[index] + Vector3.down * (_pressDistance * _pressAmounts[index]) + Vector3.up * _waveOffsets[index];
            UpdateSwitchStems(index);
        }

        /// <summary>
        /// index의 키캡과 같은 가로 위치에 있는 스위치의 움직이는 축을 찾는다.
        /// 키캡의 부모와 초기 위치를 사용하여 축 Transform, 복귀 위치와 원래 크기를 저장한다.
        /// </summary>
        private void CacheSwitchStems(int index)
        {
            List<Transform> stems = new List<Transform>();
            List<Vector3> restPositions = new List<Vector3>();
            List<Vector3> restScales = new List<Vector3>();
            Transform keycap = _bindings[index].Keycap;
            foreach (Transform mechanism in keycap.parent)
            {
                if (!mechanism.name.StartsWith("Switch_", StringComparison.Ordinal))
                    continue;

                Vector3 offset = mechanism.localPosition - _restPositions[index];
                if (Mathf.Abs(offset.x) > 0.001f || Mathf.Abs(offset.z) > 0.001f)
                    continue;

                foreach (Transform part in mechanism)
                {
                    if (part.name != "MX_Stem_X" && part.name != "MX_Stem_Z" && part.name != "Stabilizer_Stem")
                        continue;

                    stems.Add(part);
                    restPositions.Add(part.localPosition);
                    restScales.Add(part.localScale);
                }
                break;
            }
            _switchStems[index] = stems.ToArray();
            _stemRestPositions[index] = restPositions.ToArray();
            _stemRestScales[index] = restScales.ToArray();
        }

        /// <summary>
        /// index의 현재 눌림량으로 키캡 유무와 관계없이 스위치 축을 아래로 이동한다.
        /// 추가 높이, 눌림 거리와 길이 배율로 축 위치와 세로 크기를 갱신한다.
        /// </summary>
        private void UpdateSwitchStems(int index)
        {
            float amount = _pressAmounts[index];
            Vector3 offset = Vector3.up * (_stemHeightOffset - _stemPressDistance * amount);
            for (int stemIndex = 0; stemIndex < _switchStems[index].Length; stemIndex++)
            {
                _switchStems[index][stemIndex].localPosition = _stemRestPositions[index][stemIndex] + offset;
                Vector3 scale = _stemRestScales[index][stemIndex];
                scale.y *= _stemLengthMultiplier;
                _switchStems[index][stemIndex].localScale = scale;
            }
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
                keycap.localPosition = _restPositions[index] + Vector3.down * (_pressDistance * amount) + Vector3.up * _waveOffsets[index];
                UpdateSwitchStems(index);
                return;
            }
        }
        /// <summary>
        /// keycap의 충격파 높이에 delta를 더하고 기존 눌림 위치와 합쳐 표시한다.
        /// 바인딩 인덱스와 누적 높이를 사용하여 겹치는 충격파를 독립적으로 더하거나 제거한다.
        /// </summary>
        public void AddKeycapWaveOffset(Transform keycap, float delta)
        {
            if (!_waveBindingIndices.TryGetValue(keycap, out int index)) return;
            _waveOffsets[index] += delta;
            keycap.localPosition = _restPositions[index] + Vector3.down * (_pressDistance * _pressAmounts[index]) + Vector3.up * _waveOffsets[index];
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
            _currentPressedKeys = "None";
            for (int index = 0; index < _bindings.Length; index++)
            {
                if (_logInput && _previousPressed[index])
                    Debug.Log("[Keyboard][Model] RESET " + _bindings[index].Key, this);
                _previousPressed[index] = false;
                _actuatedKeys[index] = false;
                if (_pressAmounts[index] != 0f || _waveOffsets[index] != 0f)
                    _bindings[index].Keycap.localPosition = _restPositions[index];
                _waveOffsets[index] = 0f;
                _pressAmounts[index] = 0f;
                UpdateSwitchStems(index);
                _keycapButtons[index].ResetPressState();
            }
        }

        [Serializable]
        private sealed class KeyBinding
        {
            [Header("Physical Key")]
            [SerializeField] private Key _key;
            [SerializeField] private Transform _keycap;
            [SerializeField] private KeyboardSwitch _keyboardSwitch;

            public Key Key => _key;
            public Transform Keycap => _keycap;
            public KeyboardSwitch KeyboardSwitch => _keyboardSwitch;

            /// <summary>
            /// keycap을 이 물리 키의 새 입력 대상으로 저장한다.
            /// 물리 키 식별자는 유지하고 키캡 Transform 참조만 변경한다.
            /// </summary>
            public void SetKeycap(Transform keycap)
            {
                _keycap = keycap;
            }
        }
    }
}

