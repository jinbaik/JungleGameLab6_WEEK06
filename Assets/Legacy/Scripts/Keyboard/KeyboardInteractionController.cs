using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

using Unity.Cinemachine;

using Game.Economy;
using Game.Session;
using Game.Shop;
using KeyboardModeling;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(BoxCollider), typeof(BoxPickupController))]
public class KeyboardInteractionController : MonoBehaviour
{
    private const int SPAWN_REGION_COUNT = 3;
    private const float FEVER_REFILL_RETRY_INTERVAL = 0.25f;

    public enum KeyboardGuideState { Hidden, Pickup, Placement, BoxPickup, BoxHolding, BoxStore, BoxFull, BoxTake, Dispenser }

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
    public KeyboardInputController ActiveKeyboard => IsSmashMode && _placedKeyboard != null ? _placedKeyboard.Keyboard : null;
    public KeyboardInputController PlacedKeyboard => _placedKeyboard != null ? _placedKeyboard.Keyboard : null;

    public event Action<KeyboardInputController> ActiveKeyboardChanged;
    public event Action<KeyboardInputController> PlacedKeyboardChanged;
    public event Action<KeyboardState> InitialKeyboardSpawned;
    public event Action<KeyboardState> KeyboardCompleted;
    public event Action FinalRegionCompleted;

    [Header("Fever Keyboard Supply")]
    [SerializeField, Min(0.01f)] private float _feverCarryDuration = 0.35f;
    [SerializeField, Min(0.1f)] private float _feverSupplyTimeout = 5f;
    private FeverController _feverController;
    private Coroutine _feverSupplyRoutine;
    private KeyboardState _feverReservedKeyboard;
    private KeyboardStorageBox _feverSourceBox;
    private Vector3 _feverSourcePosition;
    private Quaternion _feverSourceRotation;
    private bool _feverSupplyPending;

    [Header("Pickup")]
    [SerializeField, Min(0.1f)] private float _rayDistance = 12f;
    [SerializeField, Min(0.1f)] private float _holdDistance = 3f;
    [SerializeField, Min(0.1f)] private float _followSpeed = 12f;
    [SerializeField] private Vector3 _holdEulerAngles = new Vector3(-75f, 0f, 0f);
    [SerializeField] private LayerMask _pickupLayers = ~0;
    [SerializeField] private LayerMask _heldKeyboardBlockLayers;
    [SerializeField, Min(0f)] private float _heldKeyboardWallGap = 0.02f;
    private KeyboardState _heldKeyboard;
    private Vector3 _pickupPosition;
    private Quaternion _pickupRotation;
    private bool _canPlaceHeldKeyboard;
    private KeyboardHeldMotion _heldMotion;
    private readonly KeyboardWorldRegistry _world = new KeyboardWorldRegistry();
    private KeyboardWorldTargeting _targeting;
    public bool IsHoldingKeyboard => _heldKeyboard != null;
    public int SpawnedKeyboardCount { get; private set; }

    [Header("Storage Box")]
    private BoxPickupController _boxPickupController;
    private readonly HashSet<KeyboardStorageBox> _observedBoxes = new HashSet<KeyboardStorageBox>();
    private int _guideCount;
    private int _guideCapacity;
    public int GuideCount => _guideCount;
    public int GuideCapacity => _guideCapacity;

    [Header("Keyboard Guide")]
    private KeyboardGuideState _guideState;
    public KeyboardGuideState GuideState => _guideState;

    public event Action<KeyboardGuideState> GuideStateChanged;

    [Header("Spawn")]
    [SerializeField] private bool _spawnRegionsOnEntry;
    [SerializeField] private GameObject _keyboardPrefab;
    [SerializeField] private GameObject _inGameKeyBoardPrefab;
    [FormerlySerializedAs("_front"), InspectorName("첫 번째 생성 구역")]
    [SerializeField] private BoxCollider _firstRegion;
    [FormerlySerializedAs("_frontCount"), InspectorName("첫 번째 생성 개수")]
    [SerializeField, Min(0)] private int _firstRegionCount = 20;
    [SerializeField, InspectorName("첫 번째 구역 키보드 프리팹")] private GameObject _firstRegionKeyboardPrefab;
    [FormerlySerializedAs("_left"), InspectorName("두 번째 생성 구역")]
    [SerializeField] private BoxCollider _secondRegion;
    [FormerlySerializedAs("_leftCount"), InspectorName("두 번째 생성 개수")]
    [SerializeField, Min(0)] private int _secondRegionCount;
    [SerializeField, InspectorName("두 번째 구역 키보드 프리팹")] private GameObject _secondRegionKeyboardPrefab;
    [FormerlySerializedAs("_right"), InspectorName("세 번째 생성 구역")]
    [SerializeField] private BoxCollider _thirdRegion;
    [FormerlySerializedAs("_rightCount"), InspectorName("세 번째 생성 개수")]
    [SerializeField, Min(0)] private int _thirdRegionCount;
    [Tooltip("마당에 처음 들어갈 때 한 개 생성할 키보드 프리팹입니다.")]
    [SerializeField, InspectorName("세 번째 구역 마당 키보드 프리팹")] private GameObject _yardKeyboardPrefab;
    [Tooltip("각 구역이 처음 비었을 때 세 번 방문하여 해당 구역에 키보드를 던지는 연출입니다.")]
    [SerializeField] private BackgroundKeyboardThrowVisit _regionThrowVisit;
    [SerializeField] private KeyboardCompletionProgress _completionProgress;
    private Transform _spawnRoot;
    private readonly KeyboardRegionState _regionState = new KeyboardRegionState();
    public int ActiveSpawnRegion => _regionState.ActiveRegion;

    [Header("Fallen Keyboard Return")]
    [SerializeField] private bool _returnFallenKeyboards;
    [Tooltip("등록된 월드 키보드가 이 월드 Y 이하로 떨어지면 동일한 키보드를 다시 던집니다.")]
    [SerializeField] private float _fallReturnHeight = -3f;
    [Tooltip("키보드 중심이 이 영역 밖으로 나가면 같은 키보드를 보스가 다시 던집니다. 투척 경로까지 포함하는 Trigger BoxCollider를 지정하세요. 비우면 Y 높이만 검사합니다.")]
    [SerializeField, InspectorName("키보드 유지 영역")] private BoxCollider _keyboardReturnArea;

    [Header("Opening")]
    [SerializeField] private bool _startWithSingleKeyboard;
    [SerializeField] private Transform _openingCeiling;
    private KeyboardState _openingKeyboard;

    [Header("Fist Camera Shake")]
    [SerializeField] private CinemachineImpulseSource _fistImpulseSource;

    [Header("Keycap Quality")]
    [SerializeField] private GameSession _gameSession;
    [SerializeField] private KeyboardRewardController _rewardController;
    [SerializeField] private KeycapRaritySettings _raritySettings;
    private KeyboardPlayableFactory _playableFactory;
    private bool _isPlacingKeyboard;

    [Header("Keyboard destruction")]
    [SerializeField] private GameObject _keyboardFragmentsPrefab;
    [SerializeField] private Sprite _destructionFistSprite;
    [Tooltip("주먹의 조명과 그림자 수신에 사용할 투명 Lit 재질입니다.")]
    [SerializeField] private Material _fistMaterial;
    public Sprite DestructionFistSprite => _destructionFistSprite;

