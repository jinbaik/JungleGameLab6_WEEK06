using UnityEngine;
using UnityEngine.InputSystem;

using Game.Shop;

public class MonitorController : MonoBehaviour
{
    [Header("Canvases")]
    [SerializeField] private GameObject _shopCanvas;
    [Tooltip("상점 전용 씬에서는 비워 둘 수 있습니다.")]
    [SerializeField] private GameObject _miniGameCanvas;

    [Header("Shop")]
    [SerializeField] private ShopView _shopView;

    [Header("Input")]
    private readonly InputAction _toggleAction = new InputAction("ToggleShop", InputActionType.Button, "<Keyboard>/tab");
    [SerializeField] private bool _allowTabClose = true;

    void OnEnable()
    {
        _shopView.OpenStateChanged += SetCanvasState;
        _toggleAction.performed += OnTogglePerformed;
        _toggleAction.Enable();
    }

    void Start()
    {
        SetCanvasState(_shopView.IsOpen);
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

        _shopView.Toggle();
    }

    /// <summary>
    /// 상점 표시 상태에 따라 두 캔버스의 활성 상태를 반대로 설정한다.
    /// isShopOpen을 상점 캔버스에 반영하고 연결된 미니게임 캔버스에는 반대 상태를 적용한다.
    /// </summary>
    private void SetCanvasState(bool isShopOpen)
    {
        _shopCanvas.SetActive(isShopOpen);
        if (_miniGameCanvas != null)
        {
            _miniGameCanvas.SetActive(!isShopOpen);
        }
    }
}
