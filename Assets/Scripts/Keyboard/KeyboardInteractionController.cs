using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

using Unity.Cinemachine;

using Game.Economy;
using Game.Session;
using Game.Shop;
using Game.Upgrades;
using KeyboardModeling;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(BoxCollider))]
public sealed class KeyboardInteractionController : MonoBehaviour
{
    [Header("Camera and Pad")]
    [SerializeField] private Camera _camera;
    [SerializeField] private CinemachineCamera _fixedCam;
    [SerializeField] private GameObject _referenceKeyboard;
    private Vector3 _padPosition;
    private Quaternion _padRotation;
    private Bounds _keyboardBounds;
    private BoxCollider _padCollider;
    private KeyboardState _placedKeyboard;
    public bool IsSmashMode { get; private set; }
    public KeyboardInputController ActiveKeyboard => IsSmashMode ? _placedKeyboard.Keyboard : null;
    public KeyboardInputController PlacedKeyboard => _placedKeyboard != null ? _placedKeyboard.Keyboard : null;

    public event Action<KeyboardInputController> ActiveKeyboardChanged;
    public event Action<KeyboardInputController> PlacedKeyboardChanged;

    [Header("Pickup")]
    [SerializeField, Min(0.1f)] private float _rayDistance = 12f;
    [SerializeField, Min(0.1f)] private float _holdDistance = 3f;
    [SerializeField, Min(0.1f)] private float _followSpeed = 12f;
    [SerializeField] private Vector3 _holdEulerAngles = new Vector3(-75f, 0f, 0f);
    [SerializeField] private LayerMask _pickupLayers = ~0;
    private KeyboardState _heldKeyboard;
    private Vector3 _pickupPosition;
    private Quaternion _pickupRotation;
    private bool _canPlaceHeldKeyboard;
    private readonly Dictionary<Rigidbody, KeyboardState> _keyboards = new Dictionary<Rigidbody, KeyboardState>();
    public bool IsHoldingKeyboard => _heldKeyboard != null;
    public int SpawnedKeyboardCount { get; private set; }

    [Header("Spawn")]
    [SerializeField] private GameObject _keyboardPrefab;
    [SerializeField] private GameObject _inGameKeyBoardPrefab;
    [SerializeField] private BoxCollider _front;
    [SerializeField] private BoxCollider _left;
    [SerializeField] private BoxCollider _right;
    [SerializeField] private BoxCollider _behind;
    [SerializeField, Min(0)] private int _frontCount = 20;
    [SerializeField, Min(0)] private int _leftCount = 20;
    [SerializeField, Min(0)] private int _rightCount = 20;
    [SerializeField, Min(0)] private int _behindCount = 40;
    private Transform _spawnRoot;

    [Header("Keycap Quality")]
    [SerializeField] private GameSession _gameSession;
    [SerializeField] private KeyboardRewardController _rewardController;
    [SerializeField] private KeycapRaritySettings _raritySettings;
    private readonly System.Random _qualityRandom = new System.Random();
    private bool _isPlacingKeyboard;

    [Header("Keyboard destruction")]
    [SerializeField] private GameObject _keyboardFragmentsPrefab;
    [Header("monitor")]
    [SerializeField] private MiniGameController _controller;
    public bool IsMiniGameRunning => _controller.CurrentGame != MiniGameController.MiniGameKind.Idle;
    private bool IsKeyboardBreaking => _placedKeyboard != null && _placedKeyboard.Keyboard.GetComponent<KeyboardDestruction>().IsBroken;
    public bool CanEnterShop => IsSmashMode && !IsMiniGameRunning && !IsKeyboardBreaking;
    [Header("Shop Focus")]
    [SerializeField] private ShopView _shopView;
    private bool _isShopFocused;
    private bool _resumeSmashInput;
    private KeyboardAutomaticStation _viewingAutomaticStation;