    [Header("Fist Animation")]
    [Tooltip("주먹 이미지의 전체 높이입니다. 값을 키우면 주먹이 커집니다.")]
    [SerializeField, Min(0.01f)] private float _fistHeight = 0.9f;
    [Tooltip("키보드 접촉 위치에서 주먹을 들어 올리는 거리입니다.")]
    [SerializeField, Min(0f)] private float _fistLift = 0.8f;
    [InspectorName("Fist Impact Offset")]
    [Tooltip("누른 키의 윗면에서의 이동량입니다. 0이면 해당 키를 정확히 타격합니다. 키보드 로컬 좌표 기준으로 X는 좌우, Y는 높이, Z는 앞뒤를 조절합니다.")]
    [SerializeField] private Vector3 _keycapFistImpactOffset = Vector3.zero;
    [Tooltip("주먹이 서서히 나타나는 시간입니다. 단위는 초입니다.")]
    [SerializeField, Min(0.01f)] private float _appearDuration = 0.22f;
    [Tooltip("주먹이 키보드까지 내려치는 시간입니다. 줄이면 더 빠르게 내려칩니다.")]
    [SerializeField, Min(0.01f)] private float _strikeDuration = 0.16f;
    [InspectorName("Impact Hold Duration")]
    [Tooltip("타격 위치에서 주먹을 유지하는 시간입니다. 시간이 지나면 이동이나 페이드 없이 즉시 사라집니다. 단위는 초입니다.")]
    [SerializeField, Min(0.01f)] private float _retreatDuration = 0.25f;

    [Header("Hammer Animation")]
    [SerializeField] private Sprite _hammerSprite;
    [SerializeField] private Material _hammerShockwaveMaterial;
    [SerializeField] private KeyboardDestruction.HammerAnimationSettings _hammerAnimation = new KeyboardDestruction.HammerAnimationSettings();

    [Header("monitor")]
    [SerializeField] private MiniGameController _controller;
    public bool IsMiniGameRunning => _controller.CurrentGame != MiniGameController.MiniGameKind.Idle;
    private bool IsMiniGameBlockingWorldInput => IsMiniGameRunning && _controller.HasKeyboard;
    private bool IsKeyboardBreaking => _placedKeyboard != null && _placedKeyboard.Keyboard.GetComponent<KeyboardDestruction>().IsBroken;
    public bool CanEnterShop => (_monitorController != null ? _isShopFocused : IsSmashMode)
        && !_feverSupplyPending && !IsMiniGameRunning && !IsKeyboardBreaking;
    public bool CanEnterMonitor => !_feverSupplyPending && !IsMiniGameRunning && (CanRunMiniGame
        || (_completionProgress != null && _placedKeyboard == null && _heldKeyboard == null && _boxPickupController.HeldBox == null
            && !_isPlacingKeyboard && _viewingAutomaticStation == null));
    public bool CanRunMiniGame => !_feverSupplyPending && IsSmashMode && _placedKeyboard != null && !IsKeyboardBreaking;
    [Header("Shop Focus")]
    [SerializeField] private ShopView _shopView;
    [Tooltip("바탕화면을 사용하는 씬에서는 상점 대신 모니터 조작 상태에 입력을 연결합니다.")]
    [SerializeField] private MonitorController _monitorController;
    private bool _isShopFocused;
    private bool _resumeSmashInput;
    private KeyboardAutomaticStation _viewingAutomaticStation;


