using UnityEngine;

/// <summary>
/// 생성 구역별 키보드와 보충 투척의 진행 상태를 관리한다.
/// 등록과 완료된 키보드를 받아 구역의 잔여 개수 및 보충 예약을 변경한다.
/// </summary>
internal sealed class KeyboardRegionState
{
    private const int REGION_COUNT = 3;
    private const int REFILL_THROW_COUNT = 3;

    private readonly int[] _pendingThrows = new int[REGION_COUNT];
    private readonly int[] _remainingKeyboards = new int[REGION_COUNT];
    private readonly bool[] _started = new bool[REGION_COUNT];
    private readonly bool[] _wasPopulated = new bool[REGION_COUNT];
    private readonly bool[] _refillTriggered = new bool[REGION_COUNT];
    private int _activeRegion = -1;

    public int ActiveRegion => _activeRegion;

    /// <summary>
    /// regionIndex에 등록된 키보드 한 대를 반영한다.
    /// 남은 키보드 개수를 늘리고 대기 중인 투척 한 건을 소비한다.
    /// </summary>
    public void Register(int regionIndex)
    {
        if (regionIndex < 0 || regionIndex >= REGION_COUNT) return;

        _remainingKeyboards[regionIndex]++;
        _wasPopulated[regionIndex] = true;
        if (_pendingThrows[regionIndex] > 0) _pendingThrows[regionIndex]--;
    }

    /// <summary>
    /// regionIndex에서 파괴를 완료한 키보드 한 대를 반영한다.
    /// 남은 개수를 0 아래로 내리지 않으며 다른 구역 상태는 변경하지 않는다.
    /// </summary>
    public void Complete(int regionIndex)
    {
        if (regionIndex < 0 || regionIndex >= REGION_COUNT) return;

        _remainingKeyboards[regionIndex] = Mathf.Max(0, _remainingKeyboards[regionIndex] - 1);
    }

    /// <summary>
    /// regionIndex가 마지막 활성 구역이며 모든 파괴와 투척이 끝났는지 확인한다.
    /// 현재 구역 상태를 반환하며 저장된 개수는 변경하지 않는다.
    /// </summary>
    public bool IsFinalRegionComplete(int regionIndex)
    {
        return regionIndex == 2 && _activeRegion == 2
            && _remainingKeyboards[2] == 0 && _pendingThrows[2] == 0;
    }

    /// <summary>
    /// 새 구역 regionIndex의 최초 진입 여부를 확인한다.
    /// 처음 진입하면 활성 구역과 시작 상태를 저장하고 true를 반환한다.
    /// </summary>
    public bool TryActivate(int regionIndex)
    {
        if (regionIndex < 0 || regionIndex >= REGION_COUNT || _started[regionIndex]) return false;

        _started[regionIndex] = true;
        _activeRegion = regionIndex;
        return true;
    }

    /// <summary>
    /// regionIndex가 이미 시작되었는지 확인한다.
    /// 구역 시작 상태를 반환하며 저장된 상태는 변경하지 않는다.
    /// </summary>
    public bool IsStarted(int regionIndex)
    {
        return _started[regionIndex];
    }

    /// <summary>
    /// regionIndex의 보충 조건을 확인하고 한 번의 보충을 예약한다.
    /// 보상 진행 상태와 진입 방식에 따라 예약된 투척 개수를 반환한다.
    /// </summary>
    public int TryBeginRefill(int regionIndex, bool rewardsRequested, bool spawnOnEntry)
    {
        if (rewardsRequested || _refillTriggered[regionIndex]
            || (spawnOnEntry && regionIndex != _activeRegion)
            || _remainingKeyboards[regionIndex] > 0 || !_wasPopulated[regionIndex]
            || _pendingThrows[regionIndex] > 0)
            return 0;

        _refillTriggered[regionIndex] = true;
        _pendingThrows[regionIndex] = REFILL_THROW_COUNT;
        _wasPopulated[regionIndex] = false;
        return REFILL_THROW_COUNT;
    }
}
