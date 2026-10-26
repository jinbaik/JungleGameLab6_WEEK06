using System;
using System.Collections.Generic;

using UnityEngine;

using FMOD.Studio;
using FMODUnity;

using Game.Player;

public class AudioManager : MonoBehaviour
{
    private static bool _reportedMissingPartAudio;
    public static event Action<AudioManager> Ready;

    [Header("Plate Audio Selection")]
    [SerializeField] private KeyboardCustomization _initialKeyboard;
    private KeyboardCustomization _activeKeyboard;
    private int _selectionVersion;

    [Header("FMOD Effect Values")]
    [Tooltip("X는 원음, Y는 효과 적용 값입니다. Bank의 Wet Mix 자동화와 일치해야 합니다.")]
    [SerializeField] private Vector2 _reverbOffOn = new Vector2(0f, 1f);
    [SerializeField] private Vector2 _delayOffOn = new Vector2(0f, 1f);
    [SerializeField] private Vector2 _pitchOffOn = new Vector2(0f, 1f);
    private bool _isQuitting;

    [Header("Temporary Keyboard Event Pitch")]
    [Tooltip("피치 효과를 선택한 타건 이벤트에 직접 적용하는 기존 음정 배율입니다.")]
    [SerializeField, Min(0.01f)] private float _temporaryKeyboardPitch = 1.2f;
    private float _keyboardEventPitch = 1f;

    [Header("Part Removal Sound")]
    [SerializeField] private Vector2 _removeKeyPitchRange = new Vector2(0.9f, 1.1f);

    [Header("Event Instances")]
    private List<EventInstance> eventInstances;

    [Header("Indoor Ambient Runtime")]
    private EventInstance _indoorAmbient;
    private bool _ambientRequested;
    private bool _ambientIsOneshot;
    private bool _ambientFailed;
    private bool _ambientStopped = true;
    private float _ambientVolume;
    private float _ambientTargetVolume;
    private float _ambientFadeDuration = 1f;

    public static AudioManager instance { get; private set; }