    void Awake()
    {
        _gameSession = FindAnyObjectByType<GameSession>();
        _rewardController = FindAnyObjectByType<KeyboardRewardController>();
        _controller = FindAnyObjectByType<MiniGameController>();
        _padPosition = _referenceKeyboard.transform.position;
        _padRotation = _referenceKeyboard.transform.rotation;
        _keyboardBounds = CalculateKeyboardBounds(_referenceKeyboard.transform);
        _padCollider = GetComponent<BoxCollider>();

        _padCollider.isTrigger = true;
        _fixedCam.Priority = 0;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start()
    {
        //_placedKeyboard = RegisterKeyboard(_referenceKeyboard.GetComponent<KeyboardInputController>());
        _placedKeyboard = null;
        _spawnRoot = new GameObject("SpawnedKeyboards").transform;
        SpawnKeyboards(_front, _frontCount, "front");
        SpawnKeyboards(_left, _leftCount, "left");
        SpawnKeyboards(_right, _rightCount, "right");
        SpawnKeyboards(_behind, _behindCount, "behind");
        Debug.Log($"[Keyboard] Spawned {SpawnedKeyboardCount} keyboards.", this);
    }

    void Update()
    {
        if (!Application.isFocused) return;
#if UNITY_EDITOR
        if (EditorApplication.isPaused || EditorWindow.focusedWindow == null || EditorWindow.focusedWindow.GetType().Name != "GameView") return;
#endif
        bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (_viewingAutomaticStation != null)
        {
            if (escapePressed)
            {
                _viewingAutomaticStation.SetViewActive(false);
                _viewingAutomaticStation = null;
            }
            return;
        }
        if (_isShopFocused)
        {
            return;
        }

        if (_resumeSmashInput)
        {
            bool tabPressedOrHeld = Keyboard.current != null && (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.tabKey.isPressed);

            if (tabPressedOrHeld)
            {
                return;
            }

            _resumeSmashInput = false;

            if (IsSmashMode && _placedKeyboard != null)
            {
                KeyboardDestruction destruction = _placedKeyboard.Keyboard.GetComponent<KeyboardDestruction>();

                if (!destruction.IsBroken)
                {
                    SetKeyboardInput(_placedKeyboard, true);
                }
            }
        }
        if (IsSmashMode && escapePressed)
        {
            if (!IsMiniGameRunning && !IsKeyboardBreaking)
            {
                OnExitSmashMode();
            }

            return;
        }
        if (IsSmashMode || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (_heldKeyboard != null) DropKeyboard();
        else PickUpKeyboard();
    }

    void FixedUpdate()
    {
        if (_heldKeyboard == null) return;
        Quaternion rotation = _camera.transform.rotation * Quaternion.Euler(_holdEulerAngles);
        Vector3 center = _camera.transform.position + _camera.transform.forward * _holdDistance;
        Vector3 position = center - rotation * Vector3.Scale(_heldKeyboard.Collider.center, _heldKeyboard.Body.transform.lossyScale);
        float amount = 1f - Mathf.Exp(-_followSpeed * Time.fixedDeltaTime);
        _heldKeyboard.Body.MovePosition(Vector3.Lerp(_heldKeyboard.Body.position, position, amount));
        _heldKeyboard.Body.MoveRotation(Quaternion.Slerp(_heldKeyboard.Body.rotation, rotation, amount));
    }

    //void OnTriggerEnter(Collider other) { TryPlaceKeyboard(other); }
    void OnTriggerStay(Collider other) { TryPlaceKeyboard(other); }
    void OnTriggerExit(Collider other) { if (_heldKeyboard != null && other.attachedRigidbody == _heldKeyboard.Body) _canPlaceHeldKeyboard = true; }

    void OnEnable()
    {
        _shopView.OpenStateChanged += ApplyShopFocus;
        ApplyShopFocus(_shopView.IsOpen);
    }

    void OnDisable()
    {
        if (_viewingAutomaticStation != null)
        {
            _viewingAutomaticStation.SetViewActive(false);
            _viewingAutomaticStation = null;
        }
        _shopView.OpenStateChanged -= ApplyShopFocus;

        if (_heldKeyboard != null)
        {
            DropKeyboard();
        }

        if (IsSmashMode)
        {
            OnExitSmashMode();
        }
    }

    /// <summary>
    /// keyboard의 입력과 키캡 Collider를 끄고 들기용 물리 상태를 구성하여 등록 상태를 반환한다.
    /// isPlayable이 true인 경우에만 실제 입력 키캡의 전체 파괴 판정을 초기화한다.
    /// </summary>
    private KeyboardState RegisterKeyboard(KeyboardInputController keyboard, bool isPlayable = false)
    {

        keyboard.SetInputEnabled(false);
        keyboard.enabled = false;
        Collider[] colliders = keyboard.GetComponentsInChildren<Collider>();
        bool[] colliderEnabled = new bool[colliders.Length];
        for (int index = 0; index < colliders.Length; index++) { colliderEnabled[index] = colliders[index].enabled; colliders[index].enabled = false; }
        Rigidbody body = keyboard.GetComponent<Rigidbody>();
        if (body == null) body = keyboard.gameObject.AddComponent<Rigidbody>();
        BoxCollider collider = keyboard.GetComponent<BoxCollider>();
        if (collider == null) collider = keyboard.gameObject.AddComponent<BoxCollider>();
        collider.center = _keyboardBounds.center;
        collider.size = _keyboardBounds.size;
        collider.isTrigger = false;
        collider.enabled = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        KeyboardState state = new KeyboardState(keyboard, body, collider, colliders, colliderEnabled);
        _keyboards.Add(body, state);
        if (isPlayable)
        {
            KeyboardDestruction destruction = keyboard.gameObject.AddComponent<KeyboardDestruction>();
            destruction.Initialize(keyboard.GetDestructionKeycaps(), _keyboardFragmentsPrefab, () => HandleKeyboardBreaking(state), () => HandleKeyboardDestroyed(state));
        }
        return state;
    }

    /// <summary>
    /// state의 입력을 끄고 들기 목록에서 제거한다.
    /// 배치된 키보드가 파괴되는 경우 입력 복원 예약을 해제하고,
    /// 미니게임의 입력 대상만 해제하여 진행도와 피버 상태를 보존한다.
    /// </summary>
    private void HandleKeyboardBreaking(KeyboardState state)
    {
        SetKeyboardInput(state, false);
        _keyboards.Remove(state.Body);

        if (_heldKeyboard == state)
        {
            _heldKeyboard = null;
        }

        if (_placedKeyboard == state)
        {
            _resumeSmashInput = false;
            _controller.SetKeyBoard(null);
        }
    }

    /// <summary>
    /// state가 Pad 키보드이면 파편의 -Z 이동 시작 후 SmashMode를 종료하고 배치 참조를 비운다.
    /// 보존된 미니게임 상태와 관계없이 새 키보드를 집고 배치할 수 있도록 교체 상태를 해제한다.
    /// </summary>
    private void HandleKeyboardDestroyed(KeyboardState state)
    {
        if (_placedKeyboard != state) return;

        _gameSession.CompleteKeyboard();

        if (IsSmashMode) OnExitSmashMode();
        _placedKeyboard = null;
        PlacedKeyboardChanged?.Invoke(null);
    }

    /// <summary>area의 로컬 중심과 크기 안에서 count개를 임의 위치와 회전으로 생성한다. 영역이 없으면 areaName을 알리고 해당 영역 생성을 건너뛴다.</summary>
    private void SpawnKeyboards(BoxCollider area, int count, string areaName)
    {
        if (area == null) { Debug.LogWarning($"[Keyboard] Assign the {areaName} BoxCollider to spawn {count} keyboards.", this); return; }
        area.isTrigger = true;
        for (int index = 0; index < count; index++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f) * area.size.x, Random.Range(-0.5f, 0.5f) * area.size.y, Random.Range(-0.5f, 0.5f) * area.size.z);
            Vector3 position = area.transform.TransformPoint(area.center + offset);
            GameObject keyboard = Instantiate(_keyboardPrefab, position, Random.rotationUniform, _spawnRoot);
            keyboard.name = $"{areaName}_Keyboard_{index + 1}";
            keyboard.transform.localScale = _referenceKeyboard.transform.lossyScale;
            RegisterKeyboard(keyboard.GetComponent<KeyboardInputController>());
            SpawnedKeyboardCount++;
        }
    }

