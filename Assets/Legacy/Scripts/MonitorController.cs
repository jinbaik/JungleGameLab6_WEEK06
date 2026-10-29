using System;

using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

using Game.Shop;
using KeyboardModeling;

public class MonitorController : MonoBehaviour
{
    [Header("Canvases")]
    [SerializeField] private GameObject _shopCanvas;
    [Tooltip("상점 전용 씬에서는 비워 둘 수 있습니다.")]
    [SerializeField] private GameObject _miniGameCanvas;
    [SerializeField] private CinemachineCamera monitorCam;

    [Header("Shop")]
    [SerializeField] private ShopView _shopView;

    [Header("Desktop")]
    [Tooltip("바탕화면을 연결하지 않은 개발 씬은 기존 화면 전환을 사용합니다.")]
    [SerializeField] private MonitorDesktopView _desktopView;
    [SerializeField] private KeyboardGoalAppView _goalAppView;
    [SerializeField] private MonitorSettingsAppView _settingsAppView;
    [SerializeField] private CanvasGroup _shopCanvasGroup;
    [SerializeField] private MiniGameController _miniGameController;
    [SerializeField] private KeyboardInteractionController _interactionController;
    [Tooltip("켜면 상점 사용 중에도 다음 이벤트 시간을 세고 미니게임이 상점을 닫고 진입합니다.")]
    [SerializeField] private bool _allowMiniGameInterruptShop;
    private bool _isMonitorFocused;
    public bool IsMonitorFocused => _isMonitorFocused;
    public bool CanAdvanceEventTimer => _interactionController.CanRunMiniGame && _miniGameController.HasKeyboard
        && (_isMonitorFocused
            ? _allowMiniGameInterruptShop && _shopView.IsOpen
            : _miniGameController.InputAvailable);
    public event Action<bool> FocusChanged;

    [Header("Input")]
    [SerializeField] private InputAction _toggleAction;
    [SerializeField] private bool _allowTabClose = true;

    void OnEnable()
    {
        _shopView.OpenStateChanged += SetCanvasState;
        if (_desktopView != null)
        {
            _desktopView.ShopOpenRequested += OpenShop;
            _miniGameController.StateChanged += RefreshMiniGameState;
        }
        if (_goalAppView != null)
        {
            _goalAppView.OpenRequested += OpenGoal;
            _goalAppView.OpenStateChanged += HandleGoalOpenStateChanged;
        }
        if (_settingsAppView != null)
        {
            _settingsAppView.OpenRequested += OpenSettings;
            _settingsAppView.OpenStateChanged += HandleSettingsOpenStateChanged;
        }
        _toggleAction.performed += OnTogglePerformed;
        _toggleAction.Enable();
    }

    void Start()
    {
        monitorCam.Priority = 0;
        _shopView.SetOpen(false);
        SetCanvasState(false);
    }

    void OnDisable()
    {
        _toggleAction.Disable();
        _toggleAction.performed -= OnTogglePerformed;
        _shopView.OpenStateChanged -= SetCanvasState;
        if (_goalAppView != null)
        {
            _goalAppView.OpenRequested -= OpenGoal;
            _goalAppView.OpenStateChanged -= HandleGoalOpenStateChanged;
        }
        if (_settingsAppView != null)
        {
            _settingsAppView.SetOpen(false);
            _settingsAppView.OpenRequested -= OpenSettings;
            _settingsAppView.OpenStateChanged -= HandleSettingsOpenStateChanged;
        }
        if (_desktopView != null)
        {
            _desktopView.ShopOpenRequested -= OpenShop;
            _miniGameController.StateChanged -= RefreshMiniGameState;
            SetMonitorFocus(false);
        }
    }

    void OnDestroy()
    {
        _toggleAction.Dispose();
    }

    /// <summary>
    /// context의 Tab 입력으로 모니터 조작에 진입하거나 부수기로 복귀한다.
    /// 바탕화면이 없는 전용 씬에서는 기존 상점 전환과 닫기 제한을 유지한다.
    /// </summary>
    private void OnTogglePerformed(InputAction.CallbackContext context)
    {
        if (_desktopView != null)
        {
            if (_miniGameController.CurrentGame != MiniGameController.MiniGameKind.Idle)
                return;
            SetMonitorFocus(!_isMonitorFocused);
            return;
        }

        if (_shopView.IsOpen && !_allowTabClose)
            return;

        _shopView.Toggle();
    }

    /// <summary>
    /// isFocused로 모니터 조작 상태와 카메라 우선순위를 변경한다.
    /// 진입 가능 상태를 검사하고 복귀 시 상점을 닫으며 FocusChanged로 입력 전환을 알린다.
    /// </summary>
    public void SetMonitorFocus(bool isFocused)
    {
        if (_isMonitorFocused == isFocused || (isFocused && !_interactionController.CanEnterMonitor))
            return;

        if (!isFocused)
        {
            _shopView.SetOpen(false);
            if (_goalAppView != null) _goalAppView.SetOpen(false);
            if (_settingsAppView != null) _settingsAppView.SetOpen(false);
        }

        _isMonitorFocused = isFocused;
        monitorCam.Priority = isFocused ? 30 : 0;
        FocusChanged?.Invoke(isFocused);
        RefreshDesktopState();
    }

    /// <summary>
    /// 만료된 이벤트가 부수기 입력으로 시작하도록 모니터 조작을 종료한다.
    /// 열린 상점을 닫고 입력 복원을 예약하며 타이머와 미니게임 진행도는 변경하지 않는다.
    /// </summary>
    public void PrepareMiniGame()
    {
        SetMonitorFocus(false);
    }