    /// <summary>
    /// 새 플레이 세션에서 이전 싱글턴과 준비 이벤트를 초기화한다.
    /// 입력 없이 도메인 재로드를 생략한 실행의 이전 참조도 제거한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        instance = null;
        Ready = null;
        _reportedMissingPartAudio = false;
    }

    void Awake()
    {
        if (instance != null) Debug.LogError("Found more than one Audio Manager in the scene", this);
        instance = this;
        eventInstances = new List<EventInstance>();
    }

    void OnEnable()
    {
        if (instance != this) return;
        KeyboardRequestRuntime.EmailNotificationRequested += PlayEmailNotification;
        Ready?.Invoke(this);
        if (_selectionVersion == 0 && _initialKeyboard != null) SelectKeyboardEffects(_initialKeyboard);
    }

    void OnDisable()
    {
        KeyboardRequestRuntime.EmailNotificationRequested -= PlayEmailNotification;
        if (_activeKeyboard != null) ApplyPlateEffects(PlateEffect.None);
        DetachPlate();
    }

    void OnApplicationQuit()
    {
        _isQuitting = true;
    }

    void Update()
    {
        if (!_indoorAmbient.isValid() || _ambientFailed || _isQuitting || _ambientStopped) return;

        float target = _ambientRequested ? _ambientTargetVolume : 0f;
        _ambientVolume = Mathf.MoveTowards(_ambientVolume, target,
            Time.deltaTime * Mathf.Max(_ambientTargetVolume, 0.0001f) / _ambientFadeDuration);
        if (!CheckAmbientResult(_indoorAmbient.setVolume(_ambientVolume), "볼륨 적용")) return;
        if (!CheckAmbientResult(_indoorAmbient.getPlaybackState(out PLAYBACK_STATE state), "상태 조회")) return;

        if (!_ambientRequested && _ambientVolume <= 0f)
        {
            if (state != PLAYBACK_STATE.STOPPED)
                CheckAmbientResult(_indoorAmbient.stop(FMOD.Studio.STOP_MODE.IMMEDIATE), "정지");
            _ambientStopped = true;
            return;
        }

        if (_ambientRequested && _ambientIsOneshot && state == PLAYBACK_STATE.STOPPED)
            CheckAmbientResult(_indoorAmbient.start(), "반복 재생");
    }

    /// <summary>
    /// IndoorAmbient 인스턴스 하나를 생성하거나 재사용하여 실내 배경음을 시작한다.
    /// volume과 fadeDuration으로 목표 음량과 페이드 시간을 설정하며 키보드 효과와 독립적으로 재생한다.
    /// </summary>
    public void PlayIndoorAmbient(float volume, float fadeDuration)
    {
        _ambientFailed = false;
        _ambientTargetVolume = Mathf.Clamp01(volume);
        _ambientFadeDuration = Mathf.Max(0.01f, fadeDuration);
        if (!_indoorAmbient.isValid())
        {
            _indoorAmbient = CreateEventInstance(FMODEvents.instance.IndoorAmbient);
            if (!CheckAmbientResult(_indoorAmbient.getDescription(out EventDescription description), "이벤트 조회")) return;
            if (!CheckAmbientResult(description.isOneshot(out _ambientIsOneshot), "반복 설정 조회")) return;
        }

        if (!CheckAmbientResult(_indoorAmbient.setVolume(_ambientVolume), "초기 음량 적용")) return;
        if (!CheckAmbientResult(_indoorAmbient.getPlaybackState(out PLAYBACK_STATE state), "상태 조회")) return;
        if (state == PLAYBACK_STATE.STOPPED && !CheckAmbientResult(_indoorAmbient.start(), "재생")) return;
        _ambientRequested = true;
        _ambientStopped = false;
    }

    /// <summary>
    /// 실내 배경음의 재생 요청을 해제하여 현재 페이드 시간에 걸쳐 음량을 줄이고 정지한다.
    /// 입력과 반환값 없이 관리 중인 인스턴스의 목표 음량을 무음으로 변경한다.
    /// </summary>
    public void StopIndoorAmbient()
    {
        _ambientRequested = false;
    }

    /// <summary>
    /// 실내 배경음 작업의 result를 확인하고 성공 여부를 반환한다.
    /// 실패하면 operation 이름과 FMOD 오류를 출력하고 반복 시도를 중단한다.
    /// </summary>
    private bool CheckAmbientResult(FMOD.RESULT result, string operation)
    {
        if (result == FMOD.RESULT.OK) return true;
        _ambientFailed = true;
        _ambientRequested = false;
        Debug.LogError($"[FMOD] IndoorAmbient {operation} 실패: {result} ({FMOD.Error.String(result)}).", this);
        return false;
    }

    /// <summary>
    /// keyboard를 전역 효과 제어 대상으로 선택하고 새 선택 번호를 전달한다.
    /// 이전 구독을 해제하며 null 입력이면 세 효과를 원음 상태로 변경한다.
    /// </summary>
    public void SelectKeyboardEffects(KeyboardCustomization keyboard)
    {
        if (!Application.isPlaying || instance != this || !isActiveAndEnabled) return;
        if (keyboard != null && (!keyboard.isActiveAndEnabled || !Application.IsPlaying(keyboard.gameObject))) return;
        DetachPlate();
        _selectionVersion++;
        if (keyboard == null) { ApplyPlateEffects(PlateEffect.None); return; }
        keyboard.RememberPlateAudioSelection(this, _selectionVersion);
        ResumeKeyboardEffects(keyboard, _selectionVersion);
    }

    /// <summary>
    /// keyboard와 selectionVersion이 최신 선택이면 독립 효과 변경 이벤트를 구독한다.
    /// 현재 효과를 즉시 적용하여 동일 선택의 재활성화에도 효과를 복원한다.
    /// </summary>
    public void ResumeKeyboardEffects(KeyboardCustomization keyboard, int selectionVersion)
    {
        if (selectionVersion == 0)
        {
            if (_selectionVersion == 0 && _initialKeyboard == keyboard) SelectKeyboardEffects(keyboard);
            return;
        }
        if (!Application.isPlaying || instance != this || !isActiveAndEnabled ||
            selectionVersion != _selectionVersion || !keyboard.isActiveAndEnabled ||
            !Application.IsPlaying(keyboard.gameObject)) return;
        DetachPlate();
        _activeKeyboard = keyboard;
        _activeKeyboard.PlateEffectChanged += HandlePlateEffectChanged;
        HandlePlateEffectChanged(_activeKeyboard.CurrentPlateEffect);
    }

    /// <summary>
    /// keyboard가 현재 대상이면 구독과 활성 참조를 해제하고 원음으로 복귀한다.
    /// 선택 번호를 유지하여 다른 선택이 없는 경우에만 재활성화로 복원한다.
    /// </summary>
    public void SuspendKeyboardEffects(KeyboardCustomization keyboard)
    {
        if (_activeKeyboard != keyboard) return;
        DetachPlate();
        ApplyPlateEffects(PlateEffect.None);
    }

    /// <summary>
    /// 사용이 끝난 keyboard의 효과를 해제하고 선택 번호를 무효화한다.
    /// 이전 키보드가 재활성화되어도 현재 효과를 덮어쓰지 않게 한다.
    /// </summary>
    public void ReleaseKeyboardEffects(KeyboardCustomization keyboard)
    {
        if (keyboard == null || _activeKeyboard != keyboard) return;
        SuspendKeyboardEffects(keyboard);
        _selectionVersion++;
    }

    /// <summary>
    /// 현재 플레이트 이벤트를 구독 해제하고 활성 참조를 비운다.
    /// 입력과 반환값 없이 이전 키보드의 효과 제어를 해제한다.
    /// </summary>
    private void DetachPlate()
    {
        if (_activeKeyboard != null) _activeKeyboard.PlateEffectChanged -= HandlePlateEffectChanged;
        _activeKeyboard = null;
    }

    /// <summary>
    /// 활성 키보드에서 선택한 effect를 오디오 설정으로 전달한다.
    /// 비활성 키보드의 알림을 무시하며 재질과 기존 보강판 타입은 조회하지 않는다.
    /// </summary>
    private void HandlePlateEffectChanged(PlateEffect effect)
    {
        if (_activeKeyboard == null || !_activeKeyboard.isActiveAndEnabled) return;
        ApplyPlateEffects(effect);
    }

    /// <summary>
    /// effect에 대응하는 단일 효과를 선택하고 이벤트 음정 배율을 변경한다.
    /// 기존 Pitch Shifter 꺼짐 값을 유지하며 효과 없음에서는 이벤트 배율을 1로 복원한다.
    /// </summary>
    private void ApplyPlateEffects(PlateEffect effect)
    {
        if (!Application.isPlaying || _isQuitting) return;
        _keyboardEventPitch = effect == PlateEffect.Pitch ? _temporaryKeyboardPitch : 1f;
        SetKeyboardEffects(
            effect == PlateEffect.Reverb ? _reverbOffOn.y : _reverbOffOn.x,
            effect == PlateEffect.Delay ? _delayOffOn.y : _delayOffOn.x,
            _pitchOffOn.x);
    }

    /// <summary>
    /// reverbAmount, delayAmount, pitchAmount를 로드된 Bank 정의와 대조한다.
    /// 모두 유효하면 전역 파라미터를 즉시 변경하고 실패 시 원인을 출력한다.
    /// </summary>
    public void SetKeyboardEffects(float reverbAmount, float delayAmount, float pitchAmount)
    {
        if (!Application.isPlaying || _isQuitting) return;
        bool reverbValid = ValidateEffectValue("ReverbAmount", reverbAmount);
        bool delayValid = ValidateEffectValue("DelayAmount", delayAmount);
        bool pitchValid = ValidateEffectValue("PitchAmount", pitchAmount);
        if (!reverbValid || !delayValid || !pitchValid) return;
        SetEffectParameter("ReverbAmount", reverbAmount);
        SetEffectParameter("DelayAmount", delayAmount);
        SetEffectParameter("PitchAmount", pitchAmount);
    }

    /// <summary>
    /// name이 쓰기 가능한 전역 파라미터인지 실제 Bank에서 조회한다.
    /// value의 범위 검사 결과를 반환하고 잘못된 설정은 오류로 알린다.
    /// </summary>
    private bool ValidateEffectValue(string name, float value)
    {
        FMOD.RESULT result = RuntimeManager.StudioSystem.getParameterDescriptionByName(name, out PARAMETER_DESCRIPTION description);
        if (result != FMOD.RESULT.OK)
        {
            Debug.LogError($"[FMOD] {name} 조회 실패: {result} ({FMOD.Error.String(result)}).", this);
            return false;
        }
        if ((description.flags & PARAMETER_FLAGS.GLOBAL) == 0 ||
            (description.flags & (PARAMETER_FLAGS.READONLY | PARAMETER_FLAGS.AUTOMATIC | PARAMETER_FLAGS.LABELED)) != 0 ||
            float.IsNaN(value) || float.IsInfinity(value) || value < description.minimum || value > description.maximum)
        {
            Debug.LogError($"[FMOD] {name}={value} 적용 불가: flags={description.flags}, 범위={description.minimum}~{description.maximum}.", this);
            return false;
        }
        return true;
    }

    /// <summary>
    /// name의 전역 파라미터를 value로 즉시 변경한다.
    /// FMOD 반환값을 확인하여 실패한 설정을 오류로 출력한다.
    /// </summary>
    private void SetEffectParameter(string name, float value)
    {
        FMOD.RESULT result = RuntimeManager.StudioSystem.setParameterByName(name, value, true);
        if (result != FMOD.RESULT.OK)
            Debug.LogError($"[FMOD] {name}={value} 설정 실패: {result} ({FMOD.Error.String(result)}).", this);
    }

    /// <summary>
    /// sound를 worldPos에서 한 번 재생한다.
    /// 재생이 끝난 인스턴스의 해제는 RuntimeManager가 처리한다.
    /// </summary>
    public void PlayOneShot(EventReference sound, Vector3 worldPos)
    {
        RuntimeManager.PlayOneShot(sound, worldPos);
    }

    /// <summary>
    /// FMODEvents의 Button 이벤트를 화면 UI 클릭음으로 한 번 재생한다.
    /// 입력과 반환값 없이 위치와 키보드 음정 효과에 영향을 받지 않는 UI 이벤트를 시작한다.
    /// </summary>
    public void PlayButtonSound()
    {
        RuntimeManager.PlayOneShot(FMODEvents.instance.Button);
    }

    /// <summary>
    /// opening에 따라 기존 DrawerOpen 또는 DrawerClose 2D 이벤트를 한 번 재생한다.
    /// 관리자나 이벤트 참조가 없으면 경고하며 서랍 전환과 재고 상태는 변경하지 않는다.
    /// </summary>
    public static void PlayDrawerSound(bool opening)
    {
        if (instance == null || FMODEvents.instance == null)
        {
            Debug.LogWarning("[FMOD] 서랍 효과음의 AudioManager 또는 FMODEvents가 없습니다.");
            return;
        }
        EventReference sound = opening ? FMODEvents.instance.DrawerOpen : FMODEvents.instance.DrawerClose;
        if (sound.IsNull)
        {
            Debug.LogWarning($"[FMOD] FmodManager의 {(opening ? "DrawerOpen" : "DrawerClose")} 참조가 비어 있습니다.", instance);
            return;
        }
        instance.PlayOneShot(sound, Vector3.zero);
    }

    /// <summary>
    /// 실제 메일 수신 또는 새 게임의 시작 알림에 기존 Email 이벤트를 한 번 재생한다.
    /// 입력과 반환값 없이 FMODEvents의 UI 효과음을 시작하며 메일함 상태는 변경하지 않는다.
    /// </summary>
    private void PlayEmailNotification()
    {
        if (FMODEvents.instance == null || FMODEvents.instance.Email.IsNull)
        {
            Debug.LogWarning("[FMOD] FmodManager의 Email 이벤트 참조가 비어 있습니다.", this);
            return;
        }
        RuntimeManager.PlayOneShot(FMODEvents.instance.Email);
    }

    /// <summary>
    /// worldPosition에서 설치 또는 제거 이벤트를 한 번 재생한다.
    /// removing이 참이면 인스턴스별 미세 피치를 적용하며 누락 및 실패는 기록하고 조립 처리는 유지한다.
    /// </summary>
    public static void PlayPartSound(Vector3 worldPosition, bool removing)
    {
        if (!Application.isPlaying) return;
        if (instance == null || FMODEvents.instance == null)
        {
            if (!_reportedMissingPartAudio)
                Debug.LogWarning("[FMOD] 조립 효과음에 AudioManager와 FMODEvents 연결이 필요합니다.");
            _reportedMissingPartAudio = true;
            return;
        }

        EventReference sound = removing ? FMODEvents.instance.RemoveKey : FMODEvents.instance.PutOnKey;
        if (sound.IsNull)
        {
            if (!_reportedMissingPartAudio)
                Debug.LogWarning($"[FMOD] FmodManager의 {(removing ? "RemoveKey" : "PutOnKey")} 참조가 비어 있습니다.", instance);
            _reportedMissingPartAudio = true;
            return;
        }

        EventInstance partSound = default;
        try
        {
            partSound = RuntimeManager.CreateInstance(sound);
            FMOD.RESULT result = partSound.set3DAttributes(RuntimeUtils.To3DAttributes(worldPosition));
            if (result == FMOD.RESULT.OK && removing)
            {
                Vector2 range = instance._removeKeyPitchRange;
                float low = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
                float high = Mathf.Max(low, Mathf.Max(range.x, range.y));
                result = partSound.setPitch(UnityEngine.Random.Range(low, high));
            }
            if (result == FMOD.RESULT.OK) result = partSound.start();
            if (result != FMOD.RESULT.OK)
                Debug.LogWarning($"[FMOD] 조립 효과음 재생 실패: {result} ({FMOD.Error.String(result)}).", instance);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[FMOD] 조립 효과음 생성 실패: {exception.Message}", instance);
        }
        finally
        {
            if (partSound.isValid())
            {
                FMOD.RESULT result = partSound.release();
                if (result != FMOD.RESULT.OK)
                    Debug.LogWarning($"[FMOD] 조립 효과음 해제 예약 실패: {result}.", instance);
            }
        }
    }

    /// <summary>
    /// sound를 worldPos에서 재생하며 선택된 keyboard의 타건에만 이벤트 음정 배율을 적용한다.
    /// FMOD 설정과 재생 결과를 확인하고 인스턴스를 해제 예약하여 재생 종료 후 정리한다.
    /// </summary>
    public void PlayKeyboardOneShot(EventReference sound, Vector3 worldPos, GameObject keyboard)
    {
        EventInstance eventInstance = RuntimeManager.CreateInstance(sound);
        float pitch = _activeKeyboard != null && _activeKeyboard.gameObject == keyboard ? _keyboardEventPitch : 1f;
        try
        {
            FMOD.RESULT result = eventInstance.set3DAttributes(RuntimeUtils.To3DAttributes(worldPos));
            if (result == FMOD.RESULT.OK) result = eventInstance.setPitch(pitch);
            if (result == FMOD.RESULT.OK) result = eventInstance.start();
            if (result != FMOD.RESULT.OK)
                Debug.LogError($"[FMOD] 타건 이벤트 재생 실패: pitch={pitch}, {result} ({FMOD.Error.String(result)}).", this);
        }
        finally
        {
            FMOD.RESULT result = eventInstance.release();
            if (result != FMOD.RESULT.OK)
                Debug.LogError($"[FMOD] 타건 이벤트 해제 실패: {result} ({FMOD.Error.String(result)}).", this);
        }
    }

    /// <summary>
    /// eventReference로 제어 가능한 이벤트 인스턴스를 생성해 반환한다.
    /// 생성한 인스턴스를 목록에 보관하여 관리자 파괴 시 해제한다.
    /// </summary>
    public EventInstance CreateEventInstance(EventReference eventReference)
    {
        EventInstance eventInstance = RuntimeManager.CreateInstance(eventReference);
        eventInstances.Add(eventInstance);
        return eventInstance;
    }

    /// <summary>
    /// eventInstance를 관리 목록에서 제거하고 필요하면 재생을 정지한 뒤 해제한다.
    /// stopPlayback이 참이면 즉시 정지하며, FMOD 실패 여부를 기록하고 등록 상태를 해제한다.
    /// </summary>
    public void ReleaseEventInstance(EventInstance eventInstance, bool stopPlayback)
    {
        if (!eventInstances.Remove(eventInstance) || !eventInstance.isValid()) return;
        if (stopPlayback)
        {
            FMOD.RESULT stopResult = eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            if (stopResult != FMOD.RESULT.OK)
                Debug.LogWarning($"[FMOD] 관리 이벤트 정지 실패: {stopResult}.", this);
        }
        FMOD.RESULT releaseResult = eventInstance.release();
        if (releaseResult != FMOD.RESULT.OK)
            Debug.LogWarning($"[FMOD] 관리 이벤트 해제 실패: {releaseResult}.", this);
    }

    /// <summary>
    /// 보관된 모든 이벤트의 재생을 중단하고 인스턴스를 해제한다.
    /// 입력과 반환값 없이 관리자에서 생성한 인스턴스 목록을 비운다.
    /// </summary>
    private void CleanUp()
    {
        foreach (EventInstance eventInstance in eventInstances)
        {
            eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            eventInstance.release();
        }
        eventInstances.Clear();
    }

    void OnDestroy()
    {
        DetachPlate();
        if (instance == this) instance = null;
        CleanUp();
    }
}

