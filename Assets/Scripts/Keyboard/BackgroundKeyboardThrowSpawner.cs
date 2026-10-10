using KeyboardModeling;
using System.Collections.Generic;

using UnityEngine;

[DisallowMultipleComponent]
public sealed class BackgroundKeyboardThrowSpawner : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject _keyboardPrefab;
    [SerializeField] private bool _spawningEnabled = true;
    [SerializeField, Min(0.1f)] private float _spawnInterval = 2.5f;
    [SerializeField, Min(0f)] private float _firstSpawnDelay = 1f;
    [SerializeField, Min(0.01f)] private float _keyboardScale = 0.15f;
    [SerializeField, Range(1, 20)] private int _maximumAlive = 6;
    [SerializeField, Min(0.1f)] private float _lifetime = 10f;

    [SerializeField] private KeyboardInteractionController _keyboardInteractionController;
    private readonly Queue<GameObject> _spawned = new Queue<GameObject>();
    private Bounds _modelBounds;
    private float _remaining;

    [Header("Soft Throw")]
    [SerializeField] private Vector2 _forwardSpeed = new Vector2(2.8f, 3.6f);
    [SerializeField] private Vector2 _arcHeight = new Vector2(0.45f, 0.8f);
    [SerializeField, Range(0f, 45f)] private float _directionSpread = 8f;
    [SerializeField, Min(0f)] private float _spinSpeed = 1f;
    [SerializeField] private Vector3 _modelEulerAngles = Vector3.zero;

    void Awake()
    {
        _modelBounds = CalculateModelBounds();
    }

    void OnEnable()
    {
        _remaining = _firstSpawnDelay;
    }

    void Update()
    {
        if (!_spawningEnabled) return;
        _remaining -= Time.deltaTime;
        if (_remaining > 0f) return;
        SpawnAndThrow();
        _remaining = Mathf.Max(0.1f, _spawnInterval);
    }

    void OnDisable()
    {
        while (_spawned.Count > 0)
        {
            GameObject keyboard = _spawned.Dequeue();
            if (keyboard != null) Destroy(keyboard);
        }
    }

    /// <summary>
    /// 배경용 프리팹의 메시 경계를 루트 좌표계에서 계산한다.
    /// 설정한 프리팹의 메시 모서리를 합쳐 스폰 중심 보정과 단순 BoxCollider 크기에 사용할 Bounds를 반환한다.
    /// </summary>
    private Bounds CalculateModelBounds()
    {
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (MeshFilter filter in _keyboardPrefab.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds mesh = filter.sharedMesh.bounds;
            Matrix4x4 matrix = _keyboardPrefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
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
    private void SpawnAndThrow()
    {
        if (!Application.isPlaying) return;
        while (_spawned.Count > 0 && _spawned.Peek() == null) _spawned.Dequeue();
        while (_spawned.Count >= _maximumAlive)
        {
            GameObject oldest = _spawned.Dequeue();
            if (oldest != null) Destroy(oldest);
        }
        Vector3 up = Physics.gravity.sqrMagnitude > 0.001f ? -Physics.gravity.normalized : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(transform.up, up).normalized;
        forward = Quaternion.AngleAxis(Random.Range(-_directionSpread, _directionSpread), up) * forward;
        Quaternion rotation = transform.rotation * Quaternion.Euler(_modelEulerAngles)
            * Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(-12f, 12f), Random.Range(-6f, 6f));
        GameObject keyboard = Instantiate(_keyboardPrefab);
        keyboard.name = "ThrownBackgroundKeyboard";
        keyboard.transform.localScale = Vector3.one * _keyboardScale;
        keyboard.transform.SetPositionAndRotation(transform.position - rotation * (_modelBounds.center * _keyboardScale), rotation);
        foreach (MonoBehaviour behaviour in keyboard.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (Collider existing in keyboard.GetComponentsInChildren<Collider>(true))
            existing.enabled = false;
        BoxCollider collider = keyboard.GetComponent<BoxCollider>();
        if (collider == null) collider = keyboard.AddComponent<BoxCollider>();
        collider.center = _modelBounds.center;
        collider.size = _modelBounds.size;
        collider.isTrigger = false;
        collider.enabled = true;
        Rigidbody body = keyboard.GetComponent<Rigidbody>();
        if (body == null) body = keyboard.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        body.linearDamping = 0f;
        body.angularDamping = 0.3f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.position = transform.position - rotation * (_modelBounds.center * _keyboardScale);
        body.rotation = rotation;
        float height = Mathf.Max(0f, Random.Range(_arcHeight.x, _arcHeight.y));
        float upwardSpeed = Mathf.Sqrt(2f * Physics.gravity.magnitude * height);
        body.linearVelocity = forward * Random.Range(_forwardSpeed.x, _forwardSpeed.y) + up * upwardSpeed;
        body.angularVelocity = Random.insideUnitSphere * _spinSpeed;
        _spawned.Enqueue(keyboard);
        _keyboardInteractionController.RegisterKeyboard(keyboard);
        //Destroy(keyboard, _lifetime);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, 0.12f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
    }
}
