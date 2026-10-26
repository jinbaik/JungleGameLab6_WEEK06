using System;
using System.Collections.Generic;

using UnityEngine;

[DisallowMultipleComponent]
public sealed class KeyboardCompletionProgress : MonoBehaviour
{
    private const int REWARD_KEYBOARD_COUNT = 3;

    [Header("References")]
    [SerializeField] private KeyboardInteractionController _interactionController;
    [SerializeField] private BackgroundKeyboardThrowSpawner _spawner;
    [SerializeField] private BackgroundKeyboardThrowVisit _throwVisit;
    [SerializeField] private Transform _door;

    [Header("First Region Reward")]
    [SerializeField, InspectorName("첫 번째 구역 보스 키보드 프리팹")] private GameObject _firstRegionRewardPrefab;
    [SerializeField, Min(1), InspectorName("첫 번째 구역 보스 키보드 개수")] private int _firstRegionRewardCount = 3;

    [Header("Second Region Reward")]
    [Tooltip("두 번째 구역 완료 후 보스가 던질 키보드 프리팹입니다. 비우면 보스의 기본 프리팹을 사용합니다.")]
    [SerializeField, InspectorName("두 번째 구역 보스 키보드 프리팹")] private GameObject _secondRegionRewardPrefab;
    [SerializeField, Min(1), InspectorName("두 번째 구역 보스 키보드 개수")] private int _secondRegionRewardCount = 3;
    private int _requiredRewardKeyboardCount = REWARD_KEYBOARD_COUNT;

    [Header("Door Opening")]
    [SerializeField] private float _doorOpenAngle = 90f;
    private Quaternion _closedDoorRotation;

    [Header("Completion Progress")]
    [InspectorName("금 키보드 생성에 필요한 파괴 개수")]
    [Tooltip("초기 키보드를 이 개수만큼 파괴하면 보상 금 키보드 세 개를 던집니다. 초기 생성 개수 이하로 설정하세요.")]
    [SerializeField, Min(1)] private int _requiredInitialKeyboardCount = 100;
    private readonly HashSet<int> _initialKeyboardIds = new HashSet<int>();
    private readonly HashSet<int> _rewardKeyboardIds = new HashSet<int>();
    private readonly HashSet<int> _completedKeyboardIds = new HashSet<int>();
    private int _nextProgressId;
    private int _completedInitialKeyboardCount;
    private int _completedRewardKeyboardCount;
    private bool _rewardsRequested;
    private bool _isDoorOpen;
    public int InitialKeyboardCount => _initialKeyboardIds.Count;
    public int CompletedInitialKeyboardCount => _completedInitialKeyboardCount;
    public int RequiredInitialKeyboardCount => Mathf.Min(_requiredInitialKeyboardCount, Mathf.Max(1, InitialKeyboardCount));
    public bool RewardsRequested => _rewardsRequested;
    public int RewardKeyboardCount => _rewardKeyboardIds.Count;
    public int CompletedRewardKeyboardCount => _completedRewardKeyboardCount;
    public int RequiredRewardKeyboardCount => _rewardsRequested ? _requiredRewardKeyboardCount
        : _interactionController.ActiveSpawnRegion == 0 ? Mathf.Max(1, _firstRegionRewardCount)
        : _interactionController.ActiveSpawnRegion == 1 ? Mathf.Max(1, _secondRegionRewardCount) : REWARD_KEYBOARD_COUNT;
    public bool IsDoorOpen => _isDoorOpen;
    public event Action ProgressChanged;

    void Awake()
    {
        _closedDoorRotation = _door.localRotation;
    }

    void OnEnable()
    {
        _interactionController.InitialKeyboardSpawned += RegisterInitialKeyboard;
        _interactionController.KeyboardCompleted += HandleKeyboardCompleted;
        _spawner.RewardKeyboardSpawned += RegisterRewardKeyboard;
    }

    void OnDisable()
    {
        _interactionController.InitialKeyboardSpawned -= RegisterInitialKeyboard;
        _interactionController.KeyboardCompleted -= HandleKeyboardCompleted;
        _spawner.RewardKeyboardSpawned -= RegisterRewardKeyboard;
    }

    void OnValidate()
    {
        _requiredInitialKeyboardCount = Mathf.Max(1, _requiredInitialKeyboardCount);
        _doorOpenAngle = _doorOpenAngle < 0f ? -90f : 90f;
    }

    /// <summary>
    /// 다음 구역의 door를 완료 대상으로 지정하고 해당 구역의 초기 및 보상 집계를 비운다.
    /// door가 없는 마지막 구역은 문 회전 없이 완료 상태만 기록한다.
    /// </summary>
    public void BeginRegion(Transform door)
    {
        _door = door;
        _closedDoorRotation = door != null ? door.localRotation : Quaternion.identity;
        _initialKeyboardIds.Clear();
        _rewardKeyboardIds.Clear();
        _completedKeyboardIds.Clear();
        _completedInitialKeyboardCount = 0;
        _completedRewardKeyboardCount = 0;
        _rewardsRequested = false;
        _requiredRewardKeyboardCount = REWARD_KEYBOARD_COUNT;
        _isDoorOpen = false;
        ProgressChanged?.Invoke();
    }

