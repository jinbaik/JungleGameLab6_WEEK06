using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 실제 마우스 이동량으로 공통 커서 좌표를 관리하고 UI 전용 장치와 표시 및 시스템 커서 전환을 소유한다.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class InGameCursor : MonoBehaviour
{
    public enum CursorDesign { KeycapArrow, KeycapFinger }

    private static InGameCursor _instance;
    private static CursorLockMode _requestedLock;
    private static bool _requestedVisible = true;
    public static event Action PointerCanceled;

    [Header("Design")]
    [SerializeField] private CursorDesign _design;
    [SerializeField] private Texture2D _arrowTexture;
    [SerializeField] private Vector2 _arrowSize = new Vector2(40f, 40f);
    [SerializeField, Tooltip("텍스처 왼쪽 위 기준 0~1 클릭 지점입니다.")] private Vector2 _arrowHotspot = new Vector2(0.0625f, 0.0625f);
    [SerializeField] private Texture2D _fingerTexture;
    [SerializeField] private Vector2 _fingerSize = new Vector2(40f, 40f);
    [SerializeField, Tooltip("텍스처 왼쪽 위 기준 0~1 클릭 지점입니다.")] private Vector2 _fingerHotspot = new Vector2(0.234375f, 0.21875f);

    [Header("Movement")]
    [SerializeField, Range(0.25f, 3f)] private float _sensitivity = 1f;
    private Vector2 _position;
    private bool _hasFocus;
    private bool _waitRelease = true;
    private int _blockedFrame;
    private int _dialogDepth;
    private bool _modeWasActive;

    [Header("Runtime UI")]
    private RawImage _graphic;
    private InGameCursorDevice _device;
    private InputActionAsset _pointerAsset;
    private InputActionReference[] _pointerActions;
    private readonly Dictionary<InputSystemUIInputModule, InputActionReference[]> _modules = new Dictionary<InputSystemUIInputModule, InputActionReference[]>();
    private readonly HashSet<InputSystemUIInputModule> _suspendedModules = new HashSet<InputSystemUIInputModule>();
    private readonly List<InputSystemUIInputModule> _removedModules = new List<InputSystemUIInputModule>();

    public static InGameCursor Instance => _instance;
    public static Vector2 Position => _instance != null && _instance.IsActive ? _instance._position : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    public static bool CanProcessPointer => _instance == null || (_instance._hasFocus && _instance._dialogDepth == 0 && !_instance._waitRelease && Time.frameCount > _instance._blockedFrame);
    public static bool CanProcessVirtualPointer => _instance != null && _instance.IsActive && CanProcessPointer;
    public float Sensitivity => _sensitivity;
    public event Action<float> SensitivityChanged;

    public static CursorLockMode LockState
    {
        get => _requestedLock;
        set
        {
            if (_requestedLock == value) return;
            _requestedLock = value;
            if (_instance != null) _instance.ModeChanged();
            else Cursor.lockState = value;
        }
    }

    public static bool Visible
    {
        get => _requestedVisible;
        set
        {
            if (_requestedVisible == value) return;
            _requestedVisible = value;
            if (_instance != null) _instance.ModeChanged();
            else Cursor.visible = value;
        }
    }

    private bool IsActive => _hasFocus && _dialogDepth == 0 && _requestedVisible && _requestedLock != CursorLockMode.Locked;

    /// <summary>이전 실행의 정적 참조를 비운다. 도메인 재로드를 끈 실행에서도 새 실행의 기본 모드를 사용한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntime()
    {
        _instance = null;
        _requestedLock = CursorLockMode.None;
        _requestedVisible = true;
        PointerCanceled = null;
    }

    /// <summary>첫 씬 전에 설정 Prefab 한 개를 생성한다. 이후 씬 이동에서는 같은 인스턴스와 런타임 민감도를 유지한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Instantiate(Resources.Load<GameObject>("InGameCursor"));
    }

    void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        _hasFocus = Application.isFocused;
        _position = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width, Screen.height) * 0.5f;
        InputSystem.RegisterLayout<InGameCursorDevice>();
        _device = InputSystem.AddDevice<InGameCursorDevice>();
        BuildActions();
        BuildGraphic();
        InputSystem.onAfterUpdate += UpdatePointer;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ModeChanged();
    }

    void Update()
    {
        BindSceneModules();
        ApplyPresentation();
    }

    void LateUpdate()
    {
        ApplyPresentation();
    }

    /// <summary>value를 유한한 0.25~3 배율로 검증하고 민감도와 변경 알림을 갱신한다. 잘못된 값은 예외로 거부한다.</summary>
    public void SetSensitivity(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        value = Mathf.Clamp(value, 0.25f, 3f);
        if (Mathf.Approximately(_sensitivity, value)) return;
        _sensitivity = value;
        SensitivityChanged?.Invoke(value);
    }

    /// <summary>design을 현재 표시 디자인으로 변경한다. 공통 논리 좌표는 이동시키지 않는다.</summary>
    public void SetDesign(CursorDesign design)
    {
        if (!Enum.IsDefined(typeof(CursorDesign), design)) throw new ArgumentOutOfRangeException(nameof(design));
        _design = design;
        ApplyPresentation();
    }

    /// <summary>시스템 파일 선택창이 닫힐 때까지 게임 입력을 중단하는 토큰을 반환한다. Dispose 시 현재 게임 모드로 복구한다.</summary>
    public static IDisposable SuspendForSystemDialog()
    {
        return new DialogScope(_instance);
    }

    /// <summary>UI 전용 장치의 위치와 버튼 및 휠만 읽는 UI 액션을 생성한다. 카메라 및 전투 액션은 수정하지 않는다.</summary>
    private void BuildActions()
    {
        _pointerAsset = ScriptableObject.CreateInstance<InputActionAsset>();
        var map = new InputActionMap("UI");
        _pointerAsset.AddActionMap(map);
        string[] controls = { "position", "leftButton", "rightButton", "middleButton", "scroll" };
        _pointerActions = new InputActionReference[controls.Length];
        for (int index = 0; index < controls.Length; index++)
        {
            InputAction action = map.AddAction(controls[index], InputActionType.PassThrough,
                "<InGameCursorDevice>/" + controls[index], expectedControlLayout: index == 0 || index == 4 ? "Vector2" : "Button");
            _pointerActions[index] = InputActionReference.Create(action);
        }
        map.Enable();
    }

    /// <summary>픽셀 좌표의 최상위 Canvas와 레이캐스트를 차단하지 않는 커서 이미지를 생성한다. EventSystem은 생성하지 않는다.</summary>
    private void BuildGraphic()
    {
        var canvasObject = new GameObject("CursorCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var imageObject = new GameObject("CursorGraphic", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform, false);
        _graphic = imageObject.GetComponent<RawImage>();
        _graphic.raycastTarget = false;
        _graphic.rectTransform.anchorMin = Vector2.zero;
        _graphic.rectTransform.anchorMax = Vector2.zero;
    }

    /// <summary>실제 마우스 delta에 민감도만 곱해 논리 좌표와 UI 상태를 갱신한다. 시간 배율과 프레임 시간은 사용하지 않는다.</summary>
    private void UpdatePointer()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || _device == null) return;
        Mouse mouse = Mouse.current;
        bool active = IsActive;
        if (active != _modeWasActive)
        {
            _modeWasActive = active;
            Rearm();
        }
        bool released = mouse == null || (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed
            && !mouse.middleButton.isPressed && !mouse.forwardButton.isPressed && !mouse.backButton.isPressed);
        if (_waitRelease && released && _hasFocus && _dialogDepth == 0)
        {
            _waitRelease = false;
            _blockedFrame = Time.frameCount;
        }
        bool accept = active && CanProcessPointer && mouse != null;
        if (accept) _position += mouse.delta.ReadValue() * _sensitivity;
        _position.x = Mathf.Clamp(_position.x, 0f, Mathf.Max(0, Screen.width - 1));
        _position.y = Mathf.Clamp(_position.y, 0f, Mathf.Max(0, Screen.height - 1));
        ushort buttons = 0;
        if (accept)
        {
            if (mouse.leftButton.isPressed) buttons |= 1;
            if (mouse.rightButton.isPressed) buttons |= 2;
            if (mouse.middleButton.isPressed) buttons |= 4;
            if (mouse.forwardButton.isPressed) buttons |= 8;
            if (mouse.backButton.isPressed) buttons |= 16;
        }
        InputState.Change(_device, new InGameCursorState(_position, accept ? mouse.scroll.ReadValue() : Vector2.zero, buttons));
    }

    /// <summary>모드 전환 후 모든 버튼 해제를 기다린다. 누적 이동과 눌림 입력을 다음 모드에 전달하지 않는다.</summary>
    private void Rearm()
    {
        _waitRelease = true;
        _blockedFrame = Time.frameCount;
        PointerCanceled?.Invoke();
    }

    /// <summary>요청된 표시와 잠금 변경을 입력 대기 상태에 반영한다. 같은 값을 반복 요청할 때는 대기를 반복하지 않는다.</summary>
    private void ModeChanged()
    {
        Rearm();
        ApplyPresentation();
    }

    /// <summary>현재 모드와 디자인으로 커서를 표시하고 게임 조작 중에는 마우스를 창 안에 가둔다. 포커스 상실과 시스템 창에서는 잠금을 해제한다.</summary>
    private void ApplyPresentation()
    {
        bool external = !_hasFocus || _dialogDepth > 0;
        Cursor.lockState = external ? CursorLockMode.None
            : _requestedLock == CursorLockMode.Locked ? CursorLockMode.Locked : CursorLockMode.Confined;
        Cursor.visible = external;
        if (_graphic == null) return;
        _graphic.enabled = IsActive;
        bool arrow = _design == CursorDesign.KeycapArrow;
        _graphic.texture = arrow ? _arrowTexture : _fingerTexture;
        _graphic.rectTransform.sizeDelta = arrow ? _arrowSize : _fingerSize;
        Vector2 hotspot = arrow ? _arrowHotspot : _fingerHotspot;
        _graphic.rectTransform.pivot = new Vector2(Mathf.Clamp01(hotspot.x), 1f - Mathf.Clamp01(hotspot.y));
        _graphic.rectTransform.anchoredPosition = _position;
        SetModulesSuspended(external || _waitRelease);
    }

    /// <summary>새 씬의 UI 모듈을 전용 포인터에 연결한다. 저장된 민감도와 좌표는 유지하고 전환 입력만 대기한다.</summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Rearm();
        BindSceneModules();
    }

    /// <summary>현재 씬의 UI 모듈을 찾아 연결하고 파괴된 모듈 기록을 정리한다. 런타임에 생성된 UI도 같은 입력을 사용한다.</summary>
    private void BindSceneModules()
    {
        foreach (InputSystemUIInputModule module in FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            ConfigureUI(module);
        _removedModules.Clear();
        foreach (InputSystemUIInputModule module in _modules.Keys)
            if (module == null) _removedModules.Add(module);
        foreach (InputSystemUIInputModule module in _removedModules)
        {
            _modules.Remove(module);
            _suspendedModules.Remove(module);
        }
    }

    /// <summary>module의 마우스 입력만 전용 포인터 액션으로 연결한다. 키보드 탐색과 확인 및 취소 바인딩은 유지한다.</summary>
    public static void ConfigureUI(InputSystemUIInputModule module)
    {
        if (_instance == null || module.point == _instance._pointerActions[0]) return;
        if (!_instance._modules.ContainsKey(module))
            _instance._modules.Add(module, new[] { module.point, module.leftClick, module.rightClick, module.middleClick,
                module.scrollWheel, module.move, module.submit, module.cancel, module.trackedDevicePosition, module.trackedDeviceOrientation });
        InputActionReference[] previous = _instance._modules[module];
        if (module.move == null) module.move = previous[5];
        if (module.submit == null) module.submit = previous[6];
        if (module.cancel == null) module.cancel = previous[7];
        module.point = _instance._pointerActions[0];
        module.leftClick = _instance._pointerActions[1];
        module.rightClick = _instance._pointerActions[2];
        module.middleClick = _instance._pointerActions[3];
        module.scrollWheel = _instance._pointerActions[4];
    }

    /// <summary>suspended 동안 UI 모듈 처리를 중단해 창 복귀 클릭을 차단한다. 관리자가 중단한 모듈만 다시 활성화한다.</summary>
    private void SetModulesSuspended(bool suspended)
    {
        foreach (InputSystemUIInputModule module in _modules.Keys)
        {
            if (module == null) continue;
            if (suspended && module.enabled)
            {
                _suspendedModules.Add(module);
                module.enabled = false;
            }
            else if (!suspended && _suspendedModules.Remove(module))
            {
                module.enabled = true;
                ConfigureUI(module);
            }
        }
    }

    void OnApplicationFocus(bool focused)
    {
        _hasFocus = focused;
        Rearm();
        ApplyPresentation();
    }

    void OnValidate()
    {
        _sensitivity = Mathf.Clamp(_sensitivity, 0.25f, 3f);
        _arrowSize = Vector2.Max(Vector2.one, _arrowSize);
        _fingerSize = Vector2.Max(Vector2.one, _fingerSize);
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        InputSystem.onAfterUpdate -= UpdatePointer;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        foreach (var entry in _modules)
        {
            if (entry.Key == null) continue;
            entry.Key.point = entry.Value[0];
            entry.Key.leftClick = entry.Value[1];
            entry.Key.rightClick = entry.Value[2];
            entry.Key.middleClick = entry.Value[3];
            entry.Key.scrollWheel = entry.Value[4];
            entry.Key.move = entry.Value[5];
            entry.Key.submit = entry.Value[6];
            entry.Key.cancel = entry.Value[7];
            entry.Key.trackedDevicePosition = entry.Value[8];
            entry.Key.trackedDeviceOrientation = entry.Value[9];
        }
        SetModulesSuspended(false);
        foreach (InputActionReference reference in _pointerActions) Destroy(reference);
        _pointerAsset.Disable();
        Destroy(_pointerAsset);
        if (_device != null && _device.added) InputSystem.RemoveDevice(_device);
        _instance = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private sealed class DialogScope : IDisposable
    {
        private InGameCursor _owner;

        /// <summary>owner의 대화상자 중단 횟수를 늘리고 시스템 커서를 즉시 복구한다. 중첩 호출도 같은 상태를 공유한다.</summary>
        public DialogScope(InGameCursor owner)
        {
            _owner = owner;
            if (owner == null) return;
            owner._dialogDepth++;
            owner.Rearm();
            owner.ApplyPresentation();
        }

        /// <summary>대화상자 중단을 한 번만 해제한다. 버튼을 모두 놓기 전에는 게임 입력이 다시 시작되지 않는다.</summary>
        public void Dispose()
        {
            if (_owner == null) return;
            _owner._dialogDepth--;
            _owner.Rearm();
            _owner.ApplyPresentation();
            _owner = null;
        }
    }
}
