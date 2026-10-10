using UnityEngine;
using UnityEngine.InputSystem;

using Game.Shop;
using Unity.Cinemachine;

public class MonitorController : MonoBehaviour
{
    [Header("Canvases")]
    [SerializeField] private GameObject _shopCanvas;
    [Tooltip("상점 전용 씬에서는 비워 둘 수 있습니다.")]
    [SerializeField] private GameObject _miniGameCanvas;
    [SerializeField] private CinemachineCamera monitorCam;

    [Header("Shop")]
    [SerializeField] private ShopView _shopView;

    [Header("Input")]
    [SerializeField] private InputAction _toggleAction;
    [SerializeField] private bool _allowTabClose = true;

    void OnEnable()
    {
        _shopView.OpenStateChanged += SetCanvasState;
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
    }

    void OnDestroy()
    {
        _toggleAction.Dispose();
    }

    /// <summary>
    /// context의 Tab 입력으로 상점 표시를 전환한다.
    /// 상점이 열린 상태에서는 _allowTabClose가 허용할 때만 닫는다.
    /// </summary>
    private void OnTogglePerformed(InputAction.CallbackContext context)
    {
        if (_shopView.IsOpen && !_allowTabClose)
            return;

        //monitorCam.Priority = monitorCam.Priority == 0 ? 30 : 0;

        _shopView.Toggle();
    }

    /// <summary>
    /// isShopOpen으로 상점과 미니게임 캔버스의 표시 상태를 변경한다.
    /// 실제 상점 상태에 맞춰 모니터 카메라의 우선순위를 설정한다.
    /// </summary>
    private void SetCanvasState(bool isShopOpen)
    {
        monitorCam.Priority = isShopOpen ? 30 : 0;

        _shopCanvas.SetActive(isShopOpen);

        if (_miniGameCanvas != null)
        {
            _miniGameCanvas.SetActive(!isShopOpen);
        }
    }
}
