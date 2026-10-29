using System.Collections;
using System.Collections.Generic;

using UnityEngine;

[DisallowMultipleComponent]
public sealed class BackgroundKeyboardThrowVisit : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _actor;
    [SerializeField] private Transform _visitPoint;
    [SerializeField] private BackgroundKeyboardThrowSpawner _spawner;

    [Header("Second Region Points")]
    [SerializeField] private Transform _secondRegionHomePoint;
    [SerializeField] private Transform _secondRegionVisitPoint;
    [SerializeField] private Transform _secondRegionThrowPoint;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float _moveDuration = 0.6f;
    [SerializeField, Min(0f)] private float _waitBeforeThrow = 0.5f;
    [SerializeField, Min(0f)] private float _waitAfterThrow = 0.5f;
    private Vector3 _homePosition;
    private Quaternion _homeRotation;
    private bool _visiting;
    private int _activeRegion = -1;
    private bool _activeCompletionReward;
    private GameObject _activePrefab;
    private Queue<(int Region, bool IsCompletionReward, GameObject Prefab)> _requestedRegions;
    private Queue<Rigidbody> _returnKeyboards;
    private Rigidbody _activeReturnKeyboard;
    private bool _temporaryRequestPending;
    private bool _activeTemporaryThrow;
    private GameObject _temporaryPrefab;

    void Awake()
    {
        _homePosition = _actor.position;
        _homeRotation = _actor.rotation;
        _requestedRegions = new Queue<(int Region, bool IsCompletionReward, GameObject Prefab)>();
        _returnKeyboards = new Queue<Rigidbody>();
        _spawner.SetExternalSequence(true);
    }

    void Start()
    {
        if (_spawner.IsTemporarySecondPointThrow)
            RequestTemporarySecondPointThrow(_spawner.TemporaryKeyboardPrefab);
    }

    void OnDrawGizmos()
    {
        if (_secondRegionThrowPoint == null) return;
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 1f);
        Vector3 position = _secondRegionThrowPoint.position;
        Vector3 direction = _secondRegionThrowPoint.forward;
        Vector3 tip = position + direction * 1.5f;
        Gizmos.DrawWireSphere(position, 0.2f);
        Gizmos.DrawLine(position, tip);
        Gizmos.DrawLine(tip, tip - direction * 0.3f + _secondRegionThrowPoint.right * 0.15f);
        Gizmos.DrawLine(tip, tip - direction * 0.3f - _secondRegionThrowPoint.right * 0.15f);
    }

    void Update()
    {
        if (_visiting) return;
        if (_temporaryRequestPending)
        {
            _temporaryRequestPending = false;
            _activeTemporaryThrow = true;
            _activeRegion = 1;
            _activeCompletionReward = false;
            _activePrefab = _temporaryPrefab;
            StartCoroutine(VisitAndThrow(_activeRegion));
            return;
        }
        while (_returnKeyboards.Count > 0 && _returnKeyboards.Peek() == null) _returnKeyboards.Dequeue();
        if (_returnKeyboards.Count > 0)
        {
            _activeReturnKeyboard = _returnKeyboards.Dequeue();
            StartCoroutine(VisitAndReturnKeyboard(_activeReturnKeyboard));
            return;
        }
        if (_requestedRegions.Count == 0) return;
        (int Region, bool IsCompletionReward, GameObject Prefab) request = _requestedRegions.Dequeue();
        _activeRegion = request.Region;
        _activeTemporaryThrow = false;
        _activeCompletionReward = request.IsCompletionReward;
        _activePrefab = request.Prefab;
        StartCoroutine(VisitAndThrow(_activeRegion));
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (_activeReturnKeyboard != null) _returnKeyboards.Enqueue(_activeReturnKeyboard);
        _activeReturnKeyboard = null;
        if (_activeRegion >= 0)
        {
            if (_activeTemporaryThrow) _temporaryRequestPending = true;
            else _requestedRegions.Enqueue((_activeRegion, _activeCompletionReward, _activePrefab));
        }
        _activeRegion = -1;
        _activeCompletionReward = false;
        if (_actor != null)
            _actor.SetPositionAndRotation(_homePosition, _homeRotation);
        _visiting = false;
    }

    /// <summary>
    /// regionIndex의 구역을 보충하기 위해 방문점으로 이동하고 던진 뒤 원래 위치로 복귀한다.
    /// 이동·대기 시간을 사용하며 중복 실행을 막는 _visiting 상태를 갱신한다.
    /// </summary>
    private IEnumerator VisitAndThrow(int regionIndex)
    {
        _visiting = true;
        yield return MoveTo(_visitPoint.position, _visitPoint.rotation);
        yield return WaitFor(_waitBeforeThrow);
        _spawner.SpawnAndThrowForRegion(regionIndex, _activeCompletionReward, _activePrefab, _activeTemporaryThrow);
        _activeRegion = -1;
        _activeCompletionReward = false;
        yield return WaitFor(_waitAfterThrow);
        yield return MoveTo(_homePosition, _homeRotation);
        _visiting = false;
        _activeTemporaryThrow = false;
    }

    /// <summary>
    /// prefab을 사용하는 두 번째 지점의 방문, 투척, 복귀를 한 번 예약한다.
    /// 실행 또는 예약 중이면 false를 반환하고 대기 상태이면 Inspector 지점을 적용하여 true를 반환한다.
    /// </summary>
    public bool RequestTemporarySecondPointThrow(GameObject prefab)
    {
        if (_visiting || _temporaryRequestPending || _requestedRegions.Count > 0 || _returnKeyboards.Count > 0) return false;
        _temporaryPrefab = prefab;
        _homePosition = _secondRegionHomePoint.position;
        _homeRotation = _secondRegionHomePoint.rotation;
        _actor.SetPositionAndRotation(_homePosition, _homeRotation);
        _visitPoint = _secondRegionVisitPoint;
        _spawner.SetThrowPoint(_secondRegionThrowPoint);
        _temporaryRequestPending = true;
        return true;
    }

    /// <summary>
    /// regionIndex 구역의 던지기 요청을 count회 대기열에 저장한다.
    /// 각 요청은 진행 중인 방문이 끝난 뒤 순차적으로 실행하며 다른 구역 요청도 보관한다.
    /// </summary>
    public void RequestThrows(int regionIndex, int count)
    {
        for (int index = 0; index < count; index++) _requestedRegions.Enqueue((regionIndex, false, null));
    }

    /// <summary>
    /// regionIndex 구역에 던질 완료 보상 키보드를 count개 대기열에 저장한다.
    /// prefab이 지정되면 해당 프리팹을 사용하고 생성된 객체를 보상 진행 대상으로 구분한다.
    /// </summary>
    public void RequestRewardThrows(int regionIndex, int count, GameObject prefab = null)
    {
        for (int index = 0; index < count; index++) _requestedRegions.Enqueue((regionIndex, true, prefab));
    }

    /// <summary>
    /// 떨어진 keyboard의 원본 Rigidbody를 재투척 대기열에 저장한다.
    /// 기존 방문이 끝난 뒤 처리하며 새 키보드를 생성하거나 구역 등록을 변경하지 않는다.
    /// </summary>
    public void RequestKeyboardReturn(Rigidbody keyboard)
    {
        _returnKeyboards.Enqueue(keyboard);
    }

    /// <summary>
    /// regionIndex와 offset으로 보스 위치를 옮기고 두 번째 구역은 Inspector의 지정 지점을 적용한다.
    /// 이전 구역의 방문과 투척 예약을 정리하고 같은 보스 오브젝트를 재사용한다.
    /// </summary>
    public void RelocateForRegion(Vector3 offset, int regionIndex)
    {
        StopAllCoroutines();
        _requestedRegions.Clear();
        _returnKeyboards.Clear();
        _activeReturnKeyboard = null;
        _activeRegion = -1;
        _activeCompletionReward = false;
        _visiting = false;
        _homePosition += offset;
        _temporaryRequestPending = false;
        _activeTemporaryThrow = false;
        _actor.SetPositionAndRotation(_homePosition, _homeRotation);
        if (!_visitPoint.IsChildOf(_actor)) _visitPoint.position += offset;
        if (!_spawner.transform.IsChildOf(_actor)) _spawner.transform.position += offset;
        if (regionIndex == 1)
        {
            if (_secondRegionHomePoint != null)
            {
                _homePosition = _secondRegionHomePoint.position;
                _homeRotation = _secondRegionHomePoint.rotation;
                _actor.SetPositionAndRotation(_homePosition, _homeRotation);
            }
            if (_secondRegionVisitPoint != null) _visitPoint = _secondRegionVisitPoint;
            _spawner.SetThrowPoint(_secondRegionThrowPoint);
        }
    }

    /// <summary>
    /// 기존 방문점으로 이동하여 keyboard 자체를 다시 던지고 원래 위치로 복귀한다.
    /// 이동·대기 시간을 사용하며 대기 중 원본이 제거되면 재투척을 생략한다.
    /// </summary>
    private IEnumerator VisitAndReturnKeyboard(Rigidbody keyboard)
    {
        _visiting = true;
        yield return MoveTo(_visitPoint.position, _visitPoint.rotation);
        yield return WaitFor(_waitBeforeThrow);
        if (keyboard != null) _spawner.ThrowExistingKeyboard(keyboard);
        _activeReturnKeyboard = null;
        yield return WaitFor(_waitAfterThrow);
        yield return MoveTo(_homePosition, _homeRotation);
        _visiting = false;
    }

    /// <summary>
    /// actor를 destination 위치와 destinationRotation 회전까지 설정한 시간 동안 부드럽게 이동한다.
    /// Time.deltaTime과 시작 자세를 사용하며 종료 시 actor의 위치와 회전을 목적지에 맞춘다.
    /// </summary>
    private IEnumerator MoveTo(Vector3 destination, Quaternion destinationRotation)
    {
        Vector3 start = _actor.position;
        Quaternion startRotation = _actor.rotation;
        float elapsed = 0f;
        while (elapsed < _moveDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _moveDuration));
            _actor.SetPositionAndRotation(Vector3.Lerp(start, destination, progress),
                Quaternion.Slerp(startRotation, destinationRotation, progress));
            yield return null;
        }
        _actor.SetPositionAndRotation(destination, destinationRotation);
    }

    /// <summary>
    /// duration초 동안 현재 방문 순서를 대기시킨다.
    /// Time.deltaTime으로 경과 시간을 계산하며 위치와 던지기 상태는 변경하지 않는다.
    /// </summary>
    private IEnumerator WaitFor(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}
