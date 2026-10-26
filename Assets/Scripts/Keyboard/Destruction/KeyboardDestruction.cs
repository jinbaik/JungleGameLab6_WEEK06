using System;
using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

/// <summary>
/// 키보드 내 모든 키캡의 파괴 상태를 추적하여 키보드 전체 완파를 감지하고,
/// 파괴 라이프사이클 이벤트 발행 및 파편 프레젠터 연출 실행을 전담하는 컴포넌트이다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KeyboardFragmentPresenter))]
public sealed class KeyboardDestruction : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private KeyboardFragmentPresenter _fragmentPresenter;
    [SerializeField] private bool _autoInitialize;

    [Header("Runtime State")]
    private KeyboardFeedbackManager _feedbackManager;
    private List<KeycapHealth> _requiredKeycaps;
    private Action _onBreaking;
    private Action _onFinished;
    private Action<KeyboardDestruction> _configureFistAnimation;
    private int _remainingKeycaps;
    private int _requiredBrokenCount;
    private int _remainingThreshold;
    private bool _workshopHammerPending;

    public bool IsBroken { get; private set; }

    public event Action<Vector3> FistImpact;
    public event Action Breaking;
    public event Action Finished;

    void Awake()
    {
        EnsureFragmentPresenter();
        _feedbackManager = GetComponent<KeyboardFeedbackManager>() ?? FindAnyObjectByType<KeyboardFeedbackManager>();
        if (_feedbackManager != null)
        {
            _feedbackManager.FistImpact += NotifyFistImpact;
        }
    }

    void Start()
    {
        if (_autoInitialize && _requiredKeycaps == null)
        {
            AutoInitialize();
        }
    }

    void OnDestroy()
    {
        if (_feedbackManager != null)
        {
            _feedbackManager.FistImpact -= NotifyFistImpact;
            _feedbackManager = null;
        }

        if (_requiredKeycaps != null)
        {
            foreach (KeycapHealth health in _requiredKeycaps)
            {
                if (health != null)
                {
                    health.Broken -= OnKeycapBroken;
                }
            }
        }
    }

    /// <summary>
    /// 독립 씬에서 부착된 KeyboardInputController의 키캡과 파편 설정을 찾아 스스로 초기화한다.
    /// 부착된 KeyboardInputController 및 자식 키캡들을 수집하여 초기화 처리를 진행한다.
    /// 반환값은 없다.
    /// </summary>
    public void AutoInitialize()
    {
        KeyboardInputController keyboard = GetComponent<KeyboardInputController>();
        if (keyboard == null)
        {
            return;
        }

        List<KeycapHealth> destructionKeys = keyboard.GetDestructionKeycaps();
        if (destructionKeys == null || destructionKeys.Count == 0)
        {
            return;
        }

        EnsureFragmentPresenter();

        Initialize(
            destructionKeys,
            _fragmentPresenter.FragmentsPrefab,
            null,
            () => { keyboard.SetInputEnabled(false); keyboard.enabled = false; },
            null,
            null);
    }

    /// <summary>count개 파괴 시 키보드를 파괴하도록 지정하며 초기화 전 파괴 필요 수량을 저장한다.</summary>
    public void ConfigureRequiredBrokenCount(int count) { _requiredBrokenCount = Mathf.Max(1, count); }

    /// <summary>작업대 망치의 파동이 끝날 때까지 자동 완파를 보류한다. 이미 파괴된 키보드는 거부하며 보류 시작 여부를 반환한다.</summary>
    public bool BeginWorkshopHammerBreak()
    {
        if (IsBroken || _workshopHammerPending) return false;
        _workshopHammerPending = true;
        return true;
    }

    /// <summary>망치 파동 종료 시 보류한 키보드 완파와 기존 파편 연출을 시작한다. 보류 상태를 해제하며 반환값은 없다.</summary>
    public void CompleteWorkshopHammerBreak()
    {
        if (!_workshopHammerPending || IsBroken) return;
        _workshopHammerPending = false;
        StartBreaking();
    }

    /// <summary>망치 타격 중단 시 자동 완파 보류를 해제한다. 현재 키캡 체력을 기준으로 추적을 갱신하며 반환값은 없다.</summary>
    public void CancelWorkshopHammerBreak()
    {
        if (!_workshopHammerPending) return;
        _workshopHammerPending = false;
        RefreshKeycapTracking();
    }

    /// <summary>
    /// 복구 후 추적 대상 키캡의 현재 체력을 사용해 남은 수량과 파괴 이벤트 구독을 갱신한다.
    /// 초기화된 대상 목록을 사용하며 완파 연출이 시작된 키보드의 상태는 변경하지 않는다.
    /// </summary>
    public void RefreshKeycapTracking()
    {
        if (_requiredKeycaps == null || IsBroken)
            return;

        _remainingKeycaps = 0;
        foreach (KeycapHealth health in _requiredKeycaps)
        {
            health.Broken -= OnKeycapBroken;
            if (health.CurrentHP <= 0)
                continue;

            _remainingKeycaps++;
            health.Broken += OnKeycapBroken;
        }
    }

    /// <summary>
    /// 파괴 대상 키캡들과 파편 프리팹, 콜백들을 전달받아 키보드 완파 추적을 시작한다.
    /// requiredKeycaps 키캡 목록, fragmentsPrefab 파편 프리팹, fistSprite 주먹 이미지, onBreaking/onFinished 콜백, configureFistAnimation 설정 델리게이트를 사용한다.
    /// 키캡들의 Broken 이벤트를 구독하고 파편 프레젠터를 초기화한다.
    /// </summary>
    public void Initialize(
        List<KeycapHealth> requiredKeycaps,
        GameObject fragmentsPrefab,
        Sprite fistSprite,
        Action onBreaking,
        Action onFinished,
        Action<KeyboardDestruction> configureFistAnimation)
    {
        _requiredKeycaps = requiredKeycaps;
        _onBreaking = onBreaking;
        _onFinished = onFinished;
        _configureFistAnimation = configureFistAnimation;

        EnsureFragmentPresenter().Initialize(fragmentsPrefab);

        _remainingThreshold = _requiredBrokenCount > 0 ? Mathf.Max(0, _requiredKeycaps.Count - _requiredBrokenCount) : 0;
        _remainingKeycaps = 0;
        foreach (KeycapHealth health in _requiredKeycaps)
        {
            if (health.CurrentHP == 0)
            {
                continue;
            }

            _remainingKeycaps++;
            health.Broken += OnKeycapBroken;
        }

        if (_configureFistAnimation != null)
        {
            _configureFistAnimation(this);
        }
    }

    /// <summary>
    /// 파편 연출 컴포넌트가 없으면 탐색하거나 추가하여 반환한다.
    /// GetComponent 또는 AddComponent로 _fragmentPresenter를 설정한다.
    /// 보장된 KeyboardFragmentPresenter 컴포넌트를 반환한다.
    /// </summary>
    private KeyboardFragmentPresenter EnsureFragmentPresenter()
    {
        if (_fragmentPresenter == null)
        {
            _fragmentPresenter = GetComponent<KeyboardFragmentPresenter>() ?? gameObject.AddComponent<KeyboardFragmentPresenter>();
        }

        return _fragmentPresenter;
    }

    /// <summary>
    /// 키캡 파괴 알림을 수신하여 남은 키캡 수를 줄이고 설정된 잔여 수량 이하가 되면 키보드 파괴를 시작한다.
    /// health 파괴된 키캡 컴포넌트를 사용한다.
    /// _remainingKeycaps가 _remainingThreshold 이하가 되면 IsBroken을 true로 변경하고 파편 프레젠터를 실행한다.
    /// </summary>
    private void OnKeycapBroken(KeycapHealth health)
    {
        health.Broken -= OnKeycapBroken;
        _remainingKeycaps--;

        if (_remainingKeycaps > _remainingThreshold || IsBroken || _workshopHammerPending)
        {
            return;
        }

        StartBreaking();
    }

    /// <summary>현재 키보드를 파괴 상태로 전환하고 기존 파편 프레젠터를 시작한다. Breaking과 완료 콜백 상태를 갱신하며 반환값은 없다.</summary>
    private void StartBreaking()
    {
        IsBroken = true;
        _onBreaking?.Invoke();
        Breaking?.Invoke();

        if (_fragmentPresenter != null)
        {
            _fragmentPresenter.PlayFragments(HandleFinished);
        }
        else
        {
            HandleFinished();
        }
    }

    /// <summary>
    /// 파편 연출이 완료된 후 최종 종료 콜백과 Finished 이벤트를 발행한다.
    /// _onFinished 콜백을 호출하고 Finished 이벤트를 알린다.
    /// 반환값은 없다.
    /// </summary>
    private void HandleFinished()
    {
        _onFinished?.Invoke();
        Finished?.Invoke();
    }

    /// <summary>
    /// 피버 교체 등에서 생성된 파편의 물리 충돌을 즉시 비활성화하도록 프레젠터에 위임한다.
    /// _fragmentPresenter의 DisableFragmentCollisions를 호출한다.
    /// 반환값은 없다.
    /// </summary>
    public void DisableFragmentCollisions()
    {
        if (_fragmentPresenter != null)
        {
            _fragmentPresenter.DisableFragmentCollisions();
        }
    }

    /// <summary>
    /// 기존 호환성을 위해 주먹 연출 설정을 피드백 매니저에 전달한다.
    /// fistHeight, fistLift, appearDuration, strikeDuration, retreatDuration, fistImpactOffset, fistMaterial을 피드백 매니저에 위임한다.
    /// 반환값은 없다.
    /// </summary>
    public void ConfigureFistAnimation(float fistHeight, float fistLift, float appearDuration, float strikeDuration, float retreatDuration, Vector3 fistImpactOffset, Material fistMaterial)
    {
        if (_feedbackManager == null)
        {
            _feedbackManager = GetComponent<KeyboardFeedbackManager>() ?? FindAnyObjectByType<KeyboardFeedbackManager>();
            if (_feedbackManager != null)
            {
                _feedbackManager.FistImpact += NotifyFistImpact;
            }
        }

        if (_feedbackManager != null)
        {
            _feedbackManager.ConfigureFistAnimation(fistHeight, fistLift, appearDuration, strikeDuration, retreatDuration, fistImpactOffset, fistMaterial);
        }
    }

    /// <summary>
    /// 기존 호환성을 위해 망치 연출 설정을 피드백 매니저에 전달한다.
    /// sprite, settings, shockwaveMaterial을 피드백 매니저에 위임한다.
    /// 반환값은 없다.
    /// </summary>
    public void ConfigureHammerAnimation(Sprite sprite, HammerAnimationSettings settings, Material shockwaveMaterial)
    {
        if (_feedbackManager == null)
        {
            _feedbackManager = GetComponent<KeyboardFeedbackManager>() ?? FindAnyObjectByType<KeyboardFeedbackManager>();
            if (_feedbackManager != null)
            {
                _feedbackManager.FistImpact += NotifyFistImpact;
            }
        }

        if (_feedbackManager != null)
        {
            _feedbackManager.ConfigureHammerAnimation(sprite, settings, shockwaveMaterial);
        }
    }

    /// <summary>
    /// 하위 호환성을 위해 FistImpact 이벤트를 내부에서 발행할 수 있도록 지원한다.
    /// position 타격 위치 좌표를 매개변수로 받는다.
    /// FistImpact 구독자에게 전달한다.
    /// </summary>
    public void NotifyFistImpact(Vector3 position)
    {
        FistImpact?.Invoke(position);
    }

    [Serializable]
    public sealed class HammerAnimationSettings : KeyboardFeedbackManager.HammerAnimationSettings
    {
    }
}