    /// <summary>카메라 중앙 Ray의 첫 충돌이 등록된 키보드이면 집고, 교환에 사용할 원래 위치 및 회전을 저장한다.</summary>
    private void PickUpKeyboard()
    {
        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _pickupLayers, QueryTriggerInteraction.Ignore)) return;
        KeyboardAutomaticStation station = hit.collider.GetComponentInParent<KeyboardAutomaticStation>();
        if (station != null)
        {
            // 자세히 보기 진입은 임시로 중단한다.
            // _viewingAutomaticStation = station;
            // station.SetViewActive(true);
            return;
        }
        if (hit.rigidbody == null || !_keyboards.TryGetValue(hit.rigidbody, out KeyboardState state)) return;
        if (state == _placedKeyboard)
        {
            OnEnterSmashMode();
            return;
        }
        _heldKeyboard = state;
        state.AllowPlacementRetry();
        _pickupPosition = state.Body.position;
        _pickupRotation = state.Body.rotation;
        if (!state.Body.isKinematic) { state.Body.linearVelocity = Vector3.zero; state.Body.angularVelocity = Vector3.zero; }
        state.Body.isKinematic = true;
        state.Body.useGravity = false;
        _canPlaceHeldKeyboard = !Physics.ComputePenetration(state.Collider, state.Body.position, state.Body.rotation, _padCollider, _padCollider.transform.position, _padCollider.transform.rotation, out _, out _);
        if (_placedKeyboard == state)
        {
            _placedKeyboard = null;
            PlacedKeyboardChanged?.Invoke(null);
        }
    }
    private GameObject lastDroped;

    /// <summary>
    /// 놓은 월드 키보드를 빈 자동작업대에 넣고 플레이어 회수 대상에서 제외한다.
    /// other와 station으로 기존 품질 생성 및 배치 로직을 사용하며 수동 스매쉬 모드와 입력 이벤트는 켜지 않는다.
    /// </summary>
    public void TryPlaceAutomaticKeyboard(Collider other, KeyboardAutomaticStation station)
    {
        if (_isShopFocused || _isPlacingKeyboard || _viewingAutomaticStation != null || station.HasKeyboard || other.gameObject != lastDroped)
            return;
        if (other.attachedRigidbody == null || !_keyboards.TryGetValue(other.attachedRigidbody, out KeyboardState source) || source.PlacementFailed)
            return;
        _isPlacingKeyboard = true;
        try
        {
            KeyboardState target = CreatePlayableKeyboard(source, false);
            _keyboards.Remove(target.Body);
            station.AcceptKeyboard(target.Keyboard);
            _keyboards.Remove(source.Body);
            source.Collider.enabled = false;
            lastDroped = null;
            _heldKeyboard = null;
            Destroy(source.Keyboard.gameObject);
        }
        catch (Exception exception)
        {
            source.MarkPlacementFailed();
            Debug.LogException(exception, this);
        }
        finally
        {
            _isPlacingKeyboard = false;
        }
    }

    /// <summary>현재 키보드의 카메라 추적을 중단하고 Rigidbody를 중력에 따라 움직이는 상태로 전환한다.</summary>
    private void DropKeyboard()
    {
        _heldKeyboard.Body.isKinematic = false;
        _heldKeyboard.Body.useGravity = true;
        lastDroped = _heldKeyboard.Collider.gameObject;
        _heldKeyboard = null;


    }

    /// <summary>
    /// other가 배치 가능한 월드 키보드이면 현재 품질 강화 분포로 플레이용 키보드를 생성한다.
    /// 새 객체 준비가 성공하면 원본 등록을 제거하고 패드에 배치하여 SmashMode를 시작한다.
    /// </summary>
    private void TryPlaceKeyboard(Collider other)
    {
        if (_isShopFocused || _isPlacingKeyboard)
            return;

        if (other.gameObject != lastDroped) return;

        if (!_canPlaceHeldKeyboard || _placedKeyboard != null)
        {

            return;
        }

        if (!_keyboards.TryGetValue(other.attachedRigidbody, out KeyboardState source) || source.PlacementFailed)
        {
            return;
        }

        _isPlacingKeyboard = true;
        KeyboardState target;
        try
        {
            target = CreatePlayableKeyboard(source);
        }
        catch (Exception exception)
        {
            source.MarkPlacementFailed();
            Debug.LogException(exception, this);
            _isPlacingKeyboard = false;
            return;
        }

        _placedKeyboard = target;
        Rigidbody body = target.Body;
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
        body.useGravity = false;
        body.position = _padPosition;
        body.rotation = _padRotation;
        _controller.SetKeyBoard(target.Keyboard);
        PlacedKeyboardChanged?.Invoke(target.Keyboard);

        _keyboards.Remove(source.Body);
        source.Collider.enabled = false;
        lastDroped = null;
        _heldKeyboard = null;
        Destroy(source.Keyboard.gameObject);
        _isPlacingKeyboard = false;
        OnEnterSmashMode();
    }

    /// <summary>
    /// source의 최초 배치에서 현재 강화 분포로 생성 결과를 확정하고 플레이용 객체에 적용한다.
    /// 재시도는 같은 프로필을 사용하며 준비 실패 시 새 객체를 정리하고 월드 원본을 유지한다.
    /// </summary>
    private KeyboardState CreatePlayableKeyboard(KeyboardState source, bool playerControlled = true)
    {
        GameObject instance = Instantiate(_inGameKeyBoardPrefab);
        try
        {
            KeyboardInputController keyboard = instance.GetComponent<KeyboardInputController>();
            keyboard.SetInputEnabled(false);
            keyboard.enabled = false;
            IReadOnlyDictionary<UnityEngine.InputSystem.Key, KeycapHealth> layout = keyboard.GetKeycapLayout();
            List<KeycapHealth> requiredKeys = keyboard.GetDestructionKeycaps();
            if (requiredKeys.Count == 0 || _keyboardFragmentsPrefab == null)
            {
                throw new InvalidOperationException("Playable keyboard requires destruction keys and fragments.");
            }

            if (source.PendingProfile == null)
            {
                KeyboardSpawnProfile profile = KeyboardProfileGenerator.GenerateProfile(
                    layout,
                    new HashSet<KeycapHealth>(requiredKeys),
                    _gameSession.GetCurrentRarityDistribution(),
                    _raritySettings,
                    _rewardController.RewardPerKeycap,
                    _gameSession.CurrentRarityStage,
                    _qualityRandom.Next());

                source.StoreProfile(profile);
            }

            keyboard.InitializeSpawnProfile(source.PendingProfile, layout);
            return RegisterKeyboard(keyboard, playerControlled);
        }
        catch
        {
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body != null)
            {
                _keyboards.Remove(body);
            }

            instance.SetActive(false);
            Destroy(instance);
            throw;
        }
    }

    /// 배치된 키보드와 고정 카메라를 사용해 부수기 모드에 진입한다.
    /// ActiveKeyboardChanged로 대상 키보드를 알린 뒤 해당 키보드의 입력을 켠다.
    private void OnEnterSmashMode()
    {
        IsSmashMode = true;
        _fixedCam.Priority = 20;

        ActiveKeyboardChanged?.Invoke(ActiveKeyboard);

        SetKeyboardInput(_placedKeyboard, true);
    }

    /// 배치된 키보드와 고정 카메라를 사용해 부수기 모드를 종료한다.
    /// 키보드 입력을 끄고 ActiveKeyboardChanged에 null을 전달해 대상 해제를 알린다.
    private void OnExitSmashMode()
    {
        IsSmashMode = false;
        _fixedCam.Priority = 0;
        SetKeyboardInput(_placedKeyboard, false);

        ActiveKeyboardChanged?.Invoke(null);
    }

    /// <summary>
    /// state의 입력 컨트롤러와 원래 키캡 Collider를 active에 따라 켜거나 끈다.
    /// 파괴된 키캡의 Collider는 복구하지 않으며 객체 정리 중 소멸한 참조는 건너뛴다.
    /// </summary>
    private void SetKeyboardInput(KeyboardState state, bool active)
    {
        if (state.Keyboard == null)
        {
            return;
        }

        for (int index = 0; index < state.Colliders.Length; index++)
        {
            if (state.Colliders[index] == null)
            {
                continue;
            }

            KeycapHealth health = state.Colliders[index].GetComponentInParent<KeycapHealth>();
            state.Colliders[index].enabled = active && state.ColliderEnabled[index] && (health == null || health.CurrentHP > 0);
        }
        KeyboardDestruction destruction = state.Keyboard.GetComponent<KeyboardDestruction>();
        if (state.Collider != null)
        {
            state.Collider.enabled = destruction == null || !destruction.IsBroken;
        }
        state.Keyboard.SetInputEnabled(active);
        state.Keyboard.enabled = active;
    }

    /// <summary>root 아래 메시의 경계 모서리를 root 로컬 좌표로 합쳐 키보드 전체를 감싸는 들기용 경계를 반환한다.</summary>
    private Bounds CalculateKeyboardBounds(Transform root)
    {
        Bounds result = new Bounds();
        bool initialized = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
        {
            Bounds bounds = filter.sharedMesh.bounds;
            Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3((corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x, (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y, (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                Vector3 point = matrix.MultiplyPoint3x4(bounds.center + offset);
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
        }
        return result;
    }

    /// <summary>
    /// isOpen으로 상점 Focus 상태를 갱신하고 키보드 입력을 중지한다.
    /// 커서 상태를 변경하며 상점을 닫으면 부수기 입력 복원을 예약한다.
    /// </summary>
    private void ApplyShopFocus(bool isOpen)
    {
        _isShopFocused = isOpen;
        _resumeSmashInput = !isOpen && IsSmashMode;

        if (_placedKeyboard != null)
        {
            SetKeyboardInput(_placedKeyboard, false);
        }

        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isOpen;
    }

    private sealed class KeyboardState
    {
        private KeyboardSpawnProfile _pendingProfile;
        private bool _placementFailed;
        public KeyboardInputController Keyboard { get; }
        public Rigidbody Body { get; }
        public BoxCollider Collider { get; }
        public Collider[] Colliders { get; }
        public bool[] ColliderEnabled { get; }
        public KeyboardSpawnProfile PendingProfile => _pendingProfile;
        public bool PlacementFailed => _placementFailed;

        /// <summary>profile을 최초 배치 결과로 보관하여 실패 후 재시도에서도 추첨 결과를 유지한다.</summary>
        public void StoreProfile(KeyboardSpawnProfile profile)
        {
            _pendingProfile = profile;
        }

        /// <summary>이번 배치를 실패 상태로 표시하여 반복 Trigger에서 같은 오류가 계속 발생하지 않게 한다.</summary>
        public void MarkPlacementFailed()
        {
            _placementFailed = true;
        }

        /// <summary>다시 집은 월드 키보드의 배치 실패 상태만 해제하고 기존 추첨 결과는 유지한다.</summary>
        public void AllowPlacementRetry()
        {
            _placementFailed = false;
        }

        /// <summary>keyboard, body, collider와 기존 colliders 및 활성 상태를 저장하여 입력과 들기에 사용할 키보드 상태를 구성한다.</summary>
        public KeyboardState(KeyboardInputController keyboard, Rigidbody body, BoxCollider collider, Collider[] colliders, bool[] colliderEnabled)
        {
            Keyboard = keyboard; Body = body; Collider = collider; Colliders = colliders; ColliderEnabled = colliderEnabled;
        }
    }
}
