using UnityEngine;

using KeyboardModeling;

[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class NextRegionWorkbenchTransition : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KeyboardCompletionProgress _completionProgress;
    [SerializeField] private KeyboardInteractionController _interactionController;
    [SerializeField] private PlayerMovement _player;
    [SerializeField] private Transform[] _workbenchObjects;

    [Header("Destination")]
    [SerializeField] private Vector3 _workbenchOffset;
    [SerializeField, InspectorName("작업대 기준 책상")] private Transform _workbenchAnchor;
    [SerializeField, InspectorName("작업대 이동 목적지")] private Transform _workbenchDestination;
    [SerializeField, Range(1, 2)] private int _nextSpawnRegion = 1;
    [SerializeField] private Transform _nextDoor;

    [Header("Storage Box Movement")]
    [SerializeField, InspectorName("상자 이동 목적지")] private Transform _boxDestination;
    private bool _hasTransitioned;

    void OnTriggerEnter(Collider other)
    {
        TryEnterRegion(other);
    }

    void OnTriggerStay(Collider other)
    {
        TryEnterRegion(other);
    }

    /// <summary>
    /// other가 열린 문을 지나 들어온 플레이어이면 Inspector 목적지로 작업대들을 이동한다.
    /// 거실은 문을 닫고 마당은 문을 유지하며 각 구역의 키보드를 생성한다.
    /// </summary>
    private void TryEnterRegion(Collider other)
    {
        if (_hasTransitioned || !_completionProgress.IsDoorOpen || _interactionController.ActiveSpawnRegion != _nextSpawnRegion - 1
            || other.GetComponentInParent<PlayerMovement>() != _player) return;
        _hasTransitioned = true;
        _interactionController.MoveStorageBoxes(_boxDestination);
        Vector3 workbenchOffset = MoveWorkbench();
        _interactionController.RefreshWorkbenchPlacement();
        if (_nextSpawnRegion == 2)
        {
            _interactionController.EnterYard();
            Physics.SyncTransforms();
            GetComponent<BoxCollider>().enabled = false;
            enabled = false;
            return;
        }
        _completionProgress.CloseDoor();
        _interactionController.EnterSpawnRegion(_nextSpawnRegion, _nextDoor, workbenchOffset);
        Physics.SyncTransforms();
        GetComponent<BoxCollider>().enabled = false;
        enabled = false;
    }

    /// <summary>
    /// 기준 책상을 Inspector 목적지의 위치와 회전에 맞추며 작업대들의 상대 배치를 유지한다.
    /// 목적지 또는 이동량을 사용하되 세 번째 구역에서는 자동작업대를 제외하고 실제 위치 이동량을 반환한다.
    /// </summary>
    private Vector3 MoveWorkbench()
    {
        if (_workbenchAnchor == null || _workbenchDestination == null)
        {
            foreach (Transform item in _workbenchObjects)
                if (ShouldMoveWorkbenchItem(item)) item.position += _workbenchOffset;
            return _workbenchOffset;
        }
        Vector3 origin = _workbenchAnchor.position;
        Vector3 destination = _workbenchDestination.position;
        Quaternion rotation = _workbenchDestination.rotation * Quaternion.Inverse(_workbenchAnchor.rotation);
        foreach (Transform item in _workbenchObjects)
            if (ShouldMoveWorkbenchItem(item))
                item.SetPositionAndRotation(destination + rotation * (item.position - origin), rotation * item.rotation);
        return destination - origin;
    }

    /// <summary>
    /// item이 현재 구역 전환에서 이동할 대상인지 반환한다.
    /// 다음 구역 번호와 자동작업대 컴포넌트를 확인하여 세 번째 구역에서는 자동작업대와 그 하위 대상을 제외한다.
    /// </summary>
    private bool ShouldMoveWorkbenchItem(Transform item)
    {
        return _nextSpawnRegion != 2
            || (item.GetComponentInChildren<KeyboardAutomaticStation>(true) == null
                && item.GetComponentInParent<KeyboardAutomaticStation>(true) == null);
    }
}