    /// <summary>
    /// 앱의 더블 클릭 요청을 받아 조작 가능한 바탕화면에서 상점을 연다.
    /// 미니게임 실행 중에는 요청을 무시하며 기존 상품 목록을 재사용한다.
    /// </summary>
    private void OpenShop()
    {
        if (_isMonitorFocused && _miniGameController.CurrentGame == MiniGameController.MiniGameKind.Idle)
            _shopView.SetOpen(true);
    }

    /// <summary>
    /// 목표 앱의 요청을 받아 모니터 조작 중 목표 팝업을 연다.
    /// 미니게임 상태를 확인하고 기존 상점을 닫은 뒤 팝업 상태를 변경한다.
    /// </summary>
    private void OpenGoal()
    {
        if (!_isMonitorFocused || _miniGameController.CurrentGame != MiniGameController.MiniGameKind.Idle)
            return;
        _shopView.SetOpen(false);
        if (_settingsAppView != null) _settingsAppView.SetOpen(false);
        _goalAppView.SetOpen(true);
    }

    /// <summary>
    /// isOpen으로 전달된 목표 팝업 변경을 화면 입력 상태에 반영한다.
    /// 열림 이벤트의 상태를 확인하고 상점 중복 표시를 막으며 바탕화면을 갱신한다.
    /// </summary>
    private void HandleGoalOpenStateChanged(bool isOpen)
    {
        if (isOpen)
        {
            _shopView.SetOpen(false);
            if (_settingsAppView != null) _settingsAppView.SetOpen(false);
        }
        RefreshDesktopState();
    }

    /// <summary>
    /// 설정 앱의 요청을 받아 기존 모니터 진입 규칙 안에서 팝업을 연다.
    /// 다른 앱을 닫고 설정 창과 모니터 입력 상태를 갱신한다.
    /// </summary>
    private void OpenSettings()
    {
        if (!_isMonitorFocused || !_interactionController.CanEnterMonitor
            || _miniGameController.CurrentGame != MiniGameController.MiniGameKind.Idle) return;
        _shopView.SetOpen(false);
        if (_goalAppView != null) _goalAppView.SetOpen(false);
        _settingsAppView.SetInputEnabled(true);
        _settingsAppView.SetOpen(true);
    }

    /// <summary>
    /// isOpen으로 전달된 설정 창 변경을 모니터 입력 권한에 반영한다.
    /// 열림 시 다른 앱을 닫고 바탕화면 및 팝업의 조작 상태를 갱신한다.
    /// </summary>
    private void HandleSettingsOpenStateChanged(bool isOpen)
    {
        if (isOpen)
        {
            _shopView.SetOpen(false);
            if (_goalAppView != null) _goalAppView.SetOpen(false);
        }
        RefreshDesktopState();
    }

    /// <summary>
    /// isShopOpen에 맞춰 상점 표시를 갱신한다.
    /// 바탕화면에서는 모니터 조작을 유지하며 기존 전용 씬에서는 카메라와 캔버스를 전환한다.
    /// </summary>
    private void SetCanvasState(bool isShopOpen)
    {
        if (_desktopView != null)
        {
            if (isShopOpen && _goalAppView != null) _goalAppView.SetOpen(false);
            if (isShopOpen && _settingsAppView != null) _settingsAppView.SetOpen(false);
            RefreshDesktopState();
            return;
        }

        monitorCam.Priority = isShopOpen ? 30 : 0;

        _shopCanvas.SetActive(isShopOpen);

        if (_miniGameCanvas != null)
        {
            _miniGameCanvas.SetActive(!isShopOpen);
        }
    }

    /// <summary>
    /// 현재 미니게임 상태 변경을 받아 팝업과 앱 입력을 갱신한다.
    /// 실행 중에는 상점을 닫고 완료 후 바탕화면을 표시하며 진행 상태는 변경하지 않는다.
    /// </summary>
    private void RefreshMiniGameState()
    {
        if (_miniGameController.CurrentGame != MiniGameController.MiniGameKind.Idle)
            PrepareMiniGame();
        RefreshDesktopState();
    }

    /// <summary>
    /// 모니터 조작, 상점과 미니게임 상태로 화면과 마우스 입력 권한을 갱신한다.
    /// 바탕화면을 유지하고 상점 및 미니게임 팝업만 표시하거나 숨긴다.
    /// </summary>
    private void RefreshDesktopState()
    {
        bool isMiniGameRunning = _miniGameController.CurrentGame != MiniGameController.MiniGameKind.Idle;
        bool isShopOpen = _shopView.IsOpen;
        bool isGoalOpen = _goalAppView != null && _goalAppView.IsOpen;
        bool isSettingsOpen = _settingsAppView != null && _settingsAppView.IsOpen;
        _shopCanvas.SetActive(true);
        _shopCanvasGroup.alpha = isShopOpen ? 1f : 0f;
        _shopCanvasGroup.interactable = _isMonitorFocused && isShopOpen && !isMiniGameRunning && !isSettingsOpen;
        _shopCanvasGroup.blocksRaycasts = _shopCanvasGroup.interactable;
        _shopView.SetInputEnabled(_isMonitorFocused && !isMiniGameRunning && !isSettingsOpen);
        _desktopView.SetInputEnabled(_isMonitorFocused && !isShopOpen && !isGoalOpen && !isSettingsOpen && !isMiniGameRunning);
        if (_goalAppView != null)
            _goalAppView.SetInputEnabled(_isMonitorFocused && !isShopOpen && !isSettingsOpen && !isMiniGameRunning);
        if (_settingsAppView != null)
            _settingsAppView.SetInputEnabled(_isMonitorFocused && !isShopOpen && !isGoalOpen && !isMiniGameRunning);
        _miniGameCanvas.SetActive(isMiniGameRunning);
    }
}