    /// <summary>
    /// 생성된 state에 고유 진행 식별자를 부여하고 초기 키보드 목록에 등록한다.
    /// 천장 생성 이벤트의 상태를 사용하며 시작용 키보드와 보상 키보드는 포함하지 않는다.
    /// </summary>
    private void RegisterInitialKeyboard(KeyboardInteractionController.KeyboardState state)
    {
        if (state.ProgressId >= 0) return;
        state.SetProgressId(_nextProgressId++);
        _initialKeyboardIds.Add(state.ProgressId);
    }

    /// <summary>
    /// 보상 투척으로 생성된 state에 식별자를 부여하고 보상 키보드 목록에 등록한다.
    /// 보상 요청 상태와 설정된 개수를 사용하여 문 개방 대상으로 추적한다.
    /// </summary>
    private void RegisterRewardKeyboard(KeyboardInteractionController.KeyboardState state)
    {
        if (!_rewardsRequested || _rewardKeyboardIds.Count >= _requiredRewardKeyboardCount || state.ProgressId >= 0) return;
        state.SetProgressId(_nextProgressId++);
        _rewardKeyboardIds.Add(state.ProgressId);
        ProgressChanged?.Invoke();
    }

    /// <summary>
    /// 완료된 regionIndex에 보스 보상 투척을 한 번 요청한다.
    /// 첫 번째와 두 번째 구역은 각각 Inspector의 프리팹과 개수를 사용한다.
    /// 이 투척으로 생성된 키보드를 문 개방 집계 대상으로 연결하고 전체 완료의 중복 요청을 막는다.
    /// </summary>
    public void RequestRegionRewards(int regionIndex)
    {
        if (_rewardsRequested) return;
        _rewardsRequested = true;
        _requiredRewardKeyboardCount = regionIndex == 0 ? Mathf.Max(1, _firstRegionRewardCount)
            : regionIndex == 1 ? Mathf.Max(1, _secondRegionRewardCount) : REWARD_KEYBOARD_COUNT;
        GameObject prefab = regionIndex == 0 ? _firstRegionRewardPrefab : regionIndex == 1 ? _secondRegionRewardPrefab : null;
        _throwVisit.RequestRewardThrows(regionIndex, _requiredRewardKeyboardCount, prefab);
        ProgressChanged?.Invoke();
    }

    /// <summary>
    /// 파괴 완료된 state의 식별자를 초기 또는 보상 진행에 한 번만 반영한다.
    /// 초기 파괴 목표를 달성하면 미요청 보상을 투척하고 설정된 보상 키보드가 모두 파괴되면 문을 개방한다.
    /// </summary>
    private void HandleKeyboardCompleted(KeyboardInteractionController.KeyboardState state)
    {
        int progressId = state.ProgressId;
        bool isInitial = _initialKeyboardIds.Contains(progressId);
        bool isReward = _rewardKeyboardIds.Contains(progressId);
        if ((!isInitial && !isReward) || !_completedKeyboardIds.Add(progressId)) return;

        if (isInitial)
        {
            _completedInitialKeyboardCount++;
            if (!_rewardsRequested && _completedInitialKeyboardCount >= RequiredInitialKeyboardCount)
            {
                RequestRegionRewards(state.SpawnRegionIndex >= 0 ? state.SpawnRegionIndex : 0);
            }
            ProgressChanged?.Invoke();
            return;
        }

        _completedRewardKeyboardCount++;
        if (_rewardKeyboardIds.Count == _requiredRewardKeyboardCount && _completedRewardKeyboardCount == _requiredRewardKeyboardCount)
            OpenDoor();
        ProgressChanged?.Invoke();
    }

    /// <summary>
    /// 저장한 문 시작 회전과 _doorOpenAngle을 사용해 로컬 Y축으로 문을 한 번 개방한다.
    /// 문 Transform의 회전을 변경하고 중복 개방을 막는 상태를 저장한다.
    /// </summary>
    private void OpenDoor()
    {
        if (_isDoorOpen) return;
        if (_door != null)
            _door.localRotation = _closedDoorRotation * Quaternion.Euler(0f, _doorOpenAngle, 0f);
        _isDoorOpen = true;
    }

    /// <summary>
    /// 다음 구역 진입 후 문을 Awake에서 기록한 닫힌 회전으로 복원한다.
    /// 완료 기록을 유지하고 문 개방 상태만 해제한다.
    /// </summary>
    public void CloseDoor()
    {
        if (_door != null) _door.localRotation = _closedDoorRotation;
        _isDoorOpen = false;
    }
}
