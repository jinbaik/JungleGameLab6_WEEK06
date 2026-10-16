using System;
using System.Collections.Generic;

using UnityEngine;

using FMOD.Studio;
using FMODUnity;

public class AudioManager : MonoBehaviour
{
    public static event Action<AudioManager> Ready;

    [Header("Plate Audio Selection")]
    [SerializeField] private KeyboardCustomization _initialKeyboard;
    private KeyboardCustomization _activeKeyboard;
    private KeyboardPlate _activePlate;
    private int _selectionVersion;

    [Header("FMOD Effect Values")]
    [Tooltip("X는 원음, Y는 효과 적용 값입니다. Bank의 Wet Mix 자동화와 일치해야 합니다.")]
    [SerializeField] private Vector2 _reverbOffOn = new Vector2(0f, 1f);
    [SerializeField] private Vector2 _delayOffOn = new Vector2(0f, 1f);
    [SerializeField] private Vector2 _pitchOffOn = new Vector2(0f, 1f);
    private bool _isQuitting;

    [Header("Event Instances")]
    private List<EventInstance> eventInstances;

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
        Ready?.Invoke(this);
        if (_selectionVersion == 0 && _initialKeyboard != null) SelectKeyboardEffects(_initialKeyboard);
    }

    void OnDisable()
    {
        if (_activeKeyboard != null) ApplyPlateEffects(PlateType.Nomal);
        DetachPlate();
    }

    void OnApplicationQuit()
    {
        _isQuitting = true;
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
        if (keyboard == null) { ApplyPlateEffects(PlateType.Nomal); return; }
        keyboard.RememberPlateAudioSelection(this, _selectionVersion);
        ResumeKeyboardEffects(keyboard, _selectionVersion);
    }

    /// <summary>
    /// keyboard와 selectionVersion이 최신 선택이면 PlateChanged를 구독한다.
    /// CurrentData를 즉시 적용하여 동일 플레이트의 재활성화에도 효과를 복원한다.
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
        _activePlate = keyboard.Plate;
        if (_activePlate == null) return;
        _activePlate.PlateChanged += HandlePlateChanged;
        HandlePlateChanged(_activePlate.CurrentData);
    }

    /// <summary>
    /// keyboard가 현재 대상이면 구독과 활성 참조를 해제하고 원음으로 복귀한다.
    /// 선택 번호를 유지하여 다른 선택이 없는 경우에만 재활성화로 복원한다.
    /// </summary>
    public void SuspendKeyboardEffects(KeyboardCustomization keyboard)
    {
        if (_activeKeyboard != keyboard) return;
        DetachPlate();
        ApplyPlateEffects(PlateType.Nomal);
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
        if (_activePlate != null) _activePlate.PlateChanged -= HandlePlateChanged;
        _activePlate = null;
        _activeKeyboard = null;
    }

    /// <summary>
    /// 활성 키보드의 data를 효과 설정으로 전달한다.
    /// 데이터가 없으면 Nomal을 적용하며 비활성 인스턴스는 무시한다.
    /// </summary>
    private void HandlePlateChanged(PlateData data)
    {
        if (_activeKeyboard == null || !_activeKeyboard.isActiveAndEnabled) return;
        ApplyPlateEffects(data != null ? data.Type : PlateType.Nomal);
    }

    /// <summary>
    /// type에 대응하는 효과만 선택하며 RainBow에는 피치만 적용한다.
    /// 세 전역 파라미터를 함께 변경하여 이전 플레이트 효과를 해제한다.
    /// </summary>
    private void ApplyPlateEffects(PlateType type)
    {
        if (!Application.isPlaying || _isQuitting) return;
        SetKeyboardEffects(
            type == PlateType.UnderWater ? _reverbOffOn.y : _reverbOffOn.x,
            type == PlateType.Golden ? _delayOffOn.y : _delayOffOn.x,
            type == PlateType.RainBow ? _pitchOffOn.y : _pitchOffOn.x);
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