    protected virtual void Awake()
    {
        _boxPickupController = GetComponent<BoxPickupController>();
        if (_boxPickupController == null)
            _boxPickupController = gameObject.AddComponent<BoxPickupController>();
        _boxPickupController.Initialize(_camera);
        _targeting = new KeyboardWorldTargeting(_camera, _world);
#if UNITY_EDITOR
        if (_destructionFistSprite == null)
            _destructionFistSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resource/Sprites/Fist.png");
        if (_fistMaterial == null)
            _fistMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Sprites/Materials/Fist.mat");
        if (_hammerSprite == null && (gameObject.scene.name == "Main 1" || gameObject.scene.name == "Main"))
            _hammerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resource/Sprites/Hammer.png");
#endif
        _gameSession = FindAnyObjectByType<GameSession>();
        if (_gameSession != null)
            _gameSession.Settings.RegisterShakeListener(_fixedCam.GetComponent<CinemachineImpulseListener>());
        _rewardController = FindAnyObjectByType<KeyboardRewardController>();
        _controller = FindAnyObjectByType<MiniGameController>();
        _feverController = FindAnyObjectByType<FeverController>();
        _playableFactory = new KeyboardPlayableFactory(this, _world, _inGameKeyBoardPrefab,
            _keyboardFragmentsPrefab, _gameSession, _raritySettings, _rewardController);
        _padPosition = _referenceKeyboard.transform.position;
        _padRotation = _referenceKeyboard.transform.rotation;
        _keyboardBounds = CalculateKeyboardBounds(_referenceKeyboard.transform);
        _padCollider = GetComponent<BoxCollider>();
        _heldMotion = new KeyboardHeldMotion(_camera);

        _padCollider.isTrigger = true;
        _fixedCam.Priority = 0;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    protected virtual void Start()
    {
        //_placedKeyboard = RegisterKeyboard(_referenceKeyboard.GetComponent<KeyboardInputController>());
        _placedKeyboard = null;
        _spawnRoot = new GameObject("SpawnedKeyboards").transform;
        if (_startWithSingleKeyboard)
        {
            CreateOpeningKeyboard();
            return;
        }
        SpawnSurroundingKeyboards();
    }

    protected virtual void Update()
    {
        RefreshSpawnRegions();
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
            bool mousePressedOrHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;

            if (tabPressedOrHeld || mousePressedOrHeld)
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
            if (_feverSupplyPending || (!IsMiniGameRunning && !IsKeyboardBreaking))
            {
                OnExitSmashMode();
            }

            return;
        }
        if (IsSmashMode || IsMiniGameBlockingWorldInput || Mouse.current == null) return;
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            PickUpKeyboardIntoHands();
            return;
        }
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        HandleWorldInteraction();
    }

    protected virtual void FixedUpdate()
    {
        RequestFallenKeyboardReturns();
        if (_heldKeyboard == null) return;
        _heldMotion.Move(_heldKeyboard, _holdDistance, _followSpeed, _holdEulerAngles,
            _heldKeyboardBlockLayers, _heldKeyboardWallGap);
    }

    protected virtual void LateUpdate()
    {
        RefreshGuideState();
    }

    //void OnTriggerEnter(Collider other) { TryPlaceKeyboard(other); }
    protected virtual void OnTriggerStay(Collider other) { TryPlaceKeyboard(other); }
    protected virtual void OnTriggerExit(Collider other) { if (_heldKeyboard != null && other.attachedRigidbody == _heldKeyboard.Body) _canPlaceHeldKeyboard = true; }

    protected virtual void OnEnable()
    {
        foreach (KeyboardStorageBox box in _observedBoxes)
            if (box != null) box.KeyboardReleased += ReleaseBoxKeyboard;
        if (_monitorController != null)
        {
            _monitorController.FocusChanged += ApplyShopFocus;
            ApplyShopFocus(_monitorController.IsMonitorFocused);
        }
        else
        {
            _shopView.OpenStateChanged += ApplyShopFocus;
            ApplyShopFocus(_shopView.IsOpen);
        }
    }

    protected virtual void OnDisable()
    {
        CancelFeverSupply();
        if (_boxPickupController.HeldBox != null) _boxPickupController.DropBox();
        foreach (KeyboardStorageBox box in _observedBoxes)
            if (box != null) box.KeyboardReleased -= ReleaseBoxKeyboard;
        SetGuideState(KeyboardGuideState.Hidden);
        if (_viewingAutomaticStation != null)
        {
            _viewingAutomaticStation.SetViewActive(false);
            _viewingAutomaticStation = null;
        }
        if (_monitorController != null)
            _monitorController.FocusChanged -= ApplyShopFocus;
        else
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
    /// instance의 입력과 키캡 Collider를 끄고 들기용 물리 상태를 구성하여 등록 상태를 반환한다.
    /// isPlayable이 true이면 파괴 판정을 초기화하고 regionIndex가 지정된 배경 키보드는 해당 구역 목록에 저장한다.
    /// </summary>
    public KeyboardState RegisterKeyboard(GameObject instance, bool isPlayable = false, int regionIndex = -1)
    {
        Bounds pickupBounds = isPlayable ? _keyboardBounds : CalculateKeyboardBounds(instance.transform);
        KeyboardState state = _world.Register(instance, pickupBounds);
        if (regionIndex >= 0 && regionIndex < SPAWN_REGION_COUNT)
        {
            state.SetSpawnRegionIndex(regionIndex);
            _regionState.Register(regionIndex);
        }
        if (isPlayable)
        {
            KeyboardInputController keyboard = state.Keyboard;
            KeyboardDestruction destruction = keyboard.gameObject.AddComponent<KeyboardDestruction>();
            if (_fistImpulseSource != null) destruction.FistImpact += HandleFistImpact;
            destruction.Initialize(keyboard.GetDestructionKeycaps(), _keyboardFragmentsPrefab, _destructionFistSprite, () => HandleKeyboardBreaking(state), () => HandleKeyboardDestroyed(state), ConfigureFistAnimation);
        }
        return state;
    }

    /// <summary>
    /// Inspector에 설정한 주먹 및 망치 이미지, 크기, 이동과 타격 설정을 destruction에 전달한다.
    /// 일반 키보드와 자동 작업대 키보드의 파괴 연출 설정을 동일하게 변경한다.
    /// </summary>
    public void ConfigureFistAnimation(KeyboardDestruction destruction)
    {
        destruction.ConfigureFistAnimation(_fistHeight, _fistLift, _appearDuration, _strikeDuration, _retreatDuration, _keycapFistImpactOffset, _fistMaterial);
        destruction.ConfigureHammerAnimation(_hammerSprite, _hammerAnimation, _hammerShockwaveMaterial);
    }

    /// <summary>
    /// 주먹이 닿은 position에서 설정된 Impulse 신호를 발생시킨다.
    /// 활성 상태의 SmashMode에서만 부수기 카메라에 타격 흔들림을 전달한다.
    /// </summary>
    private void HandleFistImpact(Vector3 position)
    {
        if (!isActiveAndEnabled || !IsSmashMode) return;
        if (_gameSession != null && _gameSession.Settings.CameraShakeStrength <= 0f) return;
        _fistImpulseSource.GenerateImpulseAtPositionWithVelocity(position, _fistImpulseSource.DefaultVelocity);
    }

    /// <summary>
    /// state의 입력을 끄고 들기 목록에서 제거한다.
    /// 배치된 키보드가 파괴되면 피버 공급 자격을 확정하고 입력 복원 예약을 해제한다.
    /// 미니게임의 입력 대상만 해제하여 진행도와 피버 상태를 보존한다.
    /// </summary>
    private void HandleKeyboardBreaking(KeyboardState state)
    {
        SetKeyboardInput(state, false);
        _world.Remove(state.Body);

        if (_heldKeyboard == state)
        {
            _heldKeyboard = null;
        }

        if (_placedKeyboard == state)
        {
            _feverSupplyPending = IsSmashMode && isActiveAndEnabled
                && _feverController != null && _feverController.IsFeverActive;
            if (_monitorController != null)
                _monitorController.SetMonitorFocus(false);
            _resumeSmashInput = false;
            _controller.SetKeyBoard(null);
        }
    }

    /// <summary>
    /// state가 Pad 키보드이면 완료 보상과 진행을 반영하고 배치 참조를 비운다.
    /// 피버 중 확정한 공급은 SmashMode를 유지하여 시작하고 일반 파괴는 모드를 종료한다.
    /// </summary>
    private void HandleKeyboardDestroyed(KeyboardState state)
    {
        if (_placedKeyboard != state) return;

        if (_openingKeyboard == state)
        {
            Vector3 position = _openingCeiling.localPosition;
            position.x = 30f;
            _openingCeiling.localPosition = position;
            _openingKeyboard = null;
            SpawnSurroundingKeyboards();
        }

        _gameSession.CompleteKeyboard();
        NotifyKeyboardCompleted(state);

        if (_feverSupplyPending && IsSmashMode && isActiveAndEnabled)
        {
            state.Keyboard.GetComponent<KeyboardDestruction>().DisableFragmentCollisions();
            _placedKeyboard = null;
            ActiveKeyboardChanged?.Invoke(null);
            PlacedKeyboardChanged?.Invoke(null);
            _feverSupplyRoutine = StartCoroutine(SupplyFeverKeyboard());
            return;
        }

        if (IsSmashMode) OnExitSmashMode();
        _placedKeyboard = null;
        PlacedKeyboardChanged?.Invoke(null);
    }

    /// <summary>
    /// 파괴가 완료된 state의 구역 잔여 개수를 줄이고 진행 정보를 구독자에게 전달한다.
    /// 수동 패드와 자동작업대 모두 실제 파괴 완료를 기준으로 구역 투척에 연결한다.
    /// </summary>
    private void NotifyKeyboardCompleted(KeyboardState state)
    {
        _regionState.Complete(state.SpawnRegionIndex);
        KeyboardCompleted?.Invoke(state);
        if (_regionState.IsFinalRegionComplete(state.SpawnRegionIndex))
            FinalRegionCompleted?.Invoke();
    }

    /// <summary>
    /// 피버 중 파괴 완료된 패드에 바닥 또는 상자 키보드 하나를 예약하여 운반한다.
    /// 운반 시간과 대기 한도에 Time.deltaTime을 사용하고 실패 시 일반 교체 상태로 복귀한다.
    /// </summary>
    private IEnumerator SupplyFeverKeyboard()
    {
        yield return null;
        float elapsed = 0f;
        float retryRemaining = 0f;
        float carryElapsed = 0f;

        while (_feverSupplyPending && IsSmashMode && elapsed < _feverSupplyTimeout)
        {
            if (Time.deltaTime <= 0f)
            {
                yield return null;
                continue;
            }

            elapsed += Time.deltaTime;
            if (elapsed >= _feverSupplyTimeout) break;
            if (_feverReservedKeyboard == null)
            {
                retryRemaining -= Time.deltaTime;
                if (retryRemaining <= 0f)
                {
                    if (TryReserveFeverKeyboard(out KeyboardState source))
                    {
                        _feverReservedKeyboard = source;
                        _feverSourcePosition = source.Body.position;
                        _feverSourceRotation = source.Body.rotation;
                        carryElapsed = 0f;
                    }
                    retryRemaining = FEVER_REFILL_RETRY_INTERVAL;
                }
            }

            if (_feverReservedKeyboard != null)
            {
                KeyboardState source = _feverReservedKeyboard;
                if (source.Root == null || source.Body == null)
                {
                    ReleaseFeverKeyboard();
                }
                else
                {
                    carryElapsed += Time.deltaTime;
                    float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(carryElapsed / _feverCarryDuration));
                    source.Body.position = Vector3.Lerp(_feverSourcePosition, _padPosition, progress);
                    source.Body.rotation = Quaternion.Slerp(_feverSourceRotation, _padRotation, progress);
                    if (carryElapsed >= _feverCarryDuration)
                    {
                        if (TryPlaceFeverKeyboard(source)) yield break;
                        ReleaseFeverKeyboard();
                    }
                }
            }
            yield return null;
        }

        _feverSupplyRoutine = null;
        OnExitSmashMode();
    }

    /// <summary>
    /// 예약된 source를 기존 프로필과 진행 식별자를 유지한 플레이 키보드로 전환한다.
    /// 준비 성공 시 원본을 소비하고 새 대상의 구독을 연결한 뒤 입력을 켜며 성공 여부를 반환한다.
    /// </summary>
    private bool TryPlaceFeverKeyboard(KeyboardState source)
    {
        if (!_feverSupplyPending || !IsSmashMode || _placedKeyboard != null || _isPlacingKeyboard
            || !_world.IsReserved(source)) return false;

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
            return false;
        }
        finally
        {
            _isPlacingKeyboard = false;
        }

        Rigidbody body = target.Body;
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
        body.useGravity = false;
        body.position = _padPosition;
        body.rotation = _padRotation;
        KeyboardProtectionCover cover = source.Root.GetComponentInChildren<KeyboardProtectionCover>();
        if (cover != null) cover.RemoveFrom(target.Root.transform);

        _world.Remove(source.Body);
        _world.Unreserve(source);
        if (lastDroped == source.Collider.gameObject) lastDroped = null;
        source.Root.SetActive(false);
        Destroy(source.Root);

        _placedKeyboard = target;
        _feverReservedKeyboard = null;
        _feverSourceBox = null;
        _feverSupplyRoutine = null;
        _feverSupplyPending = false;
        _controller.SetKeyBoard(target.Keyboard);
        PlacedKeyboardChanged?.Invoke(target.Keyboard);
        ActiveKeyboardChanged?.Invoke(target.Keyboard);
        SetKeyboardInput(target, true);
        return true;
    }

    /// <summary>
    /// 진행 중인 피버 공급 코루틴을 중단하고 공급 자격을 해제한다.
    /// 사용자 이탈과 비활성화에 사용하며 예약한 월드 키보드의 위치와 물리를 복원한다.
    /// </summary>
    private void CancelFeverSupply()
    {
        _feverSupplyPending = false;
        if (_feverSupplyRoutine != null)
        {
            StopCoroutine(_feverSupplyRoutine);
            _feverSupplyRoutine = null;
        }
        ReleaseFeverKeyboard();
    }

    /// <summary>
    /// 피버 공급으로 예약한 키보드를 운반 전 위치와 회전으로 반환한다.
    /// 상자에서 꺼낸 경우 원래 상자에 다시 보관하고 공급 대상 참조를 비운다.
    /// </summary>
    private void ReleaseFeverKeyboard()
    {
        if (_feverReservedKeyboard == null) return;
        ReleaseAutomaticKeyboard(_feverReservedKeyboard, _feverSourcePosition, _feverSourceRotation);
        if (_feverSourceBox != null && _feverReservedKeyboard.Root != null
            && _feverSourceBox.TryStoreKeyboard(_feverReservedKeyboard))
            _world.Store(_feverReservedKeyboard);
        _feverReservedKeyboard = null;
        _feverSourceBox = null;
    }

    /// <summary>
    /// 피버 공급용 바닥 키보드를 먼저 예약하고 없으면 가장 가까운 상자에서 하나를 꺼낸다.
    /// reserved에 원본 상태를 전달하고 보관 목록, 운반 예약과 복원용 상자 참조를 갱신한다.
    /// </summary>
    private bool TryReserveFeverKeyboard(out KeyboardState reserved)
    {
        _feverSourceBox = null;
        if (TryReserveWorldKeyboard(_padPosition, out reserved)) return true;
        KeyboardStorageBox nearest = null;
        float closest = float.PositiveInfinity;
        foreach (KeyboardStorageBox box in _observedBoxes)
        {
            if (box == null || !box.isActiveAndEnabled || box.Count == 0) continue;
            float distance = (box.transform.position - _padPosition).sqrMagnitude;
            if (distance >= closest) continue;
            nearest = box;
            closest = distance;
        }
        if (nearest == null || !nearest.TryTakeKeyboard(out reserved)) return false;
        _feverSourceBox = nearest;
        _world.Unstore(reserved);
        _world.Reserve(reserved);
        reserved.AllowPlacementRetry();
        reserved.Body.isKinematic = true;
        reserved.Body.useGravity = false;
        reserved.Collider.enabled = false;
        reserved.Body.position = nearest.transform.position + nearest.transform.up * 1.2f;
        reserved.Body.rotation = nearest.transform.rotation;
        return true;
    }

    /// <summary>
    /// 설정된 키보드 프리팹과 Pad 위치·회전으로 시작 키보드 하나를 준비한다.
    /// 기존 품질·파괴 등록을 사용하고 배치 상태를 저장하여 SmashMode 입력을 활성화한다.
    /// </summary>
    private void CreateOpeningKeyboard()
    {
        GameObject sourceObject = Instantiate(_firstRegionKeyboardPrefab != null ? _firstRegionKeyboardPrefab : _keyboardPrefab, _padPosition, _padRotation, _spawnRoot);
        KeyboardState source = RegisterKeyboard(sourceObject);
        _openingKeyboard = CreatePlayableKeyboard(source);
        _placedKeyboard = _openingKeyboard;
        _world.Remove(source.Body);
        sourceObject.SetActive(false);
        Destroy(sourceObject);
        Rigidbody body = _placedKeyboard.Body;
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
        body.useGravity = false;
        body.position = _padPosition;
        body.rotation = _padRotation;
        _controller.SetKeyBoard(_placedKeyboard.Keyboard);
        PlacedKeyboardChanged?.Invoke(_placedKeyboard.Keyboard);
        SpawnedKeyboardCount = 1;
        OnEnterSmashMode();
    }

    /// <summary>
    /// 세 구역과 각 생성 개수 설정으로 떨어질 키보드를 생성한다.
    /// 구역 등록과 SpawnedKeyboardCount를 갱신하여 이후 보충 연출에 연결한다.
    /// </summary>
    private void SpawnSurroundingKeyboards()
    {
        if (_spawnRegionsOnEntry)
        {
            ActivateSpawnRegion(0);
            return;
        }
        SpawnKeyboards(_firstRegion, _firstRegionCount, "CeilingRegion", 0, _firstRegionKeyboardPrefab);
        SpawnKeyboards(_secondRegion, _secondRegionCount, "SecondRegion", 1, _secondRegionKeyboardPrefab);
        SpawnKeyboards(_thirdRegion, _thirdRegionCount, "ThirdRegion", 2);
        Debug.Log($"[Keyboard] Spawned {SpawnedKeyboardCount} keyboards.", this);
    }

    /// <summary>
    /// regionIndex의 구역을 한 번 활성화하고 해당 구역의 설정된 개수만 생성한다.
    /// 활성 구역과 생성 예약 상태를 저장하여 중복 진입 생성을 막는다.
    /// </summary>
    private void ActivateSpawnRegion(int regionIndex)
    {
        if (!_regionState.TryActivate(regionIndex)) return;
        switch (regionIndex)
        {
            case 0: SpawnKeyboards(_firstRegion, _firstRegionCount, "FirstRegion", 0, _firstRegionKeyboardPrefab); break;
            case 1: SpawnKeyboards(_secondRegion, _secondRegionCount, "SecondRegion", 1, _secondRegionKeyboardPrefab); break;
            case 2: SpawnKeyboards(_thirdRegion, 1, "YardKeyboard", 2, _yardKeyboardPrefab, false); break;
        }
        Debug.Log($"[Keyboard] Entered region {regionIndex + 1}; initial spawn complete.", this);
    }

    /// <summary>
    /// regionIndex 진입 시 nextDoor를 새 완료 대상으로 연결하고 보스 위치를 offset만큼 옮긴다.
    /// 이전 구역 예약을 정리한 뒤 새 구역의 키보드만 생성한다.
    /// </summary>
    public void EnterSpawnRegion(int regionIndex, Transform nextDoor, Vector3 offset)
    {
        if (regionIndex < 0 || regionIndex >= SPAWN_REGION_COUNT || _regionState.IsStarted(regionIndex)) return;
        _regionThrowVisit.RelocateForRegion(offset, regionIndex);
        _completionProgress.BeginRegion(nextDoor);
        ActivateSpawnRegion(regionIndex);
    }

    /// <summary>
    /// 마당에 처음 진입하면 Inspector의 키보드 프리팹을 한 개 생성한다.
    /// 문, 작업대, 보스 위치와 이전 구역 완료 상태는 변경하지 않는다.
    /// </summary>
    public void EnterYard()
    {
        Game.UI.ThanksForPlayingHudView.Attach(this);
        ActivateSpawnRegion(2);
    }

    /// <summary>
    /// body에 등록된 키보드의 원래 생성 구역을 반환한다.
    /// 미등록 키보드는 -1을 반환하여 기존 방향 투척을 사용한다.
    /// </summary>
    public int GetKeyboardSpawnRegionIndex(Rigidbody body)
    {
        return _world.TryGet(body, out KeyboardState state) ? state.SpawnRegionIndex : -1;
    }

    /// <summary>
    /// 등록된 body에 대응하는 키보드 상태를 반환한다.
    /// 재투척 시 같은 원본, 구역과 진행 정보를 사용하며 등록 개수는 변경하지 않는다.
    /// </summary>
    public KeyboardState GetRegisteredKeyboard(Rigidbody body)
    {
        return _world.Get(body);
    }

    /// <summary>
    /// 사용 중이 아닌 임시 root를 등록 목록에서 제거하고 삭제한다.
    /// 손에 들거나 배치, 보관, 자동 운반 중인 키보드는 유지하여 연속 투척 개수 정리가 플레이 상태를 지우지 않게 한다.
    /// </summary>
    public void DiscardTemporaryKeyboard(GameObject root)
    {
        Rigidbody body = root.GetComponent<Rigidbody>();
        if (!_world.TryGet(body, out KeyboardState state)) return;
        if (state == _heldKeyboard || state == _placedKeyboard || _world.IsStored(state)
            || _world.IsReserved(state)) return;
        _world.Remove(body);
        root.SetActive(false);
        Destroy(root);
    }

    /// <summary>
    /// area 안에서 count개를 생성하고 regionIndex 구역에 등록한다.
    /// 지정한 prefab 또는 기본 프리팹을 사용하며 trackInitialProgress가 true이면 초기 완료 집계에 전달한다.
    /// </summary>
    private void SpawnKeyboards(BoxCollider area, int count, string areaName, int regionIndex, GameObject prefab = null, bool trackInitialProgress = true)
    {
        if (count == 0) return;
        if (area == null) { Debug.LogWarning($"[Keyboard] Assign the {areaName} BoxCollider to spawn {count} keyboards.", this); return; }
        area.isTrigger = true;
        for (int index = 0; index < count; index++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f) * area.size.x, Random.Range(-0.5f, 0.5f) * area.size.y, Random.Range(-0.5f, 0.5f) * area.size.z);
            Vector3 position = area.transform.TransformPoint(area.center + offset);
            GameObject keyboard = Instantiate(prefab != null ? prefab : _keyboardPrefab, position, Random.rotationUniform, _spawnRoot);
            keyboard.name = $"{areaName}_Keyboard_{index + 1}";
            keyboard.transform.localScale = _referenceKeyboard.transform.lossyScale;
            KeyboardState state = RegisterKeyboard(keyboard, false, regionIndex);
            SpawnedKeyboardCount++;
            if (trackInitialProgress) InitialKeyboardSpawned?.Invoke(state);
        }
    }

    /// <summary>
    /// 각 구역의 실제 파괴 잔여 개수가 0이면 한 번 보충 투척을 요청한다.
    /// 완료 진행이 있으면 보스 투척을 문 개방용 보상으로 요청하고 중복 투척을 막는다.
    /// </summary>
    private void RefreshSpawnRegions()
    {
        if (_regionThrowVisit == null) return;
        for (int index = 0; index < SPAWN_REGION_COUNT; index++)
        {
            int throwCount = _regionState.TryBeginRefill(index,
                _completionProgress != null && _completionProgress.RewardsRequested, _spawnRegionsOnEntry);
            if (throwCount == 0) continue;
            if (_completionProgress != null)
                _completionProgress.RequestRegionRewards(index);
            else
                _regionThrowVisit.RequestThrows(index, throwCount);
        }
    }

    /// <summary>
    /// regionIndex에 해당하는 첫 번째, 두 번째 또는 세 번째 구역 안의 임의 월드 위치를 반환한다.
    /// 구역 중심과 크기를 사용하여 보충 키보드의 던지기 도착 위치를 정한다.
    /// </summary>
    public Vector3 GetRegionSpawnPosition(int regionIndex)
    {
        BoxCollider area = regionIndex switch
        {
            0 => _firstRegion,
            1 => _secondRegion,
            2 => _thirdRegion,
            _ => throw new ArgumentOutOfRangeException(nameof(regionIndex))
        };
        Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f) * area.size.x, 0f, Random.Range(-0.3f, 0.3f) * area.size.z);
        return area.transform.TransformPoint(area.center + offset);
    }

    /// <summary>
    /// 등록된 월드 키보드가 _fallReturnHeight 이하로 떨어지거나 지정한 유지 영역 밖으로 나가면 동일한 Rigidbody의 재투척을 요청한다.
    /// 들고 있거나 배치된 키보드는 제외하고 대기 중인 키보드의 물리를 정지하여 중복 요청을 막는다.
    /// </summary>
    private void RequestFallenKeyboardReturns()
    {
        if (!_returnFallenKeyboards || _regionThrowVisit == null || !_regionThrowVisit.isActiveAndEnabled) return;
        foreach (KeyboardState state in _world.Keyboards)
        {
            Rigidbody body = state.Body;
            if (body == null || !state.Root.activeInHierarchy || state == _heldKeyboard || state == _placedKeyboard || _world.IsStored(state) || body.isKinematic) continue;
            bool belowHeight = body.position.y <= _fallReturnHeight;
            bool outsideArea = IsOutsideKeyboardReturnArea(body.worldCenterOfMass);
            if (!belowHeight && !outsideArea) continue;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            _regionThrowVisit.RequestKeyboardReturn(body);
            Debug.Log($"[Keyboard] Fallen return: {state.Root.name}, region={state.SpawnRegionIndex + 1}, reason={(belowHeight ? "BelowHeight" : "OutsideArea")}. Reusing the same keyboard.", state.Root);
        }
    }

    /// <summary>
    /// position이 Inspector에서 지정한 키보드 유지 영역 밖인지 반환한다.
    /// BoxCollider의 로컬 중심과 크기를 사용하여 회전과 배율을 반영하며 영역 미지정 시 false를 반환한다.
    /// </summary>
    private bool IsOutsideKeyboardReturnArea(Vector3 position)
    {
        if (_keyboardReturnArea == null) return false;
        Vector3 local = _keyboardReturnArea.transform.InverseTransformPoint(position) - _keyboardReturnArea.center;
        Vector3 halfSize = _keyboardReturnArea.size * 0.5f;
        return Mathf.Abs(local.x) > halfSize.x || Mathf.Abs(local.y) > halfSize.y || Mathf.Abs(local.z) > halfSize.z;
    }

    /// <summary>
    /// 현재 모드와 입력 가능 상태, 들고 있는 키보드와 카메라 중앙 조준 대상을 확인한다.
    /// 키보드와 상자, 자판기 사용 안내를 선택하고 입력할 수 없는 상황에서는 가이드를 숨긴다.
    /// </summary>
    private void RefreshGuideState()
    {
        if (!Application.isFocused || IsSmashMode || _isShopFocused || _viewingAutomaticStation != null
            || _isPlacingKeyboard || IsMiniGameBlockingWorldInput || Mouse.current == null)
        {
            SetGuideState(KeyboardGuideState.Hidden);
            return;
        }
#if UNITY_EDITOR
        if (EditorApplication.isPaused || EditorWindow.focusedWindow == null || EditorWindow.focusedWindow.GetType().Name != "GameView")
        {
            SetGuideState(KeyboardGuideState.Hidden);
            return;
        }
#endif
        if (_heldKeyboard != null)
        {
            SetGuideState(KeyboardGuideState.Placement);
            return;
        }

        if (_boxPickupController.HeldBox != null)
        {
            bool target = TryGetStorageKeyboard(out _);
            SetGuideState(target ? (_boxPickupController.HeldBox.IsFull ? KeyboardGuideState.BoxFull : KeyboardGuideState.BoxStore)
                : KeyboardGuideState.BoxHolding, _boxPickupController.HeldBox);
            return;
        }
        if (TryGetInteractionHit(out RaycastHit boxHit))
        {
            KeyboardDispenser dispenser = boxHit.collider.GetComponentInParent<KeyboardDispenser>();
            if (dispenser != null)
            {
                SetGuideState(KeyboardGuideState.Dispenser);
                return;
            }

            KeyboardStorageBox box = boxHit.collider.GetComponentInParent<KeyboardStorageBox>();
            if (box != null)
            {
                SetGuideState(box.Count > 0 && box.IsTopInteraction(boxHit.collider)
                    ? KeyboardGuideState.BoxTake : KeyboardGuideState.BoxPickup, box);
                return;
            }
        }

        bool canPickUp = TryGetLookedAtKeyboard(out _);
        SetGuideState(canPickUp ? KeyboardGuideState.Pickup : KeyboardGuideState.Hidden);
    }

    /// <summary>
    /// state가 기존 가이드 상태와 다르면 현재 상태를 교체한다.
    /// 변경된 상태를 GuideStateChanged로 전달하여 HUD 문구를 갱신한다.
    /// </summary>
    private void SetGuideState(KeyboardGuideState state, KeyboardStorageBox box = null)
    {
        int count = box != null ? box.Count : 0;
        int capacity = box != null ? box.Capacity : 0;
        if (_guideState == state && _guideCount == count && _guideCapacity == capacity)
            return;

        _guideCount = count;
        _guideCapacity = capacity;
        _guideState = state;
        GuideStateChanged?.Invoke(state);
    }

    /// <summary>
    /// 공용 조준 판정으로 처음 보이는 등록 키보드를 찾는다.
    /// 자동 작업대를 제외하고 조준 대상을 state로 전달하며 대상이 있으면 true를 반환한다.
    /// </summary>
    private bool TryGetLookedAtKeyboard(out KeyboardState state)
    {
        return _targeting.TryGetKeyboard(_rayDistance, _pickupLayers,
            _boxPickupController.HeldBox, _heldKeyboard, out state);
    }

    /// <summary>카메라 중앙 Ray의 첫 충돌이 등록된 키보드이면 집고, 교환에 사용할 원래 위치 및 회전을 저장한다.</summary>
    private void PickUpKeyboard()
    {
        if (!TryGetLookedAtKeyboard(out KeyboardState state))
            return;

        if (state == _placedKeyboard)
        {
            OnEnterSmashMode();
            return;
        }
        BeginHoldKeyboard(state);
    }

    /// <summary>
    /// 카메라 중앙에 조준한 키보드를 손에 든 상태로 전환한다.
    /// 조준한 키보드 상태를 사용하며, 배치 키보드라면 미니게임 참조와 배치 상태를 해제한다.
    /// </summary>
    private void PickUpKeyboardIntoHands()
    {
        if (_heldKeyboard != null || _boxPickupController.HeldBox != null || _isPlacingKeyboard || _feverSupplyPending)
            return;
        if (!TryGetLookedAtKeyboard(out KeyboardState state))
            return;

        if (state == _placedKeyboard)
            _controller.SetKeyBoard(null);

        BeginHoldKeyboard(state);
    }

    /// <summary>
    /// state를 손에 든 키보드로 지정하고 위치, 물리와 패드 배치 가능 상태를 갱신한다.
    /// 바닥 집기와 상자 꺼내기에 동일한 잡기 설정을 적용한다.
    /// </summary>
    private void BeginHoldKeyboard(KeyboardState state)
    {
        _heldKeyboard = state;
        state.AllowPlacementRetry();
        _pickupPosition = state.Body.position;
        _pickupRotation = state.Body.rotation;
        _canPlaceHeldKeyboard = _heldMotion.BeginHold(state, _padCollider);
        if (_placedKeyboard == state)
        {
            _placedKeyboard = null;
            PlacedKeyboardChanged?.Invoke(null);
        }
    }

    /// <summary>
    /// 이동한 작업대의 기준 키보드 위치와 회전으로 패드 배치 좌표를 갱신한다.
    /// 기존 배치 키보드가 있으면 같은 좌표로 옮기며 구매와 파괴 상태는 유지한다.
    /// </summary>
    public void RefreshWorkbenchPlacement()
    {
        _padPosition = _referenceKeyboard.transform.position;
        _padRotation = _referenceKeyboard.transform.rotation;
        if (_placedKeyboard == null) return;
        _placedKeyboard.Body.position = _padPosition;
        _placedKeyboard.Body.rotation = _padRotation;
    }

    /// <summary>
    /// 내려놓은 상자를 destination의 위치와 회전으로 이동하도록 들기 컨트롤러에 전달한다.
    /// 손에 든 상자는 카메라 추적을 유지하며 보관 목록과 키보드 상태는 변경하지 않는다.
    /// </summary>
    public void MoveStorageBoxes(Transform destination)
    {
        _boxPickupController.MoveUnheldBoxes(destination);
    }
    /// <summary>
    /// 현재 손 상태와 중앙 조준 결과로 한 번의 클릭 동작을 처리한다.
    /// 자판기 키보드 배출, 상자 보관과 꺼내기 또는 키보드 집기 상태를 변경한다.
    /// </summary>
    private void HandleWorldInteraction()
    {
        if (_heldKeyboard != null) { DropKeyboard(); return; }
        if (_boxPickupController.HeldBox != null)
        {
            if (!TryStoreLookedAtKeyboard()) _boxPickupController.DropBox();
            return;
        }
        if (TryGetInteractionHit(out RaycastHit hit))
        {
            KeyboardDispenser dispenser = hit.collider.GetComponentInParent<KeyboardDispenser>();
            if (dispenser != null)
            {
                if (dispenser.TryDispense(out GameObject keyboardRoot))
                {
                    keyboardRoot.transform.SetParent(_spawnRoot, true);
                    RegisterKeyboard(keyboardRoot);
                    SpawnedKeyboardCount++;
                }
                return;
            }

            KeyboardStorageBox box = hit.collider.GetComponentInParent<KeyboardStorageBox>();
            if (box != null)
            {
                ObserveBox(box);
                if (box.Count > 0 && box.IsTopInteraction(hit.collider)) TryTakeKeyboardFromBox(box);
                else _boxPickupController.PickUpBox(box);
                return;
            }
        }
        PickUpKeyboard();
    }

    /// <summary>
    /// 중앙 Ray의 가장 가까운 물리 표면 또는 상자 위쪽 영역을 hit에 전달한다.
    /// 일반 Trigger와 들고 있는 상자를 제외하고 카메라가 키보드 영역 안에 있는 경우도 검사하여 대상 유무를 반환한다.
    /// </summary>
    private bool TryGetInteractionHit(out RaycastHit hit)
    {
        return _targeting.TryGetHit(_rayDistance, _pickupLayers,
            _boxPickupController.HeldBox, _heldKeyboard, out hit);
    }

    /// <summary>
    /// 일반 집기와 동일한 공용 조준 판정으로 보관할 키보드를 keyboard에 전달한다.
    /// 배치, 보관, 자동 수거와 복귀 중인 대상은 제외하고 성공 여부를 반환한다.
    /// </summary>
    private bool TryGetStorageKeyboard(out KeyboardState keyboard)
    {
        return TryGetLookedAtKeyboard(out keyboard) && keyboard != _placedKeyboard;
    }

    /// <summary>
    /// box의 해제 이벤트를 한 번 구독하여 보관 상태 정리를 연결한다.
    /// </summary>
    private void ObserveBox(KeyboardStorageBox box)
    {
        if (_observedBoxes.Add(box)) box.KeyboardReleased += ReleaseBoxKeyboard;
    }

    /// <summary>
    /// 조준한 키보드를 현재 상자에 보관하고 보관 집합을 갱신한다.
    /// 가득 찬 경우도 클릭을 소비하며 키보드 조준 여부를 반환한다.
    /// </summary>
    private bool TryStoreLookedAtKeyboard()
    {
        if (!TryGetStorageKeyboard(out KeyboardState keyboard)) return false;
        if (_boxPickupController.HeldBox.TryStoreKeyboard(keyboard))
        {
            _world.Store(keyboard);
            if (lastDroped == keyboard.Collider.gameObject) lastDroped = null;
        }
        return true;
    }

    /// <summary>
    /// box에서 마지막 키보드를 꺼내 보관 집합을 해제하고 손에 든 상태로 전환한다.
    /// 꺼내기 성공 여부를 반환한다.
    /// </summary>
    private bool TryTakeKeyboardFromBox(KeyboardStorageBox box)
    {
        if (!box.TryTakeKeyboard(out KeyboardState keyboard)) return false;
        _world.Unstore(keyboard);
        BeginHoldKeyboard(keyboard);
        Quaternion rotation = _camera.transform.rotation * Quaternion.Euler(_holdEulerAngles);
        keyboard.Body.rotation = rotation;
        keyboard.Body.position = _camera.transform.position + _camera.transform.forward * _holdDistance
            - rotation * Vector3.Scale(keyboard.Collider.center, keyboard.Root.transform.lossyScale);
        return true;
    }

    /// <summary>
    /// 상자에서 해제된 state를 보관 집합에서 제거한다.
    /// 월드 키보드의 기존 집기와 배치 대상으로 복원한다.
    /// </summary>
    private void ReleaseBoxKeyboard(KeyboardState state)
    {
        _world.Unstore(state);
    }

    private GameObject lastDroped;

    /// <summary>
    /// 놓은 월드 키보드를 빈 자동작업대에 넣고 플레이어 회수 대상에서 제외한다.
    /// other와 station으로 기존 품질 생성 및 배치 로직을 사용하며 수동 스매쉬 모드와 입력 이벤트는 켜지 않는다.
    /// </summary>
    public void TryPlaceAutomaticKeyboard(Collider other, KeyboardAutomaticStation station)
    {
        if (_isShopFocused || _isPlacingKeyboard || _viewingAutomaticStation != null || station.HasKeyboard || station.IsAutoRefillInProgress || other.gameObject != lastDroped)
        {
            return;
        }

        if (other.attachedRigidbody == null || !_world.TryGet(other.attachedRigidbody, out KeyboardState source) || source.PlacementFailed || _world.IsStored(source) || _world.IsReserved(source))
        {
            return;
        }
        _isPlacingKeyboard = true;
        KeyboardState target = null;
        try
        {
            target = CreatePlayableKeyboard(source, false);
            station.AcceptKeyboard(target.Keyboard, () => NotifyKeyboardCompleted(target));
            _world.Remove(target.Body);
            _world.Remove(source.Body);
            source.Collider.enabled = false;
            lastDroped = null;
            _heldKeyboard = null;
            Destroy(source.Root);
        }
        catch (Exception exception)
        {
            if (target != null && !station.HasKeyboard)
            {
                _world.Remove(target.Body);
                Destroy(target.Keyboard.gameObject);
            }
            source.MarkPlacementFailed();
            Debug.LogException(exception, this);
        }
        finally
        {
            _isPlacingKeyboard = false;
        }
    }

    /// <summary>
    /// station에 가장 가까운 사용 가능한 월드 키보드 하나를 예약한다.
    /// 플레이어 사용 중이거나 다른 슬롯이 예약한 원본을 제외하고 충돌과 물리를 멈춘다.
    /// </summary>
    public bool TryReserveAutomaticKeyboard(
        KeyboardAutomaticStation station,
        out KeyboardState reserved)
    {
        reserved = null;

        if (_isPlacingKeyboard || station.HasKeyboard)
            return false;

        return TryReserveWorldKeyboard(station.Placement.position, out reserved);
    }

    /// <summary>
    /// placementPosition에 가까운 사용 가능한 월드 키보드 하나를 공통 예약 목록에 등록한다.
    /// 플레이어와 작업대가 사용 중인 원본을 제외하고 reserved와 예약 성공 여부를 반환한다.
    /// </summary>
    private bool TryReserveWorldKeyboard(Vector3 placementPosition, out KeyboardState reserved)
    {
        reserved = null;
        if (_isPlacingKeyboard) return false;
        return _world.TryReserveNearest(placementPosition, _heldKeyboard, _placedKeyboard,
            _fallReturnHeight, out reserved);
    }

    /// <summary>
    /// 운반하지 못한 source를 예약 전 위치와 회전으로 복원한다.
    /// 예약을 해제하고 원본의 물리와 충돌을 다시 활성화한다.
    /// </summary>
    public void ReleaseAutomaticKeyboard(
        KeyboardState source,
        Vector3 originalPosition,
        Quaternion originalRotation)
    {
        _world.ReleaseReserved(source, originalPosition, originalRotation);
    }

    /// <summary>
    /// 예약한 실제 월드 키보드를 station용 플레이 키보드로 교체한다.
    /// 기존 희귀도·보상 경로를 사용하고 성공 시 월드 원본 하나를 제거한다.
    /// </summary>
    public bool TryPlaceReservedAutomaticKeyboard(KeyboardState source, KeyboardAutomaticStation station)
    {
        if (source == null || !_world.IsReserved(source) || _isPlacingKeyboard || station.HasKeyboard)
        {
            return false;
        }

        _isPlacingKeyboard = true;
        KeyboardState target = null;

        try
        {
            try
            {
                target = CreatePlayableKeyboard(source, false);
                station.AcceptKeyboard(target.Keyboard, () => NotifyKeyboardCompleted(target));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);

                if (target == null || station.Keyboard != target.Keyboard)
                {
                    if (target != null)
                    {
                        _world.Remove(target.Body);
                        target.Root.SetActive(false);
                        Destroy(target.Root);
                    }

                    return false;
                }
            }

            _world.Remove(target.Body);
            _world.Remove(source.Body);


            _world.Unreserve(source);

            if (lastDroped == source.Collider.gameObject)
                lastDroped = null;

            source.Root.SetActive(false);
            Destroy(source.Root);
            return true;
        }
        finally
        {
            _isPlacingKeyboard = false;
        }
    }

    /// <summary>현재 키보드의 카메라 추적을 중단하고 Rigidbody를 중력에 따라 움직이는 상태로 전환한다.</summary>
    private void DropKeyboard()
    {
        _heldMotion.Drop(_heldKeyboard);
        lastDroped = _heldKeyboard.Collider.gameObject;
        _heldKeyboard = null;


    }

    /// <summary>
    /// other가 배치 가능한 월드 키보드이면 현재 품질 강화 분포로 플레이용 키보드를 생성한다.
    /// 새 객체 준비가 성공하면 원본 등록을 제거하고 패드에 배치하여 SmashMode를 시작한다.
    /// </summary>
    private void TryPlaceKeyboard(Collider other)
    {
        if (_isShopFocused || _isPlacingKeyboard || _feverSupplyPending)
            return;

        if (other.gameObject != lastDroped) return;

        if (!_canPlaceHeldKeyboard || _placedKeyboard != null)
        {

            return;
        }

        if (!_world.TryGet(other.attachedRigidbody, out KeyboardState source) || source.PlacementFailed || _world.IsStored(source) || _world.IsReserved(source))
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

        KeyboardProtectionCover cover = source.Root.GetComponentInChildren<KeyboardProtectionCover>();
        if (cover != null)
            cover.RemoveFrom(target.Root.transform);

        _world.Remove(source.Body);
        source.Collider.enabled = false;
        lastDroped = null;
        _heldKeyboard = null;
        Destroy(source.Root);
        _isPlacingKeyboard = false;
        OnEnterSmashMode();
    }

    /// <summary>
    /// source의 최초 배치에서 현재 강화 분포로 생성 결과를 확정하고 플레이용 객체에 적용한다.
    /// 재시도는 같은 프로필을 사용하고 진행 식별자를 새 상태에 복사하며 준비 실패 시 월드 원본을 유지한다.
    /// </summary>
    private KeyboardState CreatePlayableKeyboard(KeyboardState source, bool playerControlled = true)
    {
        return _playableFactory.Create(source, playerControlled);
    }

    /// <summary>
    /// 배치된 키보드와 고정 카메라를 사용해 부수기 모드에 진입한다.
    /// ActiveKeyboardChanged로 대상 키보드를 알린 뒤 해당 키보드의 입력을 켠다.
    /// </summary>
    private void OnEnterSmashMode()
    {
        IsSmashMode = true;
        _fixedCam.Priority = 20;

        ActiveKeyboardChanged?.Invoke(ActiveKeyboard);

        SetKeyboardInput(_placedKeyboard, true);
    }

    /// <summary>
    /// 배치된 키보드와 고정 카메라를 사용해 부수기 모드를 종료한다.
    /// 피버 공급을 취소하고 키보드 입력과 ActiveKeyboardChanged 대상을 해제한다.
    /// </summary>
    private void OnExitSmashMode()
    {
        CancelFeverSupply();
        _resumeSmashInput = false;
        IsSmashMode = false;
        _fixedCam.Priority = 0;
        if (_placedKeyboard != null) SetKeyboardInput(_placedKeyboard, false);

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
    /// isOpen으로 모니터 조작 상태를 갱신하고 키보드 입력을 중지한다.
    /// 커서를 표시하거나 잠그며 조작 종료 시 전환 입력 해제 후 부수기 입력 복원을 예약한다.
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

    public sealed class KeyboardState
    {
        private KeyboardSpawnProfile _pendingProfile;
        private bool _placementFailed;
        public GameObject Root { get; }
        public KeyboardInputController Keyboard { get; }
        public Rigidbody Body { get; }
        public BoxCollider Collider { get; }
        public Collider[] Colliders { get; }
        public bool[] ColliderEnabled { get; }
        public KeyboardSpawnProfile PendingProfile => _pendingProfile;
        public bool PlacementFailed => _placementFailed;
        public int ProgressId { get; private set; } = -1;
        public int SpawnRegionIndex { get; private set; } = -1;

        /// <summary>
        /// regionIndex를 원본 생성 구역으로 저장한다.
        /// 플레이용 키보드 교체 후에도 같은 구역의 파괴 개수를 추적한다.
        /// </summary>
        public void SetSpawnRegionIndex(int regionIndex)
        {
            SpawnRegionIndex = regionIndex;
        }

        /// <summary>
        /// progressId를 키보드의 진행 식별자로 저장한다.
        /// 배경용 객체에서 플레이용 객체로 교체될 때 같은 식별자를 유지한다.
        /// </summary>
        public void SetProgressId(int progressId)
        {
            ProgressId = progressId;
        }

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

        /// <summary>root, keyboard, body, collider와 기존 Collider 활성 상태를 저장하여 배경용 또는 플레이용 키보드 상태를 구성한다.</summary>
        public KeyboardState(GameObject root, KeyboardInputController keyboard, Rigidbody body, BoxCollider collider, Collider[] colliders, bool[] colliderEnabled)
        {
            Root = root;
            Keyboard = keyboard; Body = body; Collider = collider; Colliders = colliders; ColliderEnabled = colliderEnabled;
        }
    }
}
