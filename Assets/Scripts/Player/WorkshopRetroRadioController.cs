using System;
using System.Collections.Concurrent;

using UnityEngine;
using UnityEngine.InputSystem;

using FMOD.Studio;
using FMODUnity;

namespace Game.Player
{
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class WorkshopRetroRadioController : MonoBehaviour
    {
        private const string MUSIC_PLAYER_PARAMETER = "MusicPlayer";
        private const int EASTER_EGG_TRACK = 5;
        private const float EASTER_EGG_PROBABILITY = 0.1f;
        private const EVENT_CALLBACK_TYPE MUSIC_CALLBACK_MASK =
            EVENT_CALLBACK_TYPE.SOUND_PLAYED | EVENT_CALLBACK_TYPE.SOUND_STOPPED;
        private static readonly string[] TRACK_SOUND_NAMES =
        {
            "Johann Sebastian Bach-Air on G String",
            "Gymnopedie 1 Erik Satie",
            "Frederic Chopin - Nocturne No. 2",
            "Mozart - Rondo Alla Turca",
            "09 Brahams Hungarian Dance No 5",
            "화성연습"
        };
        private static readonly ConcurrentDictionary<IntPtr, SoundCallbackState> CALLBACK_STATES =
            new ConcurrentDictionary<IntPtr, SoundCallbackState>();
        private static readonly EVENT_CALLBACK MUSIC_CALLBACK = HandleMusicCallback;
        private static bool _automaticStartHandled;

        private readonly struct SoundSignal
        {
            public readonly int Generation;
            public readonly EVENT_CALLBACK_TYPE Type;
            public readonly IntPtr Sound;
            public readonly string Name;
            public readonly FMOD.RESULT NameResult;

            /// <summary>
            /// FMOD 콜백의 generation, type, sound, name과 조회 결과를 순수 데이터로 보관한다.
            /// 입력값을 메인 스레드 처리용 필드에 저장하며 Unity 상태는 변경하지 않는다.
            /// </summary>
            public SoundSignal(int generation, EVENT_CALLBACK_TYPE type, IntPtr sound, string name, FMOD.RESULT nameResult)
            {
                Generation = generation;
                Type = type;
                Sound = sound;
                Name = name;
                NameResult = nameResult;
            }
        }

        private sealed class SoundCallbackState
        {
            public readonly int Generation;
            public readonly ConcurrentQueue<SoundSignal> Signals = new ConcurrentQueue<SoundSignal>();

            /// <summary>
            /// generation으로 하나의 RadioMusic 인스턴스 콜백을 식별한다.
            /// 받은 값을 보관하며 이후 신호는 Signals 큐에 기록한다.
            /// </summary>
            public SoundCallbackState(int generation)
            {
                Generation = generation;
            }
        }

        [Header("Hover Outline")]
        [SerializeField] private Material _outlineMaterial;
        [SerializeField] private Shader _outlineDepthShader;
        private WorkshopKeyboardPlacementPreview _outline;

        [Header("Workshop Input")]
        private WorkshopViewController _workshopView;
        private Camera _outputCamera;

        [Header("Interaction")]
        [SerializeField, Min(0.1f)] private float _interactionDistance = 12f;

        [Header("FMOD Playback")]
        private AudioManager _audioManager;
        private EventInstance _music;
        private EventInstance _powerSound;
        private bool _poweredOn;
        private bool _turningOn;
        private bool _initialTrackPending;
        private bool _waitingForMusicStop;
        private int _pendingTrack = -1;
        private int _lastTrack = -1;
        private int _musicGeneration;
        private IntPtr _activeSongSound;
        private SoundCallbackState _callbackState;
        private bool _reportedUnknownSound;

        /// <summary>
        /// 새 플레이 세션에서 이전 FMOD 콜백 상태와 자동 켜기 기록을 비운다.
        /// 입력 없이 정적 상태를 초기화하여 새 게임에서만 시작 자동 재생을 허용한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCallbackStates()
        {
            CALLBACK_STATES.Clear();
            _automaticStartHandled = false;
        }

        void Awake()
        {
            _workshopView = FindFirstObjectByType<WorkshopViewController>();
            if (_workshopView != null) _outputCamera = _workshopView.OutputCamera;
        }

        void OnEnable()
        {
            if (_outputCamera != null && _outlineMaterial != null && _outlineDepthShader != null)
            {
                _outline = new WorkshopKeyboardPlacementPreview(
                    _outlineMaterial, _outlineDepthShader, _outputCamera, Color.white);
                _outline.Prepare(transform);
            }
        }

        void Start()
        {
            if (_automaticStartHandled) return;
            _automaticStartHandled = true;
            TurnOnOrChange(true);
        }

        void LateUpdate()
        {
            UpdatePowerSound();
            UpdateMusicChange();

            bool clicked = false;
            if (_workshopView != null && _outputCamera != null && Mouse.current != null)
            {
                if (_workshopView.TryGetRadioAimPosition(out Vector2 aimPosition))
                {
                    Physics.SyncTransforms();
                    Ray ray = _outputCamera.ScreenPointToRay(aimPosition);
                    if (Physics.Raycast(ray, out RaycastHit hit, _interactionDistance,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && hit.transform.IsChildOf(transform))
                    {
                        _outline?.Show(new Pose(transform.position, transform.rotation));
                        Mouse mouse = Mouse.current;
                        if (mouse.leftButton.wasPressedThisFrame != mouse.rightButton.wasPressedThisFrame)
                        {
                            clicked = true;
                            if (mouse.rightButton.wasPressedThisFrame) TurnOff();
                            else TurnOnOrChange();
                        }
                    }
                }
            }
            ProcessSoundSignals(!clicked);
        }

        /// <summary>
        /// FMOD의 곡 시작·종료 콜백에서 사운드 이름과 핸들을 순수 관리 큐로 전달한다.
        /// eventPointer와 parameters를 사용하며 Unity 객체를 만지지 않고 FMOD 성공값을 반환한다.
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT HandleMusicCallback(EVENT_CALLBACK_TYPE type, IntPtr eventPointer, IntPtr parameters)
        {
            if (!CALLBACK_STATES.TryGetValue(eventPointer, out SoundCallbackState state)) return FMOD.RESULT.OK;
            FMOD.RESULT result = new FMOD.Sound(parameters).getName(out string name, 512);
            state.Signals.Enqueue(new SoundSignal(state.Generation, type, parameters, name, result));
            return FMOD.RESULT.OK;
        }

        /// <summary>
        /// 음악 인스턴스에 곡별 SOUND 콜백을 연결하고 재생 세대를 등록한다.
        /// music 핸들과 정적 콜백을 사용하며 성공하면 메인 스레드의 신호 큐를 보관한다.
        /// </summary>
        private bool RegisterMusicCallback()
        {
            SoundCallbackState state = new SoundCallbackState(++_musicGeneration);
            if (!CALLBACK_STATES.TryAdd(_music.handle, state)) return false;
            FMOD.RESULT result = _music.setCallback(MUSIC_CALLBACK, MUSIC_CALLBACK_MASK);
            if (!CheckResult(result, "음악 콜백 연결"))
            {
                CALLBACK_STATES.TryRemove(_music.handle, out _);
                return false;
            }
            _callbackState = state;
            return true;
        }

        /// <summary>
        /// 현재 음악 인스턴스의 콜백 진입을 끊고 이전 세대의 신호를 폐기한다.
        /// music 핸들을 사용하며 비활성화·수동 변경 후 오래된 종료 신호가 게임 상태를 바꾸지 못하게 한다.
        /// </summary>
        private void UnregisterMusicCallback()
        {
            if (!_music.isValid()) return;
            CALLBACK_STATES.TryRemove(_music.handle, out _);
            CheckResult(_music.setCallback(null, MUSIC_CALLBACK_MASK), "음악 콜백 해제");
            _callbackState = null;
            _activeSongSound = IntPtr.Zero;
        }

        /// <summary>
        /// callback 큐에서 현재 세대의 곡 사운드만 추려 자연 종료를 메인 스레드에서 처리한다.
        /// allowAutomaticChange가 거짓인 수동 클릭 프레임은 넘기며 Noise와 오래된 신호는 무시한다.
        /// </summary>
        private void ProcessSoundSignals(bool allowAutomaticChange)
        {
            SoundCallbackState state = _callbackState;
            if (state == null) return;
            while (state.Signals.TryDequeue(out SoundSignal signal))
            {
                if (signal.Generation != _musicGeneration || !_poweredOn) continue;
                if (signal.NameResult != FMOD.RESULT.OK)
                {
                    if (!_reportedUnknownSound)
                        Debug.LogWarning($"[FMOD] 라디오 사운드 이름 조회 실패: {signal.NameResult}.", this);
                    _reportedUnknownSound = true;
                    continue;
                }

                bool currentSong = _lastTrack >= 0 && signal.Name != null
                    && signal.Name.IndexOf(TRACK_SOUND_NAMES[_lastTrack], StringComparison.OrdinalIgnoreCase) >= 0;
                if (signal.Type == EVENT_CALLBACK_TYPE.SOUND_PLAYED)
                {
                    if (currentSong && !_waitingForMusicStop) _activeSongSound = signal.Sound;
                    else if (!currentSong && !_reportedUnknownSound
                        && signal.Name.IndexOf("white_noise", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        _reportedUnknownSound = true;
                        Debug.LogWarning($"[FMOD] RadioMusic에서 예상하지 못한 사운드: {signal.Name}.", this);
                    }
                    continue;
                }
                if (signal.Type != EVENT_CALLBACK_TYPE.SOUND_STOPPED || !currentSong
                    || signal.Sound != _activeSongSound || _waitingForMusicStop) continue;

                _activeSongSound = IntPtr.Zero;
                if (allowAutomaticChange && !_turningOn && !_powerSound.isValid())
                {
                    ChangeTrackAutomatically();
                    allowAutomaticChange = false;
                }
            }
        }

        /// <summary>
        /// 자연 종료된 곡 다음의 번호를 같은 RadioMusic 인스턴스에 적용한다.
        /// 직전 선택과 MusicPlayer를 사용하며 Noise 재생과 전원·변경 효과음은 유지한다.
        /// </summary>
        private void ChangeTrackAutomatically()
        {
            int nextTrack = SelectNextTrack();
            if (!CheckResult(_music.setParameterByName(MUSIC_PLAYER_PARAMETER, nextTrack, true),
                    $"자동 {nextTrack}번 곡 선택")) return;
            _lastTrack = nextTrack;
        }

        /// <summary>
        /// 좌클릭 시 꺼진 라디오는 전원 효과음을 시작하고 켜진 라디오는 다른 곡을 예약한다.
        /// initialPlayback이 참인 게임 시작 요청은 일반 곡만 예약하며 중복 요청은 재생 상태를 변경하지 않는다.
        /// </summary>
        private void TurnOnOrChange(bool initialPlayback = false)
        {
            if (_powerSound.isValid() || _waitingForMusicStop) return;
            if (!_poweredOn)
            {
                if (StartPowerSound())
                {
                    _turningOn = true;
                    _initialTrackPending = initialPlayback;
                }
                return;
            }

            int nextTrack = SelectNextTrack();
            if (!FMODEvents.instance.RadioChange.IsNull)
                AudioManager.instance.PlayOneShot(FMODEvents.instance.RadioChange, transform.position);
            else
                Debug.LogWarning("[FMOD] RadioChange 이벤트 참조가 비어 있습니다.", this);

            _pendingTrack = nextTrack;
            if (!_music.isValid())
            {
                _pendingTrack = -1;
                StartMusic(nextTrack);
                return;
            }

            if (!CheckResult(_music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE), "기존 음악 정지"))
            {
                _pendingTrack = -1;
                return;
            }
            _waitingForMusicStop = true;
        }

        /// <summary>
        /// 우클릭 시 현재 음악과 대기 중인 시작을 취소하고 전원 효과음을 재생한다.
        /// 전원 전환 및 음악 상태를 사용하며 이미 꺼진 라디오는 변경하지 않는다.
        /// </summary>
        private void TurnOff()
        {
            if (_turningOn)
            {
                ReleasePowerSound(true);
                _turningOn = false;
                _initialTrackPending = false;
                StartPowerSound();
                return;
            }
            if (!_poweredOn || _powerSound.isValid()) return;

            _poweredOn = false;
            _waitingForMusicStop = false;
            _pendingTrack = -1;
            ReleaseMusic();
            StartPowerSound();
        }

        /// <summary>
        /// 현재 등록된 전원 효과음을 라디오 위치에서 시작한다.
        /// AudioManager와 RadioTurnOnOff 참조를 사용하며 성공 여부를 반환한다.
        /// </summary>
        private bool StartPowerSound()
        {
            if (AudioManager.instance == null || FMODEvents.instance == null
                || FMODEvents.instance.RadioTurnOnOff.IsNull)
            {
                Debug.LogError("[FMOD] 라디오 전원 효과음의 관리자 또는 이벤트 참조가 없습니다.", this);
                return false;
            }

            _audioManager = AudioManager.instance;
            _powerSound = _audioManager.CreateEventInstance(FMODEvents.instance.RadioTurnOnOff);
            FMOD.RESULT result = _powerSound.set3DAttributes(RuntimeUtils.To3DAttributes(transform.position));
            if (result == FMOD.RESULT.OK) result = _powerSound.start();
            if (result == FMOD.RESULT.OK) return true;

            CheckResult(result, "전원 효과음 재생");
            ReleasePowerSound(true);
            return false;
        }

        /// <summary>
        /// 전원 효과음의 실제 FMOD 재생 상태를 확인해 종료 후 첫 곡을 시작한다.
        /// 전원 예약 상태를 사용하며 고정 시간 없이 STOPPED 상태에서만 음악 상태를 변경한다.
        /// </summary>
        private void UpdatePowerSound()
        {
            if (!_powerSound.isValid()) return;
            FMOD.RESULT result = _powerSound.getPlaybackState(out PLAYBACK_STATE state);
            if (!CheckResult(result, "전원 효과음 상태 조회"))
            {
                _turningOn = false;
                _initialTrackPending = false;
                ReleasePowerSound(true);
                return;
            }
            if (state != PLAYBACK_STATE.STOPPED) return;

            ReleasePowerSound(false);
            if (!_turningOn) return;
            _turningOn = false;
            int track = SelectNextTrack(_initialTrackPending);
            _initialTrackPending = false;
            StartMusic(track);
        }

        /// <summary>
        /// 이전 음악 인스턴스가 실제로 정지하면 새 인스턴스에서 예약한 곡을 시작한다.
        /// pendingTrack과 FMOD 재생 상태를 사용하며 이전 음악과 새 음악의 동시 재생을 막는다.
        /// </summary>
        private void UpdateMusicChange()
        {
            if (!_waitingForMusicStop) return;
            if (!CheckResult(_music.getPlaybackState(out PLAYBACK_STATE state), "음악 정지 상태 조회"))
            {
                _waitingForMusicStop = false;
                _pendingTrack = -1;
                return;
            }
            if (state != PLAYBACK_STATE.STOPPED) return;

            int track = _pendingTrack;
            _pendingTrack = -1;
            _waitingForMusicStop = false;
            ReleaseMusic();
            if (_poweredOn) StartMusic(track);
        }

        /// <summary>
        /// track의 정수 값을 현재 RadioMusic 인스턴스에 적용한 뒤 재생한다.
        /// NoiseAmount는 변경하지 않으며 성공하면 전원 상태와 직전 곡 번호를 갱신한다.
        /// </summary>
        private void StartMusic(int track)
        {
            if (AudioManager.instance == null || FMODEvents.instance == null || FMODEvents.instance.RadioMusic.IsNull)
            {
                Debug.LogError("[FMOD] RadioMusic의 관리자 또는 이벤트 참조가 없습니다.", this);
                _poweredOn = false;
                return;
            }

            if (!_music.isValid())
            {
                _audioManager = AudioManager.instance;
                _music = _audioManager.CreateEventInstance(FMODEvents.instance.RadioMusic);
                if (!CheckResult(_music.set3DAttributes(RuntimeUtils.To3DAttributes(transform.position)), "음악 위치 설정"))
                {
                    ReleaseMusic();
                    _poweredOn = false;
                    return;
                }
                if (!RegisterMusicCallback())
                {
                    ReleaseMusic();
                    _poweredOn = false;
                    return;
                }
            }

            FMOD.RESULT result = _music.setParameterByName(MUSIC_PLAYER_PARAMETER, track, true);
            if (result == FMOD.RESULT.OK) result = _music.start();
            if (!CheckResult(result, $"{track}번 곡 재생"))
            {
                ReleaseMusic();
                _poweredOn = false;
                return;
            }
            _lastTrack = track;
            _activeSongSound = IntPtr.Zero;
            _poweredOn = true;
        }

        /// <summary>
        /// initialPlayback이면 일반 곡을 균등 선택하고 이후에는 직전 곡을 제외하여 0~5에서 뽑는다.
        /// lastTrack과 10% 이스터에그 확률을 사용하며 직전 곡이 5이면 일반 곡만 균등하게 반환한다.
        /// </summary>
        private int SelectNextTrack(bool initialPlayback = false)
        {
            if (initialPlayback || _lastTrack == EASTER_EGG_TRACK)
                return UnityEngine.Random.Range(0, EASTER_EGG_TRACK);
            if (UnityEngine.Random.value < EASTER_EGG_PROBABILITY) return EASTER_EGG_TRACK;
            int normalCount = _lastTrack >= 0 ? EASTER_EGG_TRACK - 1 : EASTER_EGG_TRACK;
            int choice = UnityEngine.Random.Range(0, normalCount);
            return _lastTrack >= 0 && choice >= _lastTrack ? choice + 1 : choice;
        }

        /// <summary>
        /// 전원 효과음 인스턴스를 관리 목록에서 제거하고 해제한다.
        /// stopPlayback이 참이면 먼저 재생을 멈추며 음악 인스턴스는 유지한다.
        /// </summary>
        private void ReleasePowerSound(bool stopPlayback)
        {
            if (!_powerSound.isValid()) return;
            if (_audioManager != null) _audioManager.ReleaseEventInstance(_powerSound, stopPlayback);
            else
            {
                if (stopPlayback) _powerSound.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                _powerSound.release();
            }
            _powerSound = default;
        }

        /// <summary>
        /// 음악 인스턴스를 중지·해제하여 라디오의 FMOD 참조를 비운다.
        /// AudioManager 관리 목록도 함께 갱신하며 직전 곡 번호는 유지한다.
        /// </summary>
        private void ReleaseMusic()
        {
            if (!_music.isValid()) return;
            UnregisterMusicCallback();
            if (_audioManager != null) _audioManager.ReleaseEventInstance(_music, true);
            else
            {
                _music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                _music.release();
            }
            _music = default;
        }

        /// <summary>
        /// FMOD result의 성공 여부를 확인하고 실패한 operation을 기록한다.
        /// 오류 문자열을 출력하며 호출자가 다음 재생 단계를 중단할 수 있도록 bool을 반환한다.
        /// </summary>
        private bool CheckResult(FMOD.RESULT result, string operation)
        {
            if (result == FMOD.RESULT.OK) return true;
            Debug.LogError($"[FMOD] 라디오 {operation} 실패: {result} ({FMOD.Error.String(result)}).", this);
            return false;
        }

        void OnDisable()
        {
            _outline?.Dispose();
            _outline = null;
            _turningOn = false;
            _initialTrackPending = false;
            _poweredOn = false;
            _waitingForMusicStop = false;
            _pendingTrack = -1;
            ReleasePowerSound(true);
            ReleaseMusic();
        }
    }
}
