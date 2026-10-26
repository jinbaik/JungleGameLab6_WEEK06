using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

/// <summary>
/// 주먹 및 망치 타격 애니메이션, 충격파 링 확산 연출과 함께 키보드 바운스,
/// 화이트 플래시, 부스러기(Debris), 히트스톱 등 게임 내 모든 시각/타격 피드백 연출을 총괄하는 컴포넌트이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyboardFeedbackManager : MonoBehaviour
{
    private const float MAX_ANIMATION_PROGRESS_PER_FRAME = 0.25f;
    private const int FIST_SORTING_ORDER = 10;
    private const int SHOCKWAVE_SEGMENTS = 64;

    private static readonly int _baseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int _baseMapProperty = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseMapTransformProperty = Shader.PropertyToID("_BaseMap_ST");

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
    private MaterialPropertyBlock[] _flashOriginalProperties;
    private float[] _flashEndTimes;
    private KeycapHealth[] _flashHealths;
    private readonly Dictionary<Transform, int> _flashIndices = new Dictionary<Transform, int>();
    private readonly List<int> _activeFlashIndices = new List<int>();

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
    private readonly List<int> _activeDebris = new List<int>();

    [Header("Hit Stop")]
    [SerializeField, Range(0.005f, 0.5f)] private float _hitStopDuration = 0.25f;
    [SerializeField, Min(0f)] private float _hitStopCooldown = 0.35f;
    private bool _isHitStopped;
    private float _savedTimeScale;
    private float _hitStopEnd;
    private float _nextHitStop;

    [Header("Fist Strike")]
    [SerializeField] private Sprite _fistSprite;
    [SerializeField] private Material _fistMaterial;
    [SerializeField, Min(0.01f)] private float _fistHeight = 0.9f;
    [Tooltip("0이면 지정 높이를 사용하고 양수이면 일반 키캡 폭의 배수로 주먹 폭을 맞춥니다.")]
    [SerializeField, Min(0f)] private float _fistKeycapWidthMultiplier;
    [SerializeField, Min(0f)] private float _fistLift = 0.8f;
    [SerializeField, Min(0.01f)] private float _appearDuration = 0.22f;
    [SerializeField, Min(0.01f)] private float _strikeDuration = 0.16f;
    [SerializeField, Min(0.01f)] private float _retreatDuration = 0.25f;
    [SerializeField] private Vector3 _keycapFistImpactOffset = Vector3.zero;
    private MaterialPropertyBlock _fistProperties;
    private List<MeshRenderer> _fistRenderers = new List<MeshRenderer>();
    private Stack<MeshRenderer> _availableFists = new Stack<MeshRenderer>();
    private float _standardKeycapWidth;
    public event Action<Vector3> FistImpact;

    [Header("Hammer Strike")]
    [SerializeField] private Sprite _hammerSprite;
    [SerializeField] private Material _hammerShockwaveMaterial;
    [SerializeField] private HammerAnimationSettings _hammerAnimation = new HammerAnimationSettings();
    private List<LineRenderer> _shockwaveRenderers = new List<LineRenderer>();
    private Stack<LineRenderer> _availableShockwaves = new Stack<LineRenderer>();
    private readonly Stack<ShockwaveBuffer> _availableShockwaveBuffers = new Stack<ShockwaveBuffer>();

    public Sprite FistSprite => _fistSprite;
    public Sprite HammerSprite => _hammerSprite;
    public HammerAnimationSettings HammerAnimation => _hammerAnimation;

    void Awake()
    {
        CreateDebrisPool();
        EnsureFistResources();
    }

    void OnEnable()
    {
        if (_interactionController != null)
        {
            _interactionController.ActiveKeyboardChanged += BindKeyboard;
            BindKeyboard(_interactionController.ActiveKeyboard);
        }
        else
        {
            BindKeyboard(_previewKeyboard);
        }
    }

    void Update()
    {
        if (Game.Tournament.TournamentController.IsReturnPaused)
        {
            if (_isHitStopped) _hitStopEnd += Time.unscaledDeltaTime;
            _nextHitStop += Time.unscaledDeltaTime;
            if (_flashEndTimes != null)
                for (int index = 0; index < _flashEndTimes.Length; index++)
                    if (_flashEndTimes[index] > 0f) _flashEndTimes[index] += Time.unscaledDeltaTime;
            return;
        }
        if (Game.Tutorials.FirstEncounterTutorial.IsBlocking || Game.Tournament.TournamentController.IsReturnPaused) return;
        if (_isHitStopped && (!_enableHitStop || Time.unscaledTime >= _hitStopEnd))
        {
            EndHitStop();
        }

        if (_keyboard != null)
        {
            _keyboard.SetHitStopEnabled(_enableHitStop);
        }

        UpdateDebris();
        UpdateWhiteFlashes();
    }

    void LateUpdate()
    {
        if (Game.Tutorials.FirstEncounterTutorial.IsBlocking || Game.Tournament.TournamentController.IsReturnPaused) return;
        UpdateKeyboardBounce();
    }

    void OnDisable()
    {
        if (_interactionController != null)
        {
            _interactionController.ActiveKeyboardChanged -= BindKeyboard;
        }

        BindKeyboard(null);
        EndHitStop();
    }

    void OnDestroy()
    {
        if (_debrisRoot != null)
        {
            Destroy(_debrisRoot);
        }

        if (_fistRenderers != null)
        {
            foreach (MeshRenderer fist in _fistRenderers)
            {
                if (fist != null)
                {
                    Destroy(fist.gameObject);
                }
            }
        }

        if (_shockwaveRenderers != null)
        {
            foreach (LineRenderer ring in _shockwaveRenderers)
            {
                if (ring != null)
                {
                    Destroy(ring.gameObject);
                }
            }
        }
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
    /// 별도 자동작업대의 키보드를 기존 이펙트 대상으로 연결한다.
    /// keyboard를 사용해 이전 효과 구독을 정리하고 자동 및 기존 키 피드백의 대상 참조를 변경한다.
    /// 반환값은 없다.
    /// </summary>
    public void SetAutomaticKeyboard(KeyboardInputController keyboard)
    {
        BindKeyboard(keyboard);
        _automaticTarget = keyboard != null;
    }

    /// <summary>
    /// 외부에서 전달받은 주먹 애니메이션 속성들을 저장한다.
    /// 높이, 들어올림 거리, 등장/타격/대기 시간, 오프셋과 재질을 필드에 설정한다.
    /// 최소값을 보장하여 비정상 애니메이션 진행을 방지한다.
    /// </summary>
    public void ConfigureFistAnimation(float fistHeight, float fistLift, float appearDuration, float strikeDuration, float retreatDuration, Vector3 fistImpactOffset, Material fistMaterial)
    {
        _fistHeight = Mathf.Max(0.01f, fistHeight);
        _fistLift = Mathf.Max(0f, fistLift);
        _appearDuration = Mathf.Max(0.01f, appearDuration);
        _strikeDuration = Mathf.Max(0.01f, strikeDuration);
        _retreatDuration = Mathf.Max(0.01f, retreatDuration);
        _keycapFistImpactOffset = fistImpactOffset;
        _fistMaterial = fistMaterial;
    }

    /// <summary>
    /// 외부에서 전달받은 망치 스프라이트, 애니메이션 설정 및 충격파 재질을 저장한다.
    /// sprite, settings, shockwaveMaterial을 각 필드에 저장한다.
    /// 반환값은 없다.
    /// </summary>
    public void ConfigureHammerAnimation(Sprite sprite, HammerAnimationSettings settings, Material shockwaveMaterial)
    {
        _hammerSprite = sprite;
        _hammerAnimation = settings ?? new HammerAnimationSettings();
        _hammerShockwaveMaterial = shockwaveMaterial;
    }

    /// <summary>
    /// keycap 위치로 주먹 또는 망치 타격 애니메이션을 재생한다.
    /// keycap 대상 Transform, 타격 순간에 호출할 onImpact 콜백, 망치 사용 여부 useHammer를 받는다.
    /// 코루틴을 실행하여 타격 애니메이션을 진행한다.
    /// </summary>
    public void PlayKeycapStrike(Transform keycap, Action onImpact, bool useHammer = false)
    {
        if (keycap == null || (_keyboardDestruction != null && _keyboardDestruction.IsBroken))
        {
            onImpact?.Invoke();
            return;
        }

        if (keycap == null || (_keyboardDestruction != null && _keyboardDestruction.IsBroken))
        {
            onImpact?.Invoke();
            return;
        }

        Transform shellTransform = keycap.Find("PBT_SculptedShell");
        MeshFilter shell = shellTransform != null ? shellTransform.GetComponent<MeshFilter>() : keycap.GetComponentInChildren<MeshFilter>();
        if (shell == null)
        {
            onImpact?.Invoke();
            return;
        }

        Bounds bounds = shell.sharedMesh.bounds;
        Vector3 contact = shell.transform.TransformPoint(bounds.center + Vector3.up * bounds.extents.y);
        Transform root = _keyboard != null ? _keyboard.transform : transform;
        Vector3 localContact = root.InverseTransformPoint(contact);

        if (useHammer)
        {
            StartCoroutine(SwingHammer(localContact, onImpact));
        }
        else
        {
            StartCoroutine(StrikeKeyboard(localContact, onImpact));
        }
    }

    /// <summary>
    /// keycap 위치로 망치 타격 애니메이션을 시작한다.
    /// keycap Transform과 타격 순간에 실행할 onImpact 콜백을 입력으로 받는다.
    /// 망치 이미지가 유효할 때 PlayKeycapStrike를 useHammer=true로 호출한다.
    /// </summary>
    public void PlayHammerStrike(Transform keycap, Action onImpact)
    {
        PlayKeycapStrike(keycap, onImpact, true);
    }

    /// <summary>
    /// 충격파 링 애니메이션과 주변 키캡들의 시각적 출렁임 애니메이션을 재생한다.
    /// keyboard, keycaps, positions 배열과 targetIndex 타격 인덱스, 키캡 도달 시 호출할 onKeycapReached 콜백을 입력으로 받는다.
    /// onStarted와 onFinished로 시작·종료를 알리고 travelDuration이 양수이면 전파 시간을 지정한다.
    /// </summary>
    public void PlayHammerShockwave(KeyboardInputController keyboard, KeycapHealth[] keycaps, Vector2[] positions, int targetIndex, Action<KeycapHealth> onKeycapReached = null, bool showRing = true, bool requireInput = true, Action onStarted = null, float travelDuration = 0f, Action onFinished = null)
    {
        if (keyboard == null || keycaps == null || targetIndex < 0 || targetIndex >= keycaps.Length)
        {
            onFinished?.Invoke();
            return;
        }

        Transform keycapTransform = keycaps[targetIndex].transform;
        Transform shellTransform = keycapTransform.Find("PBT_SculptedShell");
        MeshFilter shell = shellTransform != null ? shellTransform.GetComponent<MeshFilter>() : keycaps[targetIndex].GetComponentInChildren<MeshFilter>();
        if (shell == null)
        {
            onFinished?.Invoke();
            return;
        }

        Bounds bounds = shell.sharedMesh.bounds;
        Vector3 contact = keyboard.transform.InverseTransformPoint(shell.transform.TransformPoint(bounds.center + Vector3.up * bounds.extents.y));
        StartCoroutine(SpreadHammerShockwave(keyboard, keycaps, positions, positions[targetIndex], contact.y + 0.03f, onKeycapReached, showRing, requireInput, onStarted, travelDuration, onFinished));
    }

    /// <summary>
    /// keyboard의 새 눌림 이벤트를 구독하고 이전 대상의 연결과 부스러기를 해제한다.
    /// 대상 메시 경계를 저장하여 부스러기가 키보드 외곽을 넘어가지 않도록 제한한다.
    /// </summary>
    private void BindKeyboard(KeyboardInputController keyboard)
    {
        ClearKeyboardBounce();
        if (_flashHealths != null)
        {
            foreach (KeycapHealth health in _flashHealths)
            {
                if (health != null)
                {
                    health.DamageApplied -= HandleKeycapDamageApplied;
                }
            }
        }

        if (_keyboard != null)
        {
            _keyboard.ManualKeycapHit -= HandleKeycapPressed;
            _keyboard.AreaKeycapHit -= HandleKeycapPressed;
            _keyboard.AutomaticKeycapHit -= HandleKeycapPressed;
            _keyboard.SetHitStopEnabled(false);
        }

        EndHitStop();
        ClearDebris();
        ClearWhiteFlashes();
        _keyboard = keyboard;

        if (_keyboard == null)
        {
            return;
        }

        _keyboardDestruction = _keyboard.GetComponent<KeyboardDestruction>();
        CacheStandardKeycapWidth();
        CacheWhiteFlashTargets();

        BoxCollider collider = _keyboard.GetComponent<BoxCollider>();
        if (collider != null)
        {
            _keyboardBounds = new Bounds(collider.center, collider.size);
        }
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
                    if (first)
                    {
                        _keyboardBounds = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        _keyboardBounds.Encapsulate(point);
                    }
                }
            }
        }

        _keyboard.SetHitStopEnabled(_enableHitStop);
        _keyboard.ManualKeycapHit += HandleKeycapPressed;
        _keyboard.AreaKeycapHit += HandleKeycapPressed;
        _keyboard.AutomaticKeycapHit += HandleKeycapPressed;
    }

    /// <summary>
    /// 살아 있는 keycap의 주먹 충돌이나 범위 및 자동 타격에서 키보드 바운스, 부스러기, 흰색 점멸과 히트스톱을 시작한다.
    /// 키캡 위치와 현재 옵션을 사용하여 각 효과의 재생 상태를 변경한다.
    /// </summary>
    private void HandleKeycapPressed(Transform keycap)
    {
        KeycapHealth health = keycap.GetComponent<KeycapHealth>();
        if (health != null && health.CurrentHP <= 0)
        {
            return;
        }

        if (_enableKeyboardBounce && !_automaticTarget)
        {
            _keyboardBounceStartDepth = _keyboardBounceDepth;
            _keyboardBounceElapsed = 0f;
            _isKeyboardBouncing = true;
        }

        if (_enableDebris)
        {
            PlayDebris(keycap);
        }

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
        if (!_isKeyboardBouncing)
        {
            return;
        }

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
        {
            _keyboard.transform.position -= _keyboardBounceOffset;
        }

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
        if (!_isHitStopped)
        {
            return;
        }

        if (Time.timeScale == 0f && !Game.Tournament.TournamentController.IsReturnPaused)
        {
            Time.timeScale = _savedTimeScale;
        }

        _isHitStopped = false;
    }

    /// <summary>
    /// debrisPrefab과 풀 크기로 작은 조각 인스턴스와 운동 상태 배열을 준비한다.
    /// 기존 메시 및 재질을 공유하고 인스턴스를 비활성화하여 타격마다 생성하지 않는다.
    /// </summary>
    private void CreateDebrisPool()
    {
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
        if (_debrisRenderers == null || _keyboard == null)
        {
            return;
        }

        Vector3 origin = _keyboard.transform.InverseTransformPoint(keycap.TransformPoint(new Vector3(0f, 0.58f, 0f)));
        Transform shell = keycap.Find("PBT_SculptedShell");
        Material material = shell != null ? shell.GetComponent<MeshRenderer>().sharedMaterial : _debrisPrefab.GetComponent<MeshRenderer>().sharedMaterial;
        if (_flashIndices.TryGetValue(keycap, out int flashIndex) && _activeFlashIndices.Contains(flashIndex) &&
            _flashRenderers[flashIndex].sharedMaterial == _whiteFlashMaterial && _flashOriginalMaterials[flashIndex] != null)
        {
            material = _flashOriginalMaterials[flashIndex];
        }

        for (int count = 0; count < _debrisCount; count++)
        {
            int index = _nextDebris;
            _nextDebris = (_nextDebris + 1) % _debrisRenderers.Length;
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float speed = UnityEngine.Random.Range(_horizontalSpeed.x, _horizontalSpeed.y);
            _origins[index] = origin;
            _positions[index] = origin;
            _velocities[index] = new Vector3(Mathf.Cos(angle) * speed, UnityEngine.Random.Range(_upwardSpeed.x, _upwardSpeed.y), Mathf.Sin(angle) * speed);
            float size = UnityEngine.Random.Range(_debrisSize.x, _debrisSize.y);
            _sizes[index] = new Vector3(size, size * UnityEngine.Random.Range(0.4f, 0.85f), size * UnityEngine.Random.Range(0.65f, 1.2f));
            _spin[index] = UnityEngine.Random.insideUnitSphere * 650f;
            _ages[index] = 0f;
            _lifetimes[index] = _debrisLifetime * UnityEngine.Random.Range(0.8f, 1.2f);
            Transform piece = _debrisRenderers[index].transform;
            piece.position = _keyboard.transform.TransformPoint(origin);
            piece.rotation = keycap.rotation * UnityEngine.Random.rotationUniform;
            piece.localScale = Vector3.Scale(_sizes[index], _keyboard.transform.lossyScale);
            _debrisRenderers[index].sharedMaterial = material;
            piece.gameObject.SetActive(true);
            if (!_activeDebris.Contains(index))
            {
                _activeDebris.Add(index);
            }
        }
    }

    /// <summary>
    /// 활성 부스러기를 실제 시간으로 이동시키고 짧은 포물선과 감속을 표시한다.
    /// 키보드 경계와 최대 이동 거리를 적용하며 수명 끝에는 크기를 줄여 풀로 반환한다.
    /// </summary>
    private void UpdateDebris()
    {
        if (_debrisRenderers == null)
        {
            return;
        }

        if (!_enableDebris || _keyboard == null)
        {
            ClearDebris();
            return;
        }

        if (_activeDebris.Count == 0)
        {
            return;
        }

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
        for (int i = _activeDebris.Count - 1; i >= 0; i--)
        {
            int index = _activeDebris[i];
            Transform piece = _debrisRenderers[index].transform;
            if (!piece.gameObject.activeSelf)
            {
                _activeDebris.RemoveAt(i);
                continue;
            }

            _ages[index] += Time.unscaledDeltaTime;
            if (_ages[index] >= _lifetimes[index])
            {
                piece.gameObject.SetActive(false);
                _activeDebris.RemoveAt(i);
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
        if (_debrisRenderers == null)
        {
            return;
        }

        for (int i = 0; i < _activeDebris.Count; i++)
        {
            int index = _activeDebris[i];
            if (_debrisRenderers[index] != null)
            {
                _debrisRenderers[index].gameObject.SetActive(false);
            }
        }
        _activeDebris.Clear();
    }

    /// <summary>
    /// 현재 키보드의 키캡 외부 렌더러와 원래 공유 재질을 캐시한다.
    /// 키캡 버튼의 Transform을 인덱스로 연결하여 피격마다 재질 인스턴스를 만들지 않도록 준비한다.
    /// </summary>
    private void CacheWhiteFlashTargets()
    {
        _flashIndices.Clear();
        _activeFlashIndices.Clear();
        KeycapButton[] buttons = _keyboard.GetComponentsInChildren<KeycapButton>(true);
        _flashRenderers = new MeshRenderer[buttons.Length];
        _flashOriginalMaterials = new Material[buttons.Length];
        _flashOriginalProperties = new MaterialPropertyBlock[buttons.Length];
        _flashEndTimes = new float[buttons.Length];
        _flashHealths = new KeycapHealth[buttons.Length];

        for (int index = 0; index < buttons.Length; index++)
        {
            Transform shell = buttons[index].transform.Find("PBT_SculptedShell");
            if (shell == null)
            {
                continue;
            }

            MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
            _flashRenderers[index] = renderer;
            _flashOriginalMaterials[index] = renderer.sharedMaterial;
            _flashOriginalProperties[index] = new MaterialPropertyBlock();
            _flashIndices.Add(buttons[index].transform, index);
            KeycapHealth health = buttons[index].GetComponent<KeycapHealth>();
            _flashHealths[index] = health;
            if (health != null)
            {
                health.DamageApplied += HandleKeycapDamageApplied;
            }
        }
    }

    /// <summary>
    /// 실제 피해를 받은 health의 외피에 화이트 플래시를 적용한다.
    /// 화이트 플래시 옵션과 키캡 Transform을 사용하며 직접·범위·외부 공격을 같은 방식으로 표시한다.
    /// </summary>
    private void HandleKeycapDamageApplied(KeycapHealth health)
    {
        if (_enableWhiteFlash)
        {
            PlayWhiteFlash(health.transform);
        }
    }

    /// <summary>
    /// keycap의 외부 표면을 잠시 흰색 공유 재질로 표시한다.
    /// 피격 직전 재질과 사진 속성을 보관하고 사진 속성을 잠시 해제해 흰색 피드백을 표시한다.
    /// </summary>
    private void PlayWhiteFlash(Transform keycap)
    {
        if (!_flashIndices.TryGetValue(keycap, out int index))
        {
            return;
        }

        if (!_activeFlashIndices.Contains(index) || _flashRenderers[index].sharedMaterial != _whiteFlashMaterial)
        {
            _flashOriginalMaterials[index] = _flashRenderers[index].sharedMaterial;
            _flashRenderers[index].GetPropertyBlock(_flashOriginalProperties[index]);
        }
        _flashRenderers[index].SetPropertyBlock(null);
        _flashRenderers[index].sharedMaterial = _whiteFlashMaterial;
        _flashEndTimes[index] = Time.unscaledTime + _whiteFlashDuration;
        if (!_activeFlashIndices.Contains(index))
        {
            _activeFlashIndices.Add(index);
        }
    }

    /// <summary>
    /// 실제 시간을 사용해 흰색 표시가 끝난 키캡의 원래 재질을 복원한다.
    /// 옵션 해제 시 모든 표시를 즉시 해제하며 히트스톱 중에도 복원 시간이 진행된다.
    /// </summary>
    private void UpdateWhiteFlashes()
    {
        if (_flashRenderers == null)
        {
            return;
        }

        if (!_enableWhiteFlash)
        {
            ClearWhiteFlashes();
            return;
        }

        if (_activeFlashIndices.Count == 0)
        {
            return;
        }

        for (int i = _activeFlashIndices.Count - 1; i >= 0; i--)
        {
            int index = _activeFlashIndices[i];
            if (_flashEndTimes[index] == 0f || Time.unscaledTime < _flashEndTimes[index])
            {
                continue;
            }

            if (_flashRenderers[index] != null && _flashRenderers[index].sharedMaterial == _whiteFlashMaterial)
            {
                _flashRenderers[index].sharedMaterial = _flashOriginalMaterials[index];
                _flashRenderers[index].SetPropertyBlock(_flashOriginalProperties[index]);
            }

            _flashEndTimes[index] = 0f;
            _activeFlashIndices.RemoveAt(i);
        }
    }

    /// <summary>
    /// 현재 흰색 표시 중인 키캡의 원래 공유 재질을 복원한다.
    /// 캐시된 재질과 종료 시간을 사용하여 대상 교체 또는 매니저 비활성화 시 표시 상태를 해제한다.
    /// </summary>
    private void ClearWhiteFlashes()
    {
        if (_flashRenderers == null)
        {
            return;
        }

        for (int i = 0; i < _activeFlashIndices.Count; i++)
        {
            int index = _activeFlashIndices[i];
            if (_flashRenderers[index] != null && _flashRenderers[index].sharedMaterial == _whiteFlashMaterial)
            {
                _flashRenderers[index].sharedMaterial = _flashOriginalMaterials[index];
                _flashRenderers[index].SetPropertyBlock(_flashOriginalProperties[index]);
            }

            _flashEndTimes[index] = 0f;
        }
        _activeFlashIndices.Clear();
    }

    /// <summary>
    /// 주먹 이미지를 생성 및 이동시켜 내려치고 충돌 시 onImpact를 호출한다.
    /// localContact 좌표와 설정된 주먹 애니메이션 타이밍 및 높이를 사용한다.
    /// 타격 순간 FistImpact 이벤트를 발행하고 타격 완료 시 풀에 주먹 객체를 반환한다.
    /// </summary>
    private IEnumerator StrikeKeyboard(Vector3 localContact, Action onImpact)
    {
        Camera viewCamera = Camera.main;
        Texture2D texture = _fistSprite.texture;
        float fistHeight = _fistKeycapWidthMultiplier > 0f && _standardKeycapWidth > 0f
            ? _standardKeycapWidth * _fistKeycapWidthMultiplier * texture.height / texture.width
            : _fistHeight;
        float fistLift = _fistLift * (_keyboard != null ? _keyboard.transform.lossyScale.y : transform.lossyScale.y);
        float appearDuration = _appearDuration;
        float strikeDuration = _strikeDuration;
        float holdDuration = _retreatDuration;
        Vector3 localImpactContact = localContact + _keycapFistImpactOffset;

        MeshRenderer fist = GetFistRenderer();
        fist.sharedMaterial = _fistMaterial;
        fist.sharedMaterial.SetShaderPassEnabled("ShadowCaster", true);
        fist.receiveShadows = true;
        fist.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

        fist.transform.localScale = new Vector3(fistHeight * texture.width / texture.height, fistHeight, 1f);
        _fistProperties.Clear();
        _fistProperties.SetTexture(_baseMapProperty, texture);
        _fistProperties.SetVector(_baseMapTransformProperty, new Vector4(1f, -1f, 0f, 1f));
        fist.SetPropertyBlock(_fistProperties);

        Transform root = _keyboard != null ? _keyboard.transform : transform;
        UpdateFistPose(fist, viewCamera, root.TransformPoint(localImpactContact), fistLift, 0f);
        fist.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < appearDuration)
        {
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Mathf.Min(Time.deltaTime, appearDuration * MAX_ANIMATION_PROGRESS_PER_FRAME);
            float progress = Mathf.Clamp01(elapsed / appearDuration);
            UpdateFistPose(fist, viewCamera, root.TransformPoint(localImpactContact), fistLift, progress);
        }

        elapsed = 0f;
        while (elapsed < strikeDuration)
        {
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Mathf.Min(Time.deltaTime, strikeDuration * MAX_ANIMATION_PROGRESS_PER_FRAME);
            float progress = Mathf.Clamp01(elapsed / strikeDuration);
            float strikeProgress = progress * progress;
            Vector3 contactPoint = root.TransformPoint(localImpactContact);
            UpdateFistPose(fist, viewCamera, contactPoint, fistLift * (1f - strikeProgress), 1f);
        }

        Vector3 finalContact = root.TransformPoint(localImpactContact);
        UpdateFistPose(fist, viewCamera, finalContact, 0f, 1f);
        FistImpact?.Invoke(finalContact);
        onImpact?.Invoke();

        elapsed = 0f;
        while (elapsed < holdDuration)
        {
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Time.deltaTime;
        }

        fist.gameObject.SetActive(false);
        _availableFists.Push(fist);
    }

    /// <summary>
    /// 현재 키보드의 키캡 외피 폭을 키보드 로컬 단위로 측정한다.
    /// 외피 메시와 변환값으로 구한 폭의 중앙값을 일반 키캡 폭으로 저장한다.
    /// </summary>
    private void CacheStandardKeycapWidth()
    {
        List<float> widths = new List<float>();
        foreach (KeycapHealth keycap in _keyboard.GetComponentsInChildren<KeycapHealth>(true))
        {
            Transform shell = keycap.transform.Find("PBT_SculptedShell");
            MeshFilter filter = shell.GetComponent<MeshFilter>();
            Vector3 worldWidth = shell.TransformVector(Vector3.right * filter.sharedMesh.bounds.size.x);
            widths.Add(_keyboard.transform.InverseTransformVector(worldWidth).magnitude);
        }
        widths.Sort();
        _standardKeycapWidth = widths.Count > 0 ? widths[widths.Count / 2] : 0f;
    }

    /// <summary>
    /// 망치의 impactRotation과 실제 크기로 머리 접촉점이 contact에 닿는 손잡이 위치를 반환한다.
    /// Quad 단위의 gripLocal과 headLocal에 부모 스케일까지 적용하며 망치 회전을 타격 각도로 변경한다.
    /// </summary>
    private Vector3 GetHammerImpactGrip(MeshRenderer hammer, Quaternion impactRotation, Vector3 contact, Vector3 gripLocal, Vector3 headLocal)
    {
        hammer.transform.rotation = impactRotation;
        return contact - hammer.transform.TransformVector(headLocal - gripLocal);
    }

    /// <summary>
    /// 망치 이미지를 회전시켜 목표 위치를 타격하고 충돌 순간 onImpact를 호출한다.
    /// localContact 좌표와 설정된 각도 및 지속 시간을 사용하여 포즈를 갱신한다.
    /// 완료 시 망치 렌더러를 재사용 풀로 반환한다.
    /// </summary>
    private IEnumerator SwingHammer(Vector3 localContact, Action onImpact)
    {
        HammerAnimationSettings settings = _hammerAnimation;
        float height = Mathf.Max(0.01f, settings.Height);
        Texture2D texture = _hammerSprite.texture;
        float width = height * texture.width / texture.height;
        Vector3 gripLocal = new Vector3(settings.GripPoint.x - 0.5f, settings.GripPoint.y - 0.5f, 0f);
        Vector3 headLocal = new Vector3(settings.HeadContact.x - 0.5f, settings.HeadContact.y - 0.5f, 0f);

        float startAngle = settings.StartAngle;
        float impactAngle = settings.ImpactAngle;
        float appearDuration = Mathf.Max(0.01f, settings.AppearDuration);
        float prepareDuration = Mathf.Max(0f, settings.PrepareDuration);
        float swingDuration = Mathf.Max(0.01f, settings.SwingDuration);
        float holdDuration = Mathf.Max(0f, settings.HoldDuration);

        Camera viewCamera = Camera.main;
        Quaternion baseRotation = GetAttackRotation(viewCamera);
        Quaternion impactRotation = baseRotation * Quaternion.Euler(0f, 0f, impactAngle);
        Quaternion startRotation = baseRotation * Quaternion.Euler(0f, 0f, startAngle);
        Vector3 startOffset = baseRotation * Vector3.right * settings.SideOffset + Vector3.up * settings.Lift;

        MeshRenderer hammer = GetFistRenderer();
        hammer.gameObject.name = "KeyboardKeycapHammer";
        hammer.sharedMaterial = _fistMaterial;
        hammer.sharedMaterial.SetShaderPassEnabled("ShadowCaster", true);
        hammer.receiveShadows = true;
        hammer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        hammer.transform.localScale = new Vector3(width, height, 1f);

        _fistProperties.Clear();
        _fistProperties.SetTexture(_baseMapProperty, texture);
        _fistProperties.SetVector(_baseMapTransformProperty, new Vector4(1f, 1f, 0f, 0f));
        hammer.SetPropertyBlock(_fistProperties);

        Transform root = _keyboard != null ? _keyboard.transform : transform;
        Vector3 impactGrip = GetHammerImpactGrip(hammer, impactRotation, root.TransformPoint(localContact), gripLocal, headLocal);
        UpdateHammerPose(hammer, startRotation, impactGrip + startOffset, gripLocal, 0f);
        hammer.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < appearDuration)
        {
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Mathf.Min(Time.deltaTime, appearDuration * MAX_ANIMATION_PROGRESS_PER_FRAME);
            float progress = Mathf.Clamp01(elapsed / appearDuration);
            impactGrip = GetHammerImpactGrip(hammer, impactRotation, root.TransformPoint(localContact), gripLocal, headLocal);
            UpdateHammerPose(hammer, startRotation, impactGrip + startOffset * (1f - progress), gripLocal, progress);
        }

        elapsed = 0f;
        while (elapsed < prepareDuration)
        {
            impactGrip = GetHammerImpactGrip(hammer, impactRotation, root.TransformPoint(localContact), gripLocal, headLocal);
            UpdateHammerPose(hammer, startRotation, impactGrip, gripLocal, 1f);
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Time.deltaTime;
        }

        elapsed = 0f;
        while (elapsed < swingDuration)
        {
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Mathf.Min(Time.deltaTime, swingDuration * MAX_ANIMATION_PROGRESS_PER_FRAME);
            float progress = Mathf.Clamp01(elapsed / swingDuration);
            progress *= progress;
            Quaternion rotation = baseRotation * Quaternion.Euler(0f, 0f, Mathf.Lerp(startAngle, impactAngle, progress));
            impactGrip = GetHammerImpactGrip(hammer, impactRotation, root.TransformPoint(localContact), gripLocal, headLocal);
            UpdateHammerPose(hammer, rotation, impactGrip, gripLocal, 1f);
        }

        impactGrip = GetHammerImpactGrip(hammer, impactRotation, root.TransformPoint(localContact), gripLocal, headLocal);
        UpdateHammerPose(hammer, impactRotation, impactGrip, gripLocal, 1f);
        onImpact?.Invoke();

        elapsed = 0f;
        while (elapsed < holdDuration)
        {
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += Time.deltaTime;
        }

        hammer.gameObject.SetActive(false);
        _availableFists.Push(hammer);
    }

    /// <summary>
    /// 중심에서 바깥으로 퍼져나가는 충격파 링과 키캡 물결 변위를 계산하여 표시한다.
    /// keyboard와 keycaps 배열, positions 좌표, center 중심과 planeHeight 높이, onKeycapReached 콜백을 사용한다.
    /// travelDuration으로 전파 시간을 조정하고 onStarted, onKeycapReached, onFinished를 순서대로 호출하며 종료 시 변위를 복원한다.
    /// </summary>
    private IEnumerator SpreadHammerShockwave(KeyboardInputController keyboard, KeycapHealth[] keycaps, Vector2[] positions, Vector2 center, float planeHeight, Action<KeycapHealth> onKeycapReached, bool showRing, bool requireInput, Action onStarted, float travelDuration, Action onFinished)
    {
        float speed = Mathf.Max(0.01f, _hammerAnimation.WaveSpeed);
        float height = _hammerAnimation.WaveHeight;
        float rippleDuration = Mathf.Max(0.01f, _hammerAnimation.WaveDuration);
        Color color = _hammerAnimation.WaveColor;

        ShockwaveBuffer buffer = GetShockwaveBuffer(keycaps.Length);
        float[] arrivalTimes = buffer.ArrivalTimes;
        float[] contributions = buffer.Contributions;
        bool[] hit = buffer.Hit;
        Vector3[] points = buffer.Points;
        float maxArrival = 0f;

        for (int index = 0; index < keycaps.Length; index++)
        {
            arrivalTimes[index] = Vector2.Distance(center, positions[index]) / speed;
            maxArrival = Mathf.Max(maxArrival, arrivalTimes[index]);
        }

        if (travelDuration > 0f && maxArrival > 0f)
        {
            float scale = travelDuration / maxArrival;
            for (int index = 0; index < keycaps.Length; index++) arrivalTimes[index] *= scale;
            speed /= scale;
            maxArrival = travelDuration;
        }

        float duration = maxArrival + rippleDuration;
        LineRenderer ring = showRing ? GetShockwaveRenderer() : null;
        if (ring != null)
        {
            ring.widthMultiplier = _hammerAnimation.WaveWidth;
            ring.enabled = _hammerAnimation.ShowWaveRing;
            ring.gameObject.SetActive(true);
        }

        float elapsed = 0f;
        bool started = false;

        while (elapsed <= duration && (_keyboardDestruction == null || !_keyboardDestruction.IsBroken) && keyboard.gameObject.activeInHierarchy && (!requireInput || (keyboard.InputEnabled && keyboard.isActiveAndEnabled)))
        {
            if (!started)
            {
                started = true;
                onStarted?.Invoke();
            }
            for (int index = 0; index < keycaps.Length; index++)
            {
                if (_keyboardDestruction != null && _keyboardDestruction.IsBroken)
                {
                    break;
                }

                KeycapHealth keycap = keycaps[index];
                if (keycap == null)
                {
                    continue;
                }

                float age = elapsed - arrivalTimes[index];
                if (age >= 0f && !hit[index])
                {
                    hit[index] = true;
                    onKeycapReached?.Invoke(keycap);
                }

                if ((_keyboardDestruction != null && _keyboardDestruction.IsBroken) || !keyboard.gameObject.activeInHierarchy || (requireInput && (!keyboard.InputEnabled || !keyboard.isActiveAndEnabled)))
                {
                    break;
                }

                float phase = Mathf.Clamp01(age / rippleDuration);
                float offset = age >= 0f && keycap.CurrentHP > 0 ? Mathf.Sin(phase * Mathf.PI * 2f) * height * (1f - phase) : 0f;
                keyboard.AddKeycapWaveOffset(keycap.transform, offset - contributions[index]);
                contributions[index] = offset;
            }

            if (ring != null) ring.enabled = _hammerAnimation.ShowWaveRing;
            if (ring != null && ring.enabled)
            {
                float radius = Mathf.Max(0.02f, elapsed * speed);
                for (int index = 0; index < points.Length; index++)
                {
                    float angle = index * Mathf.PI * 2f / points.Length;
                    points[index] = new Vector3(center.x + Mathf.Cos(angle) * radius, planeHeight, center.y + Mathf.Sin(angle) * radius);
                }

                ring.SetPositions(points);
                Color visibleColor = color;
                visibleColor.a *= Mathf.Clamp01(1f - elapsed / duration);
                ring.startColor = visibleColor;
                ring.endColor = visibleColor;
            }

            yield return null;

            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            elapsed += travelDuration > 0f ? Time.deltaTime : Mathf.Min(Time.deltaTime, 1f / 30f);
        }

        if (keyboard.gameObject.activeInHierarchy && (!requireInput || (keyboard.InputEnabled && keyboard.isActiveAndEnabled)))
        {
            for (int index = 0; index < keycaps.Length; index++)
            {
                if (keycaps[index] != null)
                {
                    keyboard.AddKeycapWaveOffset(keycaps[index].transform, -contributions[index]);
                }
            }
        }

        if (ring != null)
        {
            ring.gameObject.SetActive(false);
            _availableShockwaves.Push(ring);
        }
        ReleaseShockwaveBuffer(buffer);
        onFinished?.Invoke();
    }

    /// <summary>
    /// 충격파 연산에 사용할 캐시된 작업 버퍼를 반환하고 필요한 크기로 준비한다.
    /// count 크기에 맞추어 도착 시각, 변위 기여도 및 타격 여부 배열을 초기화한다.
    /// </summary>
    private ShockwaveBuffer GetShockwaveBuffer(int count)
    {
        ShockwaveBuffer buffer = _availableShockwaveBuffers.Count > 0 ? _availableShockwaveBuffers.Pop() : new ShockwaveBuffer();
        buffer.EnsureCapacity(count);
        return buffer;
    }

    /// <summary>
    /// 사용이 끝난 충격파 작업 버퍼를 대기 스택에 반환하여 다음 충격파에서 재사용한다.
    /// buffer를 _availableShockwaveBuffers에 보관한다.
    /// </summary>
    private void ReleaseShockwaveBuffer(ShockwaveBuffer buffer)
    {
        if (buffer != null)
        {
            _availableShockwaveBuffers.Push(buffer);
        }
    }

    /// <summary>
    /// 충격파 연산에 필요한 포인트 배열과 키캡 연산 작업 배열을 보관하는 재사용 버퍼이다.
    /// </summary>
    private sealed class ShockwaveBuffer
    {
        public readonly Vector3[] Points = new Vector3[SHOCKWAVE_SEGMENTS];
        public float[] ArrivalTimes = System.Array.Empty<float>();
        public float[] Contributions = System.Array.Empty<float>();
        public bool[] Hit = System.Array.Empty<bool>();

        /// <summary>
        /// 작업 배열의 크기가 count 이상이 되도록 보장하고 내용을 초기화한다.
        /// </summary>
        public void EnsureCapacity(int count)
        {
            if (ArrivalTimes.Length < count)
            {
                ArrivalTimes = new float[count];
                Contributions = new float[count];
                Hit = new bool[count];
            }
            else
            {
                System.Array.Clear(ArrivalTimes, 0, count);
                System.Array.Clear(Contributions, 0, count);
                System.Array.Clear(Hit, 0, count);
            }
        }
    }

    /// <summary>
    /// 재사용 가능한 LineRenderer를 반환하거나 없으면 새로 생성한다.
    /// 투명 재질과 정렬 순서를 설정하고 목록에 등록한다.
    /// LineRenderer 인스턴스를 반환한다.
    /// </summary>
    private LineRenderer GetShockwaveRenderer()
    {
        if (_availableShockwaves.Count > 0)
        {
            return _availableShockwaves.Pop();
        }

        Transform parentTransform = _keyboard != null ? _keyboard.transform : transform;
        GameObject ringObject = new GameObject("HammerShockwave");
        ringObject.transform.SetParent(parentTransform, false);
        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = SHOCKWAVE_SEGMENTS;
        ring.sharedMaterial = _hammerShockwaveMaterial;
        ring.sortingOrder = FIST_SORTING_ORDER;
        _shockwaveRenderers.Add(ring);
        return ring;
    }

    /// <summary>
    /// 대기 중인 주먹 렌더러를 반환하거나 없으면 새 Quad 메시 객체를 생성한다.
    /// 생성된 주먹은 정리 목록에 추가하고 Collider는 제거한다.
    /// MeshRenderer 인스턴스를 반환한다.
    /// </summary>
    private MeshRenderer GetFistRenderer()
    {
        EnsureFistResources();
        if (_availableFists.Count > 0)
        {
            return _availableFists.Pop();
        }

        Transform parentTransform = _keyboard != null ? _keyboard.transform : transform;
        GameObject fistObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fistObject.name = "KeyboardKeycapFist";
        fistObject.transform.SetParent(parentTransform, false);
        Destroy(fistObject.GetComponent<Collider>());
        MeshRenderer fist = fistObject.GetComponent<MeshRenderer>();
        fist.sortingOrder = FIST_SORTING_ORDER;
        fist.receiveShadows = true;
        _fistRenderers.Add(fist);
        return fist;
    }

    /// <summary>
    /// 주먹 풀과 속성 블록 컬렉션을 준비한다.
    /// null인 목록들을 새로 인스턴스화한다.
    /// 반환값은 없다.
    /// </summary>
    private void EnsureFistResources()
    {
        if (_fistRenderers == null)
        {
            _fistRenderers = new List<MeshRenderer>();
        }

        if (_availableFists == null)
        {
            _availableFists = new Stack<MeshRenderer>();
        }

        if (_fistProperties == null)
        {
            _fistProperties = new MaterialPropertyBlock();
        }
    }

    /// <summary>
    /// fist 렌더러를 타격 위치 및 카메라 방향으로 정렬하고 투명도를 설정한다.
    /// fist의 실제 변환 크기와 viewCamera, contact, lift, alpha를 사용하여 이미지 하단을 접촉점에 맞춘다.
    /// 렌더러의 위치와 회전을 변경한다.
    /// </summary>
    private void UpdateFistPose(MeshRenderer fist, Camera viewCamera, Vector3 contact, float lift, float alpha)
    {
        Quaternion rotation = GetAttackRotation(viewCamera);
        fist.transform.SetPositionAndRotation(contact + Vector3.up * lift, rotation);
        fist.transform.position -= fist.transform.TransformVector(Vector3.down * 0.5f);
        fist.GetPropertyBlock(_fistProperties);
        _fistProperties.SetColor(_baseColorProperty, new Color(1f, 1f, 1f, alpha));
        fist.SetPropertyBlock(_fistProperties);
    }

    /// <summary>
    /// hammer 렌더러를 회전 및 그립 위치에 맞춰 배치하고 투명도를 설정한다.
    /// hammer 렌더러, rotation, gripWorld, Quad 단위 gripLocal과 alpha를 사용하여 실제 스케일로 손잡이를 배치한다.
    /// 렌더러의 위치와 회전을 변경한다.
    /// </summary>
    private void UpdateHammerPose(MeshRenderer hammer, Quaternion rotation, Vector3 gripWorld, Vector3 gripLocal, float alpha)
    {
        hammer.transform.SetPositionAndRotation(gripWorld, rotation);
        hammer.transform.position -= hammer.transform.TransformVector(gripLocal);
        hammer.GetPropertyBlock(_fistProperties);
        _fistProperties.SetColor(_baseColorProperty, new Color(1f, 1f, 1f, alpha));
        hammer.SetPropertyBlock(_fistProperties);
    }

    /// <summary>
    /// 타격 연출 이미지가 카메라 수평면을 바라보도록 하는 회전값을 계산한다.
    /// viewCamera 시점 카메라를 사용한다.
    /// 계산된 Quaternion 회전값을 반환한다.
    /// </summary>
    private Quaternion GetAttackRotation(Camera viewCamera)
    {
        Vector3 forward = viewCamera != null ? viewCamera.transform.forward : transform.forward;
        Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.0001f)
        {
            horizontalForward = Vector3.forward;
        }

        return Quaternion.LookRotation(horizontalForward, Vector3.up);
    }

    [Serializable]
    public class HammerAnimationSettings
    {
        [Header("Size and Movement")]
        [SerializeField, Min(0.01f)] private float _height = 1.8f;
        [SerializeField] private float _sideOffset = -1f;
        [SerializeField, Min(0f)] private float _lift = 0.45f;
        [SerializeField] private float _startAngle = -45f;
        [SerializeField] private float _impactAngle = -135f;
        [SerializeField] private Vector2 _headContact = new Vector2(0.18f, 0.78f);
        [SerializeField] private Vector2 _gripPoint = new Vector2(0.48f, 0.1f);

        public float Height => _height;
        public float SideOffset => _sideOffset;
        public float Lift => _lift;
        public float StartAngle => _startAngle;
        public float ImpactAngle => _impactAngle;
        public Vector2 HeadContact => _headContact;
        public Vector2 GripPoint => _gripPoint;

        [Header("Timing")]
        [SerializeField, Min(0.01f)] private float _appearDuration = 0.12f;
        [SerializeField, Min(0f)] private float _prepareDuration = 0.1f;
        [SerializeField, Min(0.01f)] private float _swingDuration = 0.18f;
        [SerializeField, Min(0f)] private float _holdDuration = 0.15f;

        public float AppearDuration => _appearDuration;
        public float PrepareDuration => _prepareDuration;
        public float SwingDuration => _swingDuration;
        public float HoldDuration => _holdDuration;

        [Header("Shockwave")]
        [SerializeField] private bool _showWaveRing = true;
        [SerializeField, Min(0.01f)] private float _waveSpeed = 22f;
        [SerializeField, Min(0f)] private float _waveHeight = 0.25f;
        [SerializeField, Min(0.01f)] private float _waveDuration = 0.35f;
        [SerializeField, Min(0.001f)] private float _waveWidth = 0.03f;
        [SerializeField] private Color _waveColor = new Color(1f, 0.75f, 0.25f, 1f);

        public float WaveSpeed => _waveSpeed;
        public bool ShowWaveRing => _showWaveRing;
        public float WaveHeight => _waveHeight;
        public float WaveDuration => _waveDuration;
        public float WaveWidth => _waveWidth;
        public Color WaveColor => _waveColor;
    }
}
