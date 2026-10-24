using System;

using UnityEngine;
using UnityEngine.InputSystem;

using Unity.Cinemachine;

namespace KeyboardModeling.Assembly
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class AssemblyFocusController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform _keyboardRoot;
        [SerializeField] private Transform _workbench;
        [SerializeField, Min(0.1f)] private float _interactionDistance = 12f;
        [SerializeField] private LayerMask _interactionLayers = ~0;

        [Header("Cameras")]
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera _inputCamera;
        [SerializeField] private CinemachineCamera _focusCamera;
        [SerializeField] private CinemachineInputAxisController _inputAxes;
        private bool _inputAxesWereEnabled;

        [Header("Assembly")]
        [SerializeField] private AssemblyDragInput _dragInput;
        [SerializeField] private AssemblyCatalogUI _catalog;
        [SerializeField] private AssemblyKeyboardPickup _pickupController;
        private bool _isFocused;
        private bool _initialized;

        public bool IsFocused => _isFocused;
        public event Action<bool> FocusChanged;

        void Awake()
        {
            _inputAxesWereEnabled = _inputAxes.enabled;
            _dragInput.SetInputAllowed(false, _brain.OutputCamera);
        }

        void Start()
        {
            _initialized = true;
            SetFocused(false);
        }

        void OnEnable()
        {
            if (_initialized)
                SetFocused(false);
        }

        void Update()
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking)
                return;

            if (_isFocused)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    SetFocused(false);
                return;
            }

            if (_pickupController.IsPlaced && !_pickupController.PlacementInputConsumed
                && !_brain.IsBlending && InGameCursor.CanProcessPointer && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame
                && IsTargetInCenter())
                SetFocused(true);
        }

        /// <summary>
        /// 일반 모드 출력 화면의 중앙 Ray에서 가장 가까운 실체 Collider가 대상 키보드인지 반환한다.
        /// 명시적으로 연결한 작업대와 키보드를 사용하며 가림막이나 분리 부품 뒤의 대상을 선택하지 않는다.
        /// </summary>
        private bool IsTargetInCenter()
        {
            if (!_keyboardRoot.gameObject.activeInHierarchy || !_workbench.gameObject.activeInHierarchy)
                return false;

            Physics.SyncTransforms();
            Ray ray = _brain.OutputCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return Physics.Raycast(ray, out RaycastHit hit, _interactionDistance, _interactionLayers,
                QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(_keyboardRoot);
        }

        /// <summary>
        /// focused에 따라 기존 Cinemachine 카메라와 커서 및 조립 입력과 인벤토리 표시를 전환한다.
        /// 종료 시 기존 취소 정책으로 조작을 정리하며 사용자 카메라 자세와 렌즈 및 재고는 변경하지 않는다.
        /// </summary>
        private void SetFocused(bool focused)
        {
            _dragInput.SetInputAllowed(false, _brain.OutputCamera);
            _isFocused = focused;
            _inputAxes.enabled = !focused && _inputAxesWereEnabled;
            _inputCamera.enabled = !focused;
            _focusCamera.enabled = focused;
            _catalog.SetInventoryVisible(focused);
            _dragInput.SetInputAllowed(focused, _brain.OutputCamera);
            ApplyCursor();
            FocusChanged?.Invoke(focused);
        }

        /// <summary>
        /// 현재 집중 상태에 맞춰 커서 잠금과 표시를 적용한다.
        /// 집중 모드에서 커서를 해제하며 일반 모드에서는 잠그고 숨긴다.
        /// </summary>
        private void ApplyCursor()
        {
            InGameCursor.LockState = _isFocused ? CursorLockMode.None : CursorLockMode.Locked;
            InGameCursor.Visible = _isFocused;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!_initialized)
                return;
            if (!hasFocus)
                _dragInput.CancelDrag();
            else if (!Game.Tutorials.FirstEncounterTutorial.IsBlocking)
                ApplyCursor();
        }

        void OnDisable()
        {
            if (!_initialized)
                return;
            _isFocused = false;
            if (_dragInput != null)
                _dragInput.SetInputAllowed(false, _dragInput.InputCamera);
            if (_catalog != null)
                _catalog.SetInventoryVisible(false);
            FocusChanged?.Invoke(false);
            if (_inputAxes != null)
                _inputAxes.enabled = _inputAxesWereEnabled;
            if (_inputCamera != null)
                _inputCamera.enabled = true;
            if (_focusCamera != null)
                _focusCamera.enabled = false;
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
        }
    }
}
