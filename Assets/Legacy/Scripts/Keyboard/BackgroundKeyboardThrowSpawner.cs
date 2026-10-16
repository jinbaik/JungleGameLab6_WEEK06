using System;
using System.Collections.Generic;

using UnityEngine;
using Random = UnityEngine.Random;

[DisallowMultipleComponent]
public sealed class BackgroundKeyboardThrowSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject _keyboardPrefab;
    [SerializeField] private bool _spawningEnabled = true;
    [SerializeField] private bool _externalSequence;
    [SerializeField, Min(0.1f)] private float _spawnInterval = 2.5f;
    [SerializeField, Min(0f)] private float _firstSpawnDelay = 1f;
    [SerializeField, Min(0.01f)] private float _keyboardScale = 0.15f;
    [SerializeField, Range(1, 20)] private int _maximumAlive = 6;
    [SerializeField, Min(0.1f)] private float _lifetime = 10f;

    [SerializeField] private KeyboardInteractionController _keyboardInteractionController;
    private readonly Queue<GameObject> _spawned = new Queue<GameObject>();
    private float _remaining;
    public event Action<KeyboardInteractionController.KeyboardState> RewardKeyboardSpawned;

    [Header("Temporary Second Point Throw")]
    [SerializeField] private bool _temporarySecondPointThrow;
    [SerializeField] private Transform _temporaryThrowPoint;
    [SerializeField] private GameObject _temporaryKeyboardPrefab;
    [SerializeField] private BackgroundKeyboardThrowVisit _temporaryVisit;
    private readonly Queue<GameObject> _temporaryKeyboards = new Queue<GameObject>();
    public bool IsTemporarySecondPointThrow => _temporarySecondPointThrow;
    public GameObject TemporaryKeyboardPrefab => _temporaryKeyboardPrefab;

    [Header("Soft Throw")]
    [SerializeField] private Transform _throwPoint;
    [SerializeField, Min(0.01f)] private float _throwMass = 1f;
    [SerializeField] private Vector2 _forwardSpeed = new Vector2(2.8f, 3.6f);
    [SerializeField] private Vector2 _arcHeight = new Vector2(0.45f, 0.8f);
    [SerializeField, Range(0f, 45f)] private float _directionSpread = 8f;
    [SerializeField, Min(0f)] private float _spinSpeed = 1f;
    [SerializeField] private Vector3 _modelEulerAngles = Vector3.zero;

    void OnEnable()
    {
        _remaining = _firstSpawnDelay;
    }

    void Update()
    {
        if (_temporarySecondPointThrow)
        {
            _remaining -= Time.deltaTime;
            if (_remaining > 0f) return;
            if (_temporaryVisit.RequestTemporarySecondPointThrow(_temporaryKeyboardPrefab))
                _remaining = Mathf.Max(0.1f, _spawnInterval);
            return;
        }
        if (!_spawningEnabled || _externalSequence) return;
        _remaining -= Time.deltaTime;
        if (_remaining > 0f) return;
        SpawnAndThrow();
        _remaining = Mathf.Max(0.1f, _spawnInterval);
    }

    void OnDisable()
    {
        while (_temporaryKeyboards.Count > 0)
        {
            GameObject keyboard = _temporaryKeyboards.Dequeue();
            if (keyboard != null) _keyboardInteractionController.DiscardTemporaryKeyboard(keyboard);
        }
        while (_spawned.Count > 0)
        {
            GameObject keyboard = _spawned.Dequeue();
            if (keyboard != null) Destroy(keyboard);
        }
    }

    /// <summary>
    /// model의 메시 모서리를 루트 좌표계에서 합친다.
    /// 신규 및 기존 키보드 모두 같은 충돌체 중심과 크기에 사용할 Bounds를 반환한다.
    /// </summary>
    private Bounds CalculateModelBounds(GameObject model)
    {
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds mesh = filter.sharedMesh.bounds;
            Matrix4x4 matrix = model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }

    /// <summary>
    /// 발사 오브젝트의 위치와 방향을 기준으로 배경용 키보드를 생성하고 가볍게 던진다.
    /// 속도, 상승 높이와 랜덤 회전을 사용해 중력 포물선의 초기 속도를 설정하고 개수 및 수명 제한을 적용한다.
    /// </summary>
    [ContextMenu("Spawn And Throw")]
    public void SpawnAndThrow()
    {
        SpawnAndThrowForRegion(-1);
    }

    /// <summary>
    /// external에 따라 자체 주기 생성 여부를 변경한다.
    /// 외부 방문 연출을 사용할 때는 자동 생성 대신 명시적인 던지기 요청만 처리한다.
    /// </summary>
    public void SetExternalSequence(bool external)
    {
        _externalSequence = external;
    }

    /// <summary>
    /// point를 공통 투척 위치와 방향의 기준으로 저장한다.
    /// 지정하지 않으면 발사 컴포넌트의 Transform을 기준으로 사용한다.
    /// </summary>
    public void SetThrowPoint(Transform point)
    {
        _throwPoint = point;
    }

    /// <summary>
    /// regionIndex 구역에 키보드를 생성하여 던지고 isCompletionReward이면 보상 생성 이벤트를 전달한다.
    /// prefab 또는 기본 프리팹과 투척 설정을 사용하며 보상 진행에 연결하고 음수 구역은 기존 개수 제한을 적용한다.
    /// </summary>
    public void SpawnAndThrowForRegion(int regionIndex, bool isCompletionReward = false, GameObject prefab = null, bool temporary = false)
    {
        if (!Application.isPlaying) return;
        if (temporary)
        {
            while (_temporaryKeyboards.Count > 0 && _temporaryKeyboards.Peek() == null) _temporaryKeyboards.Dequeue();
            while (_temporaryKeyboards.Count >= _maximumAlive)
            {
                GameObject previous = _temporaryKeyboards.Dequeue();
                if (previous != null) _keyboardInteractionController.DiscardTemporaryKeyboard(previous);
            }
        }
        while (_spawned.Count > 0 && _spawned.Peek() == null) _spawned.Dequeue();
        while (regionIndex < 0 && _spawned.Count >= _maximumAlive)
        {
            GameObject oldest = _spawned.Dequeue();
            if (oldest != null) Destroy(oldest);
        }
        GameObject keyboard = Instantiate(prefab != null ? prefab : _keyboardPrefab);
        KeyboardInteractionController.KeyboardState state = _keyboardInteractionController.RegisterKeyboard(keyboard, false, temporary ? -1 : regionIndex);
        PrepareAndLaunchKeyboard(state, regionIndex);
        if (temporary) _temporaryKeyboards.Enqueue(keyboard);
        else _spawned.Enqueue(keyboard);
        Debug.Log($"[Keyboard] New boss keyboard: instance={keyboard.GetInstanceID()}, region={regionIndex + 1}, reason={(isCompletionReward ? "completion reward" : regionIndex >= 0 ? "region refill" : "automatic spawn")}", keyboard);
        if (isCompletionReward) RewardKeyboardSpawned?.Invoke(state);
        //Destroy(keyboard, _lifetime);
    }

    /// <summary>
    /// keyboard의 등록 상태를 조회하고 신규 투척과 같은 준비 및 발사 경로로 다시 던진다.
    /// 원본 모델, 진행 식별자와 보상 정보를 유지하며 중복 등록이나 생성을 하지 않는다.
    /// </summary>
    public void ThrowExistingKeyboard(Rigidbody keyboard)
    {
        Debug.Log($"[Keyboard] Returning existing keyboard: instance={keyboard.gameObject.GetInstanceID()}. No new instance.", keyboard);
        KeyboardInteractionController.KeyboardState state = _keyboardInteractionController.GetRegisteredKeyboard(keyboard);
        PrepareAndLaunchKeyboard(state, _temporarySecondPointThrow ? 1 : state.SpawnRegionIndex);
    }

    /// <summary>
    /// state의 크기, 비활성 스크립트와 충돌체를 동일한 설정으로 준비하고 regionIndex 구역으로 발사한다.
    /// 신규 및 재투척 모두 같은 메시 경계와 Rigidbody 설정을 사용하며 진행 정보는 변경하지 않는다.
    /// </summary>
    private void PrepareAndLaunchKeyboard(KeyboardInteractionController.KeyboardState state, int regionIndex)
    {
        GameObject keyboard = state.Root;
        keyboard.name = "ThrownBackgroundKeyboard";
        keyboard.transform.localScale = Vector3.one * _keyboardScale;
        foreach (MonoBehaviour behaviour in keyboard.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (Collider existing in keyboard.GetComponentsInChildren<Collider>(true))
            existing.enabled = false;
        Bounds bounds = CalculateModelBounds(keyboard);
        state.Collider.center = bounds.center;
        state.Collider.size = bounds.size;
        state.Collider.isTrigger = false;
        state.Collider.enabled = true;
        LaunchKeyboard(state.Body, regionIndex);
    }

    /// <summary>
    /// body의 Collider 중심과 기존 던지기 설정을 사용해 발사 위치, 회전과 초기 속도를 적용한다.
    /// regionIndex가 지정되면 해당 구역으로 던지고 음수이면 기존 발사 방향으로 던진다.
    /// </summary>
    private void LaunchKeyboard(Rigidbody body, int regionIndex)
    {
        Transform origin = _temporarySecondPointThrow && _temporaryThrowPoint != null ? _temporaryThrowPoint
            : _throwPoint != null ? _throwPoint : transform;
        GameObject keyboard = body.gameObject;
        BoxCollider collider = keyboard.GetComponent<BoxCollider>();
        Vector3 up = Physics.gravity.sqrMagnitude > 0.001f ? -Physics.gravity.normalized : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(origin.forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(origin.up, up).normalized;
        forward = Quaternion.AngleAxis(Random.Range(-_directionSpread, _directionSpread), up) * forward;
        Quaternion rotation = origin.rotation * Quaternion.Euler(_modelEulerAngles)
            * Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(-12f, 12f), Random.Range(-6f, 6f));
        body.interpolation = RigidbodyInterpolation.None;
        body.mass = _throwMass;
        body.detectCollisions = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        body.linearDamping = 0f;
        body.angularDamping = 0.3f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.ResetCenterOfMass();
        body.ResetInertiaTensor();
        body.position = origin.position - rotation * (Vector3.Scale(collider.center, body.transform.lossyScale));
        body.rotation = rotation;
        float height = Mathf.Max(0f, Random.Range(_arcHeight.x, _arcHeight.y));
        float upwardSpeed = Mathf.Sqrt(2f * Physics.gravity.magnitude * height);
        body.linearVelocity = forward * Random.Range(_forwardSpeed.x, _forwardSpeed.y) + up * upwardSpeed;
        if (regionIndex >= 0)
        {
            Vector3 destination = _keyboardInteractionController.GetRegionSpawnPosition(regionIndex);
            Vector3 distance = destination - body.position;
            float gravity = Physics.gravity.magnitude;
            float verticalDistance = Vector3.Dot(distance, up);
            float apexHeight = Mathf.Max(height, verticalDistance + 0.1f);
            float launchSpeed = Mathf.Sqrt(2f * gravity * apexHeight);
            float flightTime = (launchSpeed + Mathf.Sqrt(2f * gravity * (apexHeight - verticalDistance))) / gravity;
            body.linearVelocity = Vector3.ProjectOnPlane(distance, up) / flightTime + up * launchSpeed;
            keyboard.name = $"ThrownBackgroundKeyboard_Region_{regionIndex + 1}";
        }
        body.angularVelocity = Random.insideUnitSphere * _spinSpeed;
        body.WakeUp();
    }

    void OnDrawGizmosSelected()
    {
        Transform origin = _throwPoint != null ? _throwPoint : transform;
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 1f);
        Gizmos.DrawWireSphere(origin.position, 0.12f);
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * 1.5f);
    }
}
