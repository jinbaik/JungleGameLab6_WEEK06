using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

using KeyboardModeling;

using Random = UnityEngine.Random;
using Game.Shop;


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

    public event Action<KeyboardInputController> ActiveKeyboardChanged;

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


    void Awake()
    {
        _padPosition = _referenceKeyboard.transform.position;
        _padRotation = _referenceKeyboard.transform.rotation;
        _keyboardBounds = CalculateKeyboardBounds(_referenceKeyboard.transform);
        _padCollider = GetComponent<BoxCollider>();
        _controller = FindAnyObjectByType<MiniGameController>();
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

    /// <summary>입력과 키캡 Collider를 끄고 keyboard에 들기용 Rigidbody와 BoxCollider를 구성하여 등록된 상태를 반환한다.</summary>
    private KeyboardState RegisterKeyboard(KeyboardInputController keyboard)
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
        KeyboardDestruction destruction = keyboard.gameObject.AddComponent<KeyboardDestruction>();
        destruction.Initialize(keyboard.GetDestructionKeycaps(), _keyboardFragmentsPrefab, () => HandleKeyboardBreaking(state), () => HandleKeyboardDestroyed(state));
        return state;
    }

    /// <summary>
    /// state의 입력을 끄고 들기 목록에서 제거한다.
    /// 배치된 키보드가 파괴되는 경우 입력 복원 예약을 해제하고,
    /// 진행 중인 미니게임을 클리어 보상 없이 종료한다.
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
            _controller.CancelMiniGame();
        }
    }

    /// <summary>state가 Pad 키보드이면 파편의 -Z 이동 시작 후 SmashMode를 종료하고 배치 참조를 비운다.</summary>
    private void HandleKeyboardDestroyed(KeyboardState state)
    {
        if (_placedKeyboard != state) return;
        if (IsSmashMode) OnExitSmashMode();
        _placedKeyboard = null;
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
        if (hit.rigidbody == null || !_keyboards.TryGetValue(hit.rigidbody, out KeyboardState state)) return;
        if (state == _placedKeyboard)
        {
            OnEnterSmashMode();
            return;
        }
        _heldKeyboard = state;
        _pickupPosition = state.Body.position;
        _pickupRotation = state.Body.rotation;
        if (!state.Body.isKinematic) { state.Body.linearVelocity = Vector3.zero; state.Body.angularVelocity = Vector3.zero; }
        state.Body.isKinematic = true;
        state.Body.useGravity = false;
        _canPlaceHeldKeyboard = !Physics.ComputePenetration(state.Collider, state.Body.position, state.Body.rotation, _padCollider, _padCollider.transform.position, _padCollider.transform.rotation, out _, out _);
        if (_placedKeyboard == state) _placedKeyboard = null;
    }
    private GameObject lastDroped;
    /// <summary>현재 키보드의 카메라 추적을 중단하고 Rigidbody를 중력에 따라 움직이는 상태로 전환한다.</summary>
    private void DropKeyboard()
    {
        _heldKeyboard.Body.isKinematic = false;
        _heldKeyboard.Body.useGravity = true;
        lastDroped = _heldKeyboard.Collider.gameObject;
        _heldKeyboard = null;


    }

    /// <summary>other가 들고 있는 키보드이면 Pad 기준 위치와 회전으로 놓고, 기존 키보드는 집었던 위치로 옮긴 뒤 SmashMode를 시작한다.</summary>
    private void TryPlaceKeyboard(Collider other)
    {
        if (_isShopFocused)
            return;

        if (other.gameObject != lastDroped) return;

        if (!_canPlaceHeldKeyboard || _placedKeyboard != null)
        {

            return;
        }

        //if (_placedKeyboard != null)
        //{

        //    SetKeyboardInput(_placedKeyboard, false);
        //    _placedKeyboard.Body.position = _pickupPosition;
        //    _placedKeyboard.Body.rotation = _pickupRotation;
        //}

        GameObject temp = Instantiate(_inGameKeyBoardPrefab);
        _placedKeyboard = RegisterKeyboard(temp.GetComponent<KeyboardInputController>());
        Destroy(other.gameObject);
        _heldKeyboard = null;

        _controller.SetKeyBoard(_placedKeyboard.Collider.GetComponent<KeyboardInputController>());
        _placedKeyboard.Body.position = _padPosition;
        _placedKeyboard.Body.rotation = _padRotation;

        //_placedKeyboard.Body.isKinematic = true;

        Rigidbody body = _placedKeyboard.Body;

        // 먼저 물리 모드를 확정하고, 보간 없이 최종 위치를 지정한다.
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
        body.useGravity = false;

        body.position = _padPosition;
        body.rotation = _padRotation;

        OnEnterSmashMode();
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

    /// <summary>state의 입력 컨트롤러와 원래 키캡 Collider를 active에 따라 켜거나 끈다. 파괴된 키캡의 Collider는 복구하지 않는다.</summary>
    private void SetKeyboardInput(KeyboardState state, bool active)
    {
        for (int index = 0; index < state.Colliders.Length; index++)
        {
            KeycapHealth health = state.Colliders[index].GetComponentInParent<KeycapHealth>();
            state.Colliders[index].enabled = active && state.ColliderEnabled[index] && (health == null || health.CurrentHP > 0);
        }
        KeyboardDestruction destruction = state.Keyboard.GetComponent<KeyboardDestruction>();
        state.Collider.enabled = destruction == null || !destruction.IsBroken;
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
        public KeyboardInputController Keyboard { get; }
        public Rigidbody Body { get; }
        public BoxCollider Collider { get; }
        public Collider[] Colliders { get; }
        public bool[] ColliderEnabled { get; }

        /// <summary>keyboard, body, collider와 기존 colliders 및 활성 상태를 저장하여 입력과 들기에 사용할 키보드 상태를 구성한다.</summary>
        public KeyboardState(KeyboardInputController keyboard, Rigidbody body, BoxCollider collider, Collider[] colliders, bool[] colliderEnabled)
        {
            Keyboard = keyboard; Body = body; Collider = collider; Colliders = colliders; ColliderEnabled = colliderEnabled;
        }
    }
}
