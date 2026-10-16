using UnityEngine;

public sealed class BoxPickupController : MonoBehaviour
{
    [Header("Holding")]
    [SerializeField] private Camera _camera;
    [SerializeField] private Vector2 _holdViewportPosition = new Vector2(0.2f, 0.18f);
    [SerializeField, Min(0.1f)] private float _holdDistance = 4f;
    [SerializeField, Min(0.1f)] private float _followSpeed = 12f;
    private KeyboardStorageBox _heldBox;
    private Rigidbody _heldBody;
    private bool _detectCollisionsBeforeHold;
    public KeyboardStorageBox HeldBox => _heldBox;

    /// <summary>
    /// camera를 상자 추적 카메라로 저장한다.
    /// 기존 상호작용 컨트롤러와 같은 시점을 사용하도록 참조를 초기화한다.
    /// </summary>
    public void Initialize(Camera camera)
    {
        _camera = camera;
    }

    /// <summary>
    /// 같은 씬에 내려놓은 상자를 destination의 위치와 회전으로 이동한다.
    /// 상자의 Rigidbody 자세만 변경하고 손에 든 상자와 보관 상태는 유지한다.
    /// </summary>
    public void MoveUnheldBoxes(Transform destination)
    {
        Vector3 target = destination.position;
        Quaternion rotation = destination.rotation;
        foreach (KeyboardStorageBox box in FindObjectsByType<KeyboardStorageBox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (box == _heldBox || box.gameObject.scene != gameObject.scene) continue;
            Rigidbody body = box.GetComponent<Rigidbody>();
            body.position = target;
            body.rotation = rotation;
        }
    }

    void FixedUpdate()
    {
        if (_heldBox != null) UpdateHeldBoxPose();
    }

    void OnDisable()
    {
        if (_heldBox != null) DropBox();
    }

    /// <summary>
    /// box를 들기 대상으로 저장하고 잡기 전 충돌 설정을 기록한다.
    /// 중력과 물리 충돌을 끄고 카메라 추적을 시작한다.
    /// </summary>
    public void PickUpBox(KeyboardStorageBox box)
    {
        _heldBox = box;
        _heldBody = box.GetComponent<Rigidbody>();
        _detectCollisionsBeforeHold = _heldBody.detectCollisions;
        if (!_heldBody.isKinematic)
        {
            _heldBody.linearVelocity = Vector3.zero;
            _heldBody.angularVelocity = Vector3.zero;
        }
        _heldBody.isKinematic = true;
        _heldBody.useGravity = false;
        _heldBody.detectCollisions = false;
    }

    /// <summary>
    /// 카메라와 화면 좌표, 거리로 상자 중심의 목표 자세를 계산한다.
    /// 추적 속도를 사용하여 상자를 화면 왼쪽 하단으로 이동한다.
    /// </summary>
    private void UpdateHeldBoxPose()
    {
        Vector3 center = _camera.ViewportToWorldPoint(new Vector3(_holdViewportPosition.x, _holdViewportPosition.y, _holdDistance));
        Quaternion rotation = _camera.transform.rotation;
        Vector3 position = center - rotation * Vector3.Scale(_heldBody.centerOfMass, _heldBox.transform.lossyScale);
        float amount = 1f - Mathf.Exp(-_followSpeed * Time.fixedDeltaTime);
        _heldBody.MovePosition(Vector3.Lerp(_heldBody.position, position, amount));
        _heldBody.MoveRotation(Quaternion.Slerp(_heldBody.rotation, rotation, amount));
    }

    /// <summary>
    /// 상자를 현재 위치 아래의 바닥이나 작업대에 수평으로 놓고 충돌 설정을 복원한다.
    /// 중력과 동적 물리를 계속 끄고 현재 들기 상태를 비운다.
    /// </summary>
    public void DropBox()
    {
        _heldBody.detectCollisions = _detectCollisionsBeforeHold;
        _heldBody.rotation = Quaternion.Euler(0f, _camera.transform.eulerAngles.y, 0f);
        Physics.SyncTransforms();
        _heldBody.position = GetPlacementPosition();
        _heldBody.isKinematic = true;
        _heldBody.useGravity = false;
        _heldBox = null;
        _heldBody = null;
    }

    /// <summary>
    /// 들고 있는 상자의 현재 수평 위치 아래에서 가장 가까운 고정 표면을 찾는다.
    /// 상자 바닥 높이를 맞춘 배치 위치를 반환하며 지지면이 없으면 현재 위치를 유지한다.
    /// </summary>
    private Vector3 GetPlacementPosition()
    {
        Vector3 origin = _heldBody.position;
        origin.y = Mathf.Max(origin.y, _camera.transform.position.y) + 1f;
        float closest = float.PositiveInfinity;
        Vector3 surface = default;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody != null || hit.collider is CharacterController || hit.normal.y < 0.5f || hit.distance >= closest) continue;
            closest = hit.distance;
            surface = hit.point;
        }
        if (closest == float.PositiveInfinity) return _heldBody.position;
        Bounds bounds = default;
        bool initialized = false;
        foreach (Collider collider in _heldBox.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger) continue;
            if (!initialized) { bounds = collider.bounds; initialized = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        return surface + Vector3.up * (_heldBox.transform.position.y - bounds.min.y + 0.02f);
    }
}
