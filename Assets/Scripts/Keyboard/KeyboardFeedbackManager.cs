using KeyboardModeling;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class KeyboardFeedbackManager : MonoBehaviour
{
    /// <summary>
    /// 별도 자동작업대의 키보드를 기존 이펙트 대상으로 연결한다.
    /// keyboard를 사용해 이전 효과 구독을 정리하고 자동 및 기존 키 피드백의 대상 참조를 변경한다.
    /// </summary>
    public void SetAutomaticKeyboard(KeyboardInputController keyboard)
    {
        BindKeyboard(keyboard);
        _automaticTarget = keyboard != null;
    }


    [Header("Target")]
    [SerializeField] private KeyboardInteractionController _interactionController;
    [SerializeField] private KeyboardInputController _previewKeyboard;
    private KeyboardInputController _keyboard;
    private bool _automaticTarget;

    [Header("Live Options")]
    [SerializeField] private bool _enableHitStop = true;
    [SerializeField] private bool _enableDebris = true;
    [SerializeField] private bool _enableWhiteFlash = true;
    [SerializeField] private bool _enableKeyboardBounce = true;

    [Header("Keyboard Bounce")]
    [SerializeField, Min(0f)] private float _keyboardBounceDistance = 0.18f;
    [SerializeField, Min(0.01f)] private float _keyboardBouncePressDuration = 0.045f;
    [SerializeField, Min(0.01f)] private float _keyboardBounceReturnDuration = 0.14f;
    private KeyboardDestruction _keyboardDestruction;
    private Vector3 _keyboardBounceOffset;
    private float _keyboardBounceDepth;
    private float _keyboardBounceStartDepth;
    private float _keyboardBounceElapsed;
    private bool _isKeyboardBouncing;

    [Header("White Flash")]
    [SerializeField] private Material _whiteFlashMaterial;
    [SerializeField, Range(0.02f, 0.5f)] private float _whiteFlashDuration = 0.12f;
    private MeshRenderer[] _flashRenderers;
    private Material[] _flashOriginalMaterials;
    private float[] _flashEndTimes;
    private readonly Dictionary<Transform, int> _flashIndices = new Dictionary<Transform, int>();

    [Header("Debris")]
    [SerializeField] private GameObject _debrisPrefab;
    [SerializeField, Range(8, 96)] private int _debrisPoolSize = 48;
    [SerializeField, Range(1, 12)] private int _debrisCount = 5;
    [SerializeField, Min(0.05f)] private float _debrisLifetime = 0.45f;
    [SerializeField] private Vector2 _debrisSize = new Vector2(0.07f, 0.13f);
    [SerializeField] private Vector2 _horizontalSpeed = new Vector2(0.6f, 1.4f);
    [SerializeField] private Vector2 _upwardSpeed = new Vector2(1.3f, 2.1f);
    [SerializeField, Min(0f)] private float _debrisGravity = 9f;
    [SerializeField, Min(0.01f)] private float _maximumTravelDistance = 0.85f;
    private GameObject _debrisRoot;
    private MeshRenderer[] _debrisRenderers;
    private Vector3[] _origins;
    private Vector3[] _positions;
    private Vector3[] _velocities;
    private Vector3[] _sizes;
    private Vector3[] _spin;
    private float[] _ages;
    private float[] _lifetimes;
    private Bounds _keyboardBounds;
    private int _nextDebris;

    [Header("Hit Stop")]
    [SerializeField, Range(0.005f, 0.5f)] private float _hitStopDuration = 0.25f;
    [SerializeField, Min(0f)] private float _hitStopCooldown = 0.35f;
    private bool _isHitStopped;
    private float _savedTimeScale;
    private float _hitStopEnd;
    private float _nextHitStop;

    void Awake()
    {
        CreateDebrisPool();
    }

    void OnEnable()
    {
        if (_interactionController != null)
        {
            _interactionController.ActiveKeyboardChanged += BindKeyboard;
            BindKeyboard(_interactionController.ActiveKeyboard);
        }
        else
            BindKeyboard(_previewKeyboard);
    }

    void Update()
    {
        if (_isHitStopped && (!_enableHitStop || Time.unscaledTime >= _hitStopEnd))
            EndHitStop();
        if (_keyboard != null)
            _keyboard.SetHitStopEnabled(_enableHitStop);
        UpdateDebris();
        UpdateWhiteFlashes();
    }

    void OnDisable()
    {
        if (_interactionController != null)
            _interactionController.ActiveKeyboardChanged -= BindKeyboard;
        BindKeyboard(null);
        EndHitStop();
    }

    void LateUpdate()
    {
        UpdateKeyboardBounce();
    }

    void OnDestroy()
    {
        if (_debrisRoot != null) Destroy(_debrisRoot);
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            EndHitStop();
            ClearKeyboardBounce();
        }
    }

    /// <summary>
    /// keyboard의 새 눌림 이벤트를 구독하고 이전 대상의 연결과 부스러기를 해제한다.
    /// 대상 메시 경계를 저장하여 부스러기가 키보드 외곽을 넘어가지 않도록 제한한다.
    /// </summary>
    private void BindKeyboard(KeyboardInputController keyboard)
    {
        ClearKeyboardBounce();
        if (_keyboard != null)
        {
            _keyboard.KeycapPressed -= HandleKeycapPressed;
            _keyboard.AutomaticKeycapHit -= HandleKeycapPressed;
            _keyboard.SetHitStopEnabled(false);
        }
        EndHitStop();
        ClearDebris();
        ClearWhiteFlashes();
        _keyboard = keyboard;
        if (_keyboard == null) return;
        _keyboardDestruction = _keyboard.GetComponent<KeyboardDestruction>();
        CacheWhiteFlashTargets();
        BoxCollider collider = _keyboard.GetComponent<BoxCollider>();
        if (collider != null)
            _keyboardBounds = new Bounds(collider.center, collider.size);
        else
        {
            bool first = true;
            foreach (MeshFilter filter in _keyboard.GetComponentsInChildren<MeshFilter>())
            {
                Bounds bounds = filter.sharedMesh.bounds;
                Matrix4x4 matrix = _keyboard.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { _keyboardBounds = new Bounds(point, Vector3.zero); first = false; }
                    else _keyboardBounds.Encapsulate(point);
                }
            }
        }
        _keyboard.SetHitStopEnabled(_enableHitStop);
        _keyboard.KeycapPressed += HandleKeycapPressed;
        _keyboard.AutomaticKeycapHit += HandleKeycapPressed;
    }

    /// <summary>
    /// 살아 있는 keycap의 새 눌림에서 키보드 바운스, 부스러기, 흰색 점멸과 히트스톱을 시작한다.
    /// 키캡 위치와 현재 옵션을 사용하여 각 효과의 재생 상태를 변경한다.
    /// </summary>
    private void HandleKeycapPressed(Transform keycap)
    {
        KeycapHealth health = keycap.GetComponent<KeycapHealth>();
        if (health != null && health.CurrentHP <= 0) return;
        if (_enableKeyboardBounce && !_automaticTarget)
        {
            _keyboardBounceStartDepth = _keyboardBounceDepth;
            _keyboardBounceElapsed = 0f;
            _isKeyboardBouncing = true;
        }
        if (_enableDebris) PlayDebris(keycap);
        if (_enableWhiteFlash) PlayWhiteFlash(keycap);
        if (_enableHitStop && !_isHitStopped && Time.timeScale > 0f && Time.unscaledTime >= _nextHitStop)
        {
            _savedTimeScale = Time.timeScale;
            _hitStopEnd = Time.unscaledTime + _hitStopDuration;
            _nextHitStop = _hitStopEnd + _hitStopCooldown;
            _isHitStopped = true;
            Time.timeScale = 0f;
        }
    }

    /// <summary>
    /// 눌림 시간과 복귀 시간 및 이동량으로 키보드 전체를 아래로 눌렀다가 원위치로 복귀시킨다.
    /// 실제 경과 시간과 현재 깊이를 사용하며 이전 이동분을 교체하여 연타 시 위치 누적을 막는다.
    /// </summary>
    private void UpdateKeyboardBounce()
    {
        if (!_isKeyboardBouncing) return;
        if (_keyboard == null || !_enableKeyboardBounce || (!_keyboard.InputEnabled && !_automaticTarget) ||
            (_keyboardDestruction != null && _keyboardDestruction.IsBroken))
        {
            ClearKeyboardBounce();
            return;
        }

        _keyboardBounceElapsed += Time.unscaledDeltaTime;
        float pressDuration = Mathf.Max(0.01f, _keyboardBouncePressDuration);
        float returnDuration = Mathf.Max(0.01f, _keyboardBounceReturnDuration);
        if (_keyboardBounceElapsed >= pressDuration + returnDuration)
        {
            ClearKeyboardBounce();
            return;
        }

        if (_keyboardBounceElapsed < pressDuration)
        {
            _keyboardBounceDepth = Mathf.Lerp(_keyboardBounceStartDepth, _keyboardBounceDistance,
                Mathf.SmoothStep(0f, 1f, _keyboardBounceElapsed / pressDuration));
        }
        else
        {
            _keyboardBounceDepth = Mathf.Lerp(_keyboardBounceDistance, 0f,
                Mathf.SmoothStep(0f, 1f, (_keyboardBounceElapsed - pressDuration) / returnDuration));
        }

        Transform target = _keyboard.transform;
        Vector3 offset = target.TransformVector(Vector3.down * _keyboardBounceDepth);
        target.position += offset - _keyboardBounceOffset;
        _keyboardBounceOffset = offset;
    }

    /// <summary>
    /// 현재 키보드에 적용한 이동분을 제거하고 눌림 효과의 깊이와 재생 상태를 초기화한다.
    /// 저장된 이동분을 사용하여 대상 교체, 비활성화 또는 효과 종료 시 원래 위치를 복원한다.
    /// </summary>
    private void ClearKeyboardBounce()
    {
        if (_keyboard != null)
            _keyboard.transform.position -= _keyboardBounceOffset;
        _keyboardBounceOffset = Vector3.zero;
        _keyboardBounceDepth = 0f;
        _keyboardBounceStartDepth = 0f;
        _keyboardBounceElapsed = 0f;
        _isKeyboardBouncing = false;
    }

    /// <summary>
    /// 매니저가 시작한 히트스톱의 이전 시간 배율을 복원한다.
    /// 저장한 배율과 소유 상태를 사용하며 히트스톱 종료 상태로 변경한다.
    /// </summary>
    private void EndHitStop()
    {
        if (!_isHitStopped) return;
        if (Time.timeScale == 0f) Time.timeScale = _savedTimeScale;
        _isHitStopped = false;
    }

    /// <summary>
    /// debrisPrefab과 풀 크기로 작은 조각 인스턴스와 운동 상태 배열을 준비한다.
    /// 기존 메시 및 재질을 공유하고 인스턴스를 비활성화하여 타격마다 생성하지 않는다.
    /// </summary>
    private void CreateDebrisPool()
    {
        if (_debrisPrefab == null) return;
        _debrisRoot = new GameObject("KeyboardDebrisPool");
        _debrisRenderers = new MeshRenderer[_debrisPoolSize];
        _origins = new Vector3[_debrisPoolSize];
        _positions = new Vector3[_debrisPoolSize];
        _velocities = new Vector3[_debrisPoolSize];
        _sizes = new Vector3[_debrisPoolSize];
        _spin = new Vector3[_debrisPoolSize];
        _ages = new float[_debrisPoolSize];
        _lifetimes = new float[_debrisPoolSize];
        for (int index = 0; index < _debrisRenderers.Length; index++)
        {
            GameObject piece = Instantiate(_debrisPrefab, _debrisRoot.transform);
            _debrisRenderers[index] = piece.GetComponent<MeshRenderer>();
            piece.SetActive(false);
        }
    }

    /// <summary>
    /// keycap 위에서 랜덤한 방향, 크기, 회전과 수명을 가진 부스러기를 재생한다.
    /// 키캡 외부의 공유 재질을 재사용하고 풀 슬롯의 로컬 위치와 속도를 초기화한다.
    /// </summary>
    private void PlayDebris(Transform keycap)
    {
        if (_debrisRenderers == null) return;
        Vector3 origin = _keyboard.transform.InverseTransformPoint(keycap.TransformPoint(new Vector3(0f, 0.58f, 0f)));
        Transform shell = keycap.Find("PBT_SculptedShell");
        Material material = shell != null ? shell.GetComponent<MeshRenderer>().sharedMaterial : _debrisPrefab.GetComponent<MeshRenderer>().sharedMaterial;
        if (_flashIndices.TryGetValue(keycap, out int flashIndex) && _flashOriginalMaterials[flashIndex] != null)
            material = _flashOriginalMaterials[flashIndex];
        for (int count = 0; count < _debrisCount; count++)
        {
            int index = _nextDebris;
            _nextDebris = (_nextDebris + 1) % _debrisRenderers.Length;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float speed = Random.Range(_horizontalSpeed.x, _horizontalSpeed.y);
            _origins[index] = origin;
            _positions[index] = origin;
            _velocities[index] = new Vector3(Mathf.Cos(angle) * speed, Random.Range(_upwardSpeed.x, _upwardSpeed.y), Mathf.Sin(angle) * speed);
            float size = Random.Range(_debrisSize.x, _debrisSize.y);
            _sizes[index] = new Vector3(size, size * Random.Range(0.4f, 0.85f), size * Random.Range(0.65f, 1.2f));
            _spin[index] = Random.insideUnitSphere * 650f;
            _ages[index] = 0f;
            _lifetimes[index] = _debrisLifetime * Random.Range(0.8f, 1.2f);
            Transform piece = _debrisRenderers[index].transform;
            piece.position = _keyboard.transform.TransformPoint(origin);
            piece.rotation = keycap.rotation * Random.rotationUniform;
            piece.localScale = Vector3.Scale(_sizes[index], _keyboard.transform.lossyScale);
            _debrisRenderers[index].sharedMaterial = material;
            piece.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 활성 부스러기를 실제 시간으로 이동시키고 짧은 포물선과 감속을 표시한다.
    /// 키보드 경계와 최대 이동 거리를 적용하며 수명 끝에는 크기를 줄여 풀로 반환한다.
    /// </summary>
    private void UpdateDebris()
    {
        if (_debrisRenderers == null) return;
        if (!_enableDebris || _keyboard == null)
        {
            ClearDebris();
            return;
        }
        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
        for (int index = 0; index < _debrisRenderers.Length; index++)
        {
            Transform piece = _debrisRenderers[index].transform;
            if (!piece.gameObject.activeSelf) continue;
            _ages[index] += Time.unscaledDeltaTime;
            if (_ages[index] >= _lifetimes[index])
            {
                piece.gameObject.SetActive(false);
                continue;
            }
            _velocities[index].y -= _debrisGravity * deltaTime;
            _velocities[index].x *= Mathf.Exp(-3f * deltaTime);
            _velocities[index].z *= Mathf.Exp(-3f * deltaTime);
            Vector3 candidate = _positions[index] + _velocities[index] * deltaTime;
            Vector3 offset = candidate - _origins[index];
            float limit = Mathf.Max(0.01f, _maximumTravelDistance);
            if (offset.sqrMagnitude > limit * limit)
            {
                candidate = _origins[index] + Vector3.ClampMagnitude(offset, limit);
                _velocities[index] = Vector3.zero;
            }
            candidate.x = Mathf.Clamp(candidate.x, _keyboardBounds.min.x, _keyboardBounds.max.x);
            candidate.z = Mathf.Clamp(candidate.z, _keyboardBounds.min.z, _keyboardBounds.max.z);
            if (candidate.y < _origins[index].y)
            {
                candidate.y = _origins[index].y;
                _velocities[index].y = 0f;
            }
            _positions[index] = candidate;
            piece.position = _keyboard.transform.TransformPoint(candidate);
            piece.Rotate(_spin[index] * deltaTime, Space.Self);
            float shrink = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, _ages[index] / _lifetimes[index]));
            piece.localScale = Vector3.Scale(_sizes[index], _keyboard.transform.lossyScale) * shrink;
        }
    }

    /// <summary>
    /// 모든 부스러기를 숨기고 현재 타격 표시를 초기화한다.
    /// 옵션 해제, 대상 교체 또는 비활성화 시 인스턴스는 삭제하지 않고 재사용한다.
    /// </summary>
    private void ClearDebris()
    {
        if (_debrisRenderers == null) return;
        foreach (MeshRenderer piece in _debrisRenderers)
            piece.gameObject.SetActive(false);
    }

    /// <summary>
    /// 현재 키보드의 키캡 외부 렌더러와 원래 공유 재질을 캐시한다.
    /// 키캡 버튼의 Transform을 인덱스로 연결하여 피격마다 재질 인스턴스를 만들지 않도록 준비한다.
    /// </summary>
    private void CacheWhiteFlashTargets()
    {
        _flashIndices.Clear();
        KeycapButton[] buttons = _keyboard.GetComponentsInChildren<KeycapButton>(true);
        _flashRenderers = new MeshRenderer[buttons.Length];
        _flashOriginalMaterials = new Material[buttons.Length];
        _flashEndTimes = new float[buttons.Length];
        for (int index = 0; index < buttons.Length; index++)
        {
            Transform shell = buttons[index].transform.Find("PBT_SculptedShell");
            if (shell == null) continue;
            MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
            _flashRenderers[index] = renderer;
            _flashOriginalMaterials[index] = renderer.sharedMaterial;
            _flashIndices.Add(buttons[index].transform, index);
        }
    }

    /// <summary>
    /// keycap의 외부 표면을 잠시 흰색 공유 재질로 표시한다.
    /// 대상 인덱스와 지속 시간을 사용하며 연타 시 복원 시간을 갱신하고 원본 재질 에셋은 변경하지 않는다.
    /// </summary>
    private void PlayWhiteFlash(Transform keycap)
    {
        if (_whiteFlashMaterial == null || !_flashIndices.TryGetValue(keycap, out int index)) return;
        _flashRenderers[index].sharedMaterial = _whiteFlashMaterial;
        _flashEndTimes[index] = Time.unscaledTime + _whiteFlashDuration;
    }

    /// <summary>
    /// 실제 시간을 사용해 흰색 표시가 끝난 키캡의 원래 재질을 복원한다.
    /// 옵션 해제 시 모든 표시를 즉시 해제하며 히트스톱 중에도 복원 시간이 진행된다.
    /// </summary>
    private void UpdateWhiteFlashes()
    {
        if (_flashRenderers == null) return;
        if (!_enableWhiteFlash)
        {
            ClearWhiteFlashes();
            return;
        }
        for (int index = 0; index < _flashRenderers.Length; index++)
        {
            if (_flashEndTimes[index] == 0f || Time.unscaledTime < _flashEndTimes[index]) continue;
            if (_flashRenderers[index] != null)
                _flashRenderers[index].sharedMaterial = _flashOriginalMaterials[index];
            _flashEndTimes[index] = 0f;
        }
    }

    /// <summary>
    /// 현재 흰색 표시 중인 키캡의 원래 공유 재질을 복원한다.
    /// 캐시된 재질과 종료 시간을 사용하여 대상 교체 또는 매니저 비활성화 시 표시 상태를 해제한다.
    /// </summary>
    private void ClearWhiteFlashes()
    {
        if (_flashRenderers == null) return;
        for (int index = 0; index < _flashRenderers.Length; index++)
        {
            if (_flashEndTimes[index] == 0f) continue;
            if (_flashRenderers[index] != null)
                _flashRenderers[index].sharedMaterial = _flashOriginalMaterials[index];
            _flashEndTimes[index] = 0f;
        }
    }
}
