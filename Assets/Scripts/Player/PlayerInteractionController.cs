using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Unity.Cinemachine;

using KeyboardModeling;

namespace Game.Player
{
    /// <summary>
    /// 필드에서 마우스로 선택할 수 있는 상호작용 대상의 동작을 정의한다.
    /// Interact를 호출하며 대상이 변경할 상태는 구현체가 관리한다.
    /// </summary>
    public interface IPlayerInteractable
    {
        /// <summary>
        /// 플레이어의 상호작용 입력에 반응하여 대상의 동작을 실행한다.
        /// 반환값은 없으며 대상의 주요 상태를 변경한다.
        /// </summary>
        void Interact();
    }

    public sealed class PlayerInteractionController : MonoBehaviour
    {
        private const float KEYBOARD_EDIT_ROTATION_SENSITIVITY = 0.2f;
        private const float INTERACTION_PROMPT_CURSOR_OFFSET = 24f;

        [Header("Interaction")]
        [SerializeField] private Camera _camera;
        [SerializeField] private InputActionReference _clickInput;
        [SerializeField] private InputActionReference _coverInput;
        [SerializeField] private InputActionReference _moveInput;
        [SerializeField] private float _interactionDistance = 3f;
        [SerializeField] private LayerMask _interactionLayers = ~0;
        [SerializeField] private Text _interactionPrompt;
        private bool _contextHintsEnabled;
        private RectTransform _interactionPromptRectTransform;
        private Vector2 _interactionPromptDefaultAnchorMin;
        private Vector2 _interactionPromptDefaultAnchorMax;
        private Vector2 _interactionPromptDefaultPivot;
        private Vector2 _interactionPromptDefaultAnchoredPosition;
        private readonly RaycastHit[] _interactionHits = new RaycastHit[32];

        [Header("Upload Confirmation Input")]
        private bool _uploadConfirmationInputCaptured;
        private bool _uploadConfirmationCameraInputWasEnabled;
        private CursorLockMode _uploadConfirmationCursorLockState;
        private bool _uploadConfirmationCursorWasVisible;

        [Header("Keyboard Cover")]
        [SerializeField] private KeyboardProtectionCover _keyboardProtectionCoverPrefab;

        [Header("Keyboard Pickup")]
        [SerializeField, Min(0.1f)] private float _heldKeyboardDistance = 2f;
        [SerializeField, Min(-1f)] private float _heldKeyboardCenterHeight = 0.12f;
        [SerializeField, Min(0f)] private float _keyboardThrowSpeed = 10f;
        [SerializeField] private Vector3 _heldKeyboardEulerAngles = new Vector3(-75f, 0f, 0f);
        private Rigidbody _heldKeyboardBody;
        private Vector3 _heldKeyboardCenterOffset;
        private bool _heldKeyboardWasKinematic;
        private bool _heldKeyboardUsedGravity;
        private Collider[] _playerColliders;
        private readonly List<(Collider Keyboard, Collider Player, bool WasIgnored)> _heldCollisionStates
            = new List<(Collider Keyboard, Collider Player, bool WasIgnored)>();

        [Header("Keyboard Edit")]
        [SerializeField] private Transform _keyboardEditPosition;
        [SerializeField, Min(0.1f)] private float _keyboardEditDistance = 2.5f;
        [SerializeField] private CinemachinePanTilt _cameraPanTilt;
        [SerializeField] private CinemachineInputAxisController _cameraInputAxisController;
        [SerializeField, Min(0.01f)] private float _cameraTransitionDuration = 0.45f;
        [SerializeField] private GameObject _keyboardEditOverlay;
        [SerializeField] private WorkshopKeyboardPullerController _keyboardPullers;
        [SerializeField] private GameObject _centerReticle;
        [SerializeField, Range(1f, 179f)] private float _keyboardEditMinFieldOfView = 40f;
        [SerializeField, Range(1f, 179f)] private float _keyboardEditMaxFieldOfView = 100f;
        [SerializeField, Min(0.01f)] private float _keyboardEditFieldOfViewStep = 0.1f;
        private CinemachineCamera _editingCinemachineCamera;
        private KeyboardEditOverlayUI _keyboardEditOverlayUI;
        private KeyboardInputController _editingKeyboard;
        private Coroutine _cameraTransition;
        private Vector3 _savedPlayerPosition;
        private Quaternion _savedCameraRotation;
        private bool _moveInputWasEnabled;
        private bool _cameraInputWasEnabled;
        private bool _editingKeyboardAttackWasEnabled;
        private bool _isKeyboardEditMode;
        public bool IsKeyboardEditing => _isKeyboardEditMode && _cameraTransition == null;
        public KeyboardInputController EditingKeyboard => _editingKeyboard;
        private Rigidbody _editingKeyboardBody;
        private float _savedCameraFieldOfView;
        private InputAction _keyboardResetInput;
        private Quaternion _initialEditingKeyboardRotation;
        private Vector3 _editingKeyboardCenterOffset;
        private bool _isDraggingEditingKeyboard;
        private Vector2 _pendingKeyboardRotationDelta;
        private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();

        [Header("Monitor Focus")]
        [SerializeField, Min(0.1f)] private float _monitorFocusDistance = 2.5f;
        [SerializeField, Range(1f, 179f)] private float _monitorFocusFieldOfView = 75f;
        private Coroutine _monitorFocusTransition;
        private Vector3 _monitorSavedPlayerPosition;
        private Quaternion _monitorSavedCameraRotation;
        private float _monitorSavedFieldOfView;
        private bool _monitorMoveInputWasEnabled;
        private bool _monitorCameraInputWasEnabled;
        private bool _isMonitorFocusMode;
        private MonitorKeyboardHolder _focusedMonitorHolder;
        public event System.Action<MonitorKeyboardHolder> MonitorFocusChanged;

        void Awake()
        {
            _playerColliders = GetComponentsInChildren<Collider>(true);
            _keyboardResetInput = new InputAction("ResetEditingKeyboard", InputActionType.Button, "<Mouse>/middleButton");

            if (_camera == null)
                _camera = Camera.main;

            _interactionPromptRectTransform = _interactionPrompt.rectTransform;
            _interactionPromptDefaultAnchorMin = _interactionPromptRectTransform.anchorMin;
            _interactionPromptDefaultAnchorMax = _interactionPromptRectTransform.anchorMax;
            _interactionPromptDefaultPivot = _interactionPromptRectTransform.pivot;
            _interactionPromptDefaultAnchoredPosition = _interactionPromptRectTransform.anchoredPosition;

            if (_cameraPanTilt == null)
                _cameraPanTilt = GetComponentInChildren<CinemachinePanTilt>();

            if (_cameraInputAxisController == null)
                _cameraInputAxisController = GetComponentInChildren<CinemachineInputAxisController>();

            if (_cameraPanTilt != null)
                _editingCinemachineCamera = _cameraPanTilt.GetComponent<CinemachineCamera>();

            if (_moveInput == null)
                _moveInput = FindMoveInputReference();

            if (_keyboardEditOverlay != null)
                _keyboardEditOverlayUI = _keyboardEditOverlay.GetComponent<KeyboardEditOverlayUI>();
        }

        void OnEnable()
        {
            InGameCursor.LockState = CursorLockMode.Locked;
            InGameCursor.Visible = false;
            if (_keyboardEditOverlayUI != null)
            {
                _keyboardEditOverlayUI.SwitchSoundSelected += HandleSwitchSoundSelected;
                _keyboardEditOverlayUI.CaseMaterialSelected += HandleCaseMaterialSelected;
                _keyboardEditOverlayUI.PlateMaterialSelected += HandlePlateMaterialSelected;
                _keyboardEditOverlayUI.PlateEffectSelected += HandlePlateEffectSelected;
                _keyboardEditOverlayUI.AttackModeSelected += HandleAttackModeSelected;
            }
            _clickInput.action.started += HandleClickStarted;
            _clickInput.action.canceled += HandleClickCanceled;
            _clickInput.action.Enable();
            _coverInput.action.started += HandleCoverStarted;
            _coverInput.action.canceled += HandleClickCanceled;
            _coverInput.action.Enable();
            _keyboardResetInput.started += HandleKeyboardResetStarted;
            _keyboardResetInput.Enable();
        }

        void OnDisable()
        {
            if (_keyboardEditOverlayUI != null)
            {
                _keyboardEditOverlayUI.SwitchSoundSelected -= HandleSwitchSoundSelected;
                _keyboardEditOverlayUI.CaseMaterialSelected -= HandleCaseMaterialSelected;
                _keyboardEditOverlayUI.PlateMaterialSelected -= HandlePlateMaterialSelected;
                _keyboardEditOverlayUI.PlateEffectSelected -= HandlePlateEffectSelected;
                _keyboardEditOverlayUI.AttackModeSelected -= HandleAttackModeSelected;
            }
            _clickInput.action.started -= HandleClickStarted;
            _clickInput.action.canceled -= HandleClickCanceled;
            _clickInput.action.Disable();
            _coverInput.action.started -= HandleCoverStarted;
            _coverInput.action.canceled -= HandleClickCanceled;
            _coverInput.action.Disable();
            _keyboardResetInput.started -= HandleKeyboardResetStarted;
            _keyboardResetInput.Disable();
            ResetMonitorFocusMode();
            ResetKeyboardEditMode();
            DropHeldKeyboard();
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
        }

        void FixedUpdate()
        {
            FollowHeldKeyboard();
            ApplyEditingKeyboardRotation();
        }

        void OnDestroy()
        {
            _keyboardResetInput.Dispose();
        }

        void LateUpdate()
        {
            UpdateUploadConfirmationInput();
            if (Game.Logging.PlaytestResearchSession.IsUploadConfirmationOpen
                || Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (_isKeyboardEditMode && _cameraTransition == null
                && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ExitKeyboardEditMode();
                return;
            }
            UpdateInteractionPrompt();
            CaptureEditingKeyboardMouseDelta();
            UpdateEditingCameraZoom();
        }

        /// <summary>
        /// 업로드 동의 창이 열리면 카메라 입력을 끄고 마우스 커서를 해제한다.
        /// 창을 닫으면 열기 전 카메라 입력과 커서 상태를 복원한다.
        /// </summary>
        private void UpdateUploadConfirmationInput()
        {
            if (Game.Logging.PlaytestResearchSession.IsUploadConfirmationOpen)
            {
                if (!_uploadConfirmationInputCaptured)
                {
                    _uploadConfirmationInputCaptured = true;
                    _uploadConfirmationCursorLockState = InGameCursor.LockState;
                    _uploadConfirmationCursorWasVisible = InGameCursor.Visible;
                    _uploadConfirmationCameraInputWasEnabled = _cameraInputAxisController != null
                        && _cameraInputAxisController.enabled;
                }

                if (_cameraInputAxisController != null)
                    _cameraInputAxisController.enabled = false;
                InGameCursor.LockState = CursorLockMode.None;
                InGameCursor.Visible = true;
                return;
            }

            if (!_uploadConfirmationInputCaptured) return;

            _uploadConfirmationInputCaptured = false;
            if (_cameraInputAxisController != null)
                _cameraInputAxisController.enabled = _uploadConfirmationCameraInputWasEnabled;
            InGameCursor.LockState = _uploadConfirmationCursorLockState;
            InGameCursor.Visible = _uploadConfirmationCursorWasVisible;
        }

        /// <summary>
        /// 좌클릭 입력이 시작되면 손에 든 키보드를 던지거나 조준한 대상을 작동시킨다.
        /// context와 포커스 상태를 사용하며 모니터 포커스 중에는 필드 상호작용을 실행하지 않는다.
        /// </summary>
        private void HandleClickStarted(InputAction.CallbackContext context)
        {
            if (Game.Logging.PlaytestResearchSession.IsUploadConfirmationOpen) return;
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (_isMonitorFocusMode || _monitorFocusTransition != null)
                return;
            if (_isKeyboardEditMode)
            {
                return;
            }

            if (_cameraTransition != null)
                return;

            TryInteract();
        }

        /// <summary>
        /// context의 우클릭 회전 입력이 끝나면 키보드 드래그 회전을 멈춘다.
        /// 좌클릭 해제는 무시하며 대기 중인 회전량과 드래그 상태를 초기화한다.
        /// </summary>
        private void HandleClickCanceled(InputAction.CallbackContext context)
        {
            if (context.action != _coverInput.action) return;
            StopEditingKeyboardRotation();
        }

        /// <summary>
        /// 휠클릭 입력을 받으면 편집 대상의 모든 키캡과 작업대 최초 회전을 복원한다.
        /// context와 저장한 회전을 사용하며 드래그를 멈추고 키보드 중심 위치를 유지한다.
        /// </summary>
        private void HandleKeyboardResetStarted(InputAction.CallbackContext context)
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (!_isKeyboardEditMode || _cameraTransition != null || _editingKeyboard == null || _editingKeyboardBody == null)
                return;

            StopEditingKeyboardRotation();
            Vector3 visualCenter = _editingKeyboardBody.transform.TransformPoint(_editingKeyboardCenterOffset);
            Vector3 centerOffset = _editingKeyboardBody.transform.TransformVector(_editingKeyboardCenterOffset);
            Quaternion rotationDelta = _initialEditingKeyboardRotation * Quaternion.Inverse(_editingKeyboardBody.rotation);
            _editingKeyboardBody.position = visualCenter - rotationDelta * centerOffset;
            _editingKeyboardBody.rotation = _initialEditingKeyboardRotation;
            _editingKeyboard.RestoreKeycaps();
        }

        /// <summary>
        /// context의 우클릭 입력으로 편집 중 키보드 회전을 시작하거나 모니터와 작업대 편집에 진입한다.
        /// 모니터 포커스 중에는 종료하고 손에 든 키보드의 커버를 조작한다.
        /// context 입력을 받으며 조준 대상과 현재 상태에 따라 진입, 종료 또는 커버 전환을 실행한다.
        /// </summary>
        private void HandleCoverStarted(InputAction.CallbackContext context)
        {
            if (Game.Tutorials.FirstEncounterTutorial.IsBlocking) return;
            if (_cameraTransition != null || _monitorFocusTransition != null)
                return;

            if (_isMonitorFocusMode)
            {
                ExitMonitorFocusMode();
                return;
            }

            if (_isKeyboardEditMode)
            {
                TryBeginEditingKeyboardRotation();
                return;
            }

            if (_heldKeyboardBody != null)
            {
                ToggleHeldKeyboardCover();
                return;
            }

            if (!TryGetInteractionHit(out RaycastHit hit))
                return;

            MonitorKeyboardHolder monitorHolder = hit.collider.GetComponentInParent<MonitorKeyboardHolder>();
            if (monitorHolder != null && monitorHolder.KeyboardBody != null)
            {
                EnterMonitorFocusMode(monitorHolder);
                return;
            }

            if (TryGetWorkbenchKeyboard(hit, out WorkbenchKeyboardHolder holder, out KeyboardInputController keyboard, out Vector3 aimPoint))
            {
                EnterKeyboardEditMode(keyboard, holder, aimPoint);
            }
        }

        /// <summary>
        /// 쓰레기통을 조준하면 손에 든 키보드를 삭제하고, 그 외에는 던지거나 조준 대상을 동작시킨다.
        /// interactionDistance, interactionLayers와 보유 키보드를 사용하며 실행 성공 여부를 반환한다.
        /// </summary>
        private bool TryInteract()
        {
            if (_heldKeyboardBody != null)
            {
                if (TryGetInteractionHit(out RaycastHit trashHit))
                {
                    KeyboardTrashCan trashCan = trashHit.collider.GetComponentInParent<KeyboardTrashCan>();
                    if (trashCan != null && trashCan.TryDispose(_heldKeyboardBody))
                    {
                        RestoreHeldKeyboardCollisions();
                        _heldKeyboardBody = null;
                        return true;
                    }
                }

                ThrowHeldKeyboard();
                return true;
            }

            if (!TryGetInteractionHit(out RaycastHit hit))
            {
                return false;
            }

            KeyboardDispenser dispenser = hit.collider.GetComponentInParent<KeyboardDispenser>();
            if (dispenser != null)
            {
                dispenser.TryDispense(out _);
                return true;
            }

            WorkbenchKeyboardHolder workbenchHolder = hit.collider.GetComponentInParent<WorkbenchKeyboardHolder>();
            if (workbenchHolder != null && workbenchHolder.KeyboardBody != null)
            {
                PickUpKeyboard(workbenchHolder.KeyboardBody);
                return true;
            }

            MonitorKeyboardHolder monitorHolder = hit.collider.GetComponentInParent<MonitorKeyboardHolder>();
            if (monitorHolder != null && monitorHolder.KeyboardBody != null)
            {
                PickUpKeyboard(monitorHolder.KeyboardBody);
                return true;
            }

            KeyboardPickupTarget pickupTarget = hit.collider.GetComponentInParent<KeyboardPickupTarget>();
            if (pickupTarget != null)
            {
                Rigidbody body = hit.collider.attachedRigidbody;
                if (body == null)
                    body = pickupTarget.GetComponentInChildren<Rigidbody>();

                if (body != null)
                {
                    PickUpKeyboard(body);
                    return true;
                }
            }

            MonoBehaviour[] behaviours = hit.collider.GetComponentsInParent<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is not IPlayerInteractable interactable)
                {
                    continue;
                }

                interactable.Interact();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 중앙 조준점이 가리키는 대상에 맞춰 상호작용 안내를 표시한다.
        /// 조준 광선과 들고 있는 키보드 상태를 사용해 안내 문구와 표시 상태를 갱신한다.
        /// </summary>
        private void UpdateInteractionPrompt()
        {
            if (_contextHintsEnabled) { _interactionPrompt.enabled = false; return; }
            string prompt = string.Empty;
            if (_isMonitorFocusMode)
            {
                prompt = "우클릭으로 모니터 포커스 나가기";
            }
            else if (_isKeyboardEditMode)
            {
                prompt = "좌클릭: 파츠 제거 / 우클릭 드래그: 회전 / Esc: 편집 종료";
            }
            else if (_heldKeyboardBody != null)
            {
                if (TryGetInteractionHit(out RaycastHit trashHit)
                    && trashHit.collider.GetComponentInParent<KeyboardTrashCan>() != null)
                    prompt = "좌클릭으로 키보드 버리기";
                else prompt = _heldKeyboardBody.GetComponentInChildren<KeyboardProtectionCover>() == null
                    ? "좌클릭 던지기 / 우클릭 커버 덮기"
                    : "좌클릭 던지기 / 우클릭 커버 벗기기";
            }
            else if (TryGetInteractionHit(out RaycastHit hit))
            {
                if (hit.collider.GetComponentInParent<KeyboardDispenser>() != null)
                    prompt = "좌클릭으로 키보드 받기";
                else if (hit.collider.GetComponentInParent<WorkbenchKeyboardHolder>() != null)
                    prompt = "좌클릭으로 키보드 들기 / 우클릭으로 편집";
                else if (hit.collider.GetComponentInParent<MonitorKeyboardHolder>()?.KeyboardBody != null)
                    prompt = "좌클릭으로 키보드 들기 / 우클릭으로 모니터 보기";
                else if (hit.collider.GetComponentInParent<KeyboardPickupTarget>() != null)
                    prompt = "좌클릭으로 키보드 들기";
            }

            _interactionPrompt.text = prompt;
            _interactionPrompt.enabled = prompt.Length > 0;
            UpdateInteractionPromptPosition();
        }

        /// <summary>enabled가 true이면 기존 위치의 안내만 숨긴다. 다른 씬은 기존 표시를 계속 사용한다.</summary>
        public void SetContextHintsEnabled(bool enabled)
        {
            _contextHintsEnabled = enabled;
            if (enabled) _interactionPrompt.enabled = false;
        }

        /// <summary>현재 입력 차단과 전환 상태로 안내 표시 가능 여부를 반환한다. 시스템 창과 팝업 중에는 false이다.</summary>
        public bool CanShowControlHints => isActiveAndEnabled && Application.isFocused && Time.timeScale > 0f
            && InGameCursor.CanProcessPointer && !Game.Tutorials.FirstEncounterTutorial.IsBlocking
            && !Game.Logging.PlaytestResearchSession.IsUploadConfirmationOpen
            && _cameraTransition == null && _monitorFocusTransition == null;

        /// <summary>state에 실제 필드 대상과 편집 입력의 조작을 추가한다. 기존 중앙 조준 레이와 회전 가능 판정을 재사용한다.</summary>
        public void CollectControlHints(WorkshopControlHintState state)
        {
            if (!CanShowControlHints) return;
            if (_isKeyboardEditMode)
            {
                state.Title = "키보드 편집";
                bool occupied = _keyboardPullers != null && _keyboardPullers.IsManipulatingPart;
                if (!occupied && (_isDraggingEditingKeyboard || CanRotateEditingKeyboard()))
                    state.Add(_isDraggingEditingKeyboard ? "우클릭 놓기" : "우클릭 드래그", _isDraggingEditingKeyboard ? "회전 종료" : "키보드 회전");
                if (!occupied && _keyboardResetInput.enabled) state.Add("휠 클릭", "키보드 초기화");
                if (InGameCursor.CanProcessVirtualPointer && !_keyboardEditOverlayUI.IsPointerOverUI(InGameCursor.Position))
                    state.Add("마우스 휠", "키보드 확대·축소");
                if (_editingKeyboard != null && _editingKeyboard.InputEnabled)
                    state.Add("키보드 입력", _editingKeyboard.AttackEnabled ? "타건·공격 미리보기" : "타건");
                state.Add("Esc", "편집 종료");
                return;
            }
            if (_isMonitorFocusMode)
            {
                state.Title = "모니터 보기";
                state.Add("좌클릭", "프로그램 실행");
                if (_coverInput.action.enabled) state.Add("우클릭", "모니터 보기 종료");
                return;
            }
            state.Title = _heldKeyboardBody != null ? "키보드 운반" : "작업실 둘러보기";
            if (_cameraInputAxisController.enabled) state.Add("마우스 이동", "둘러보기");
            if (_heldKeyboardBody != null)
            {
                bool trash = TryGetInteractionHit(out RaycastHit trashHit) && trashHit.collider.GetComponentInParent<KeyboardTrashCan>() != null;
                if (_clickInput.action.enabled) state.Add("좌클릭", trash ? "키보드 버리기" : "키보드 던지기");
                if (_coverInput.action.enabled) state.Add("우클릭", _heldKeyboardBody.GetComponentInChildren<KeyboardProtectionCover>() == null ? "커버 덮기" : "커버 벗기기");
                return;
            }
            if (!TryGetInteractionHit(out RaycastHit hit)) return;
            KeyboardDispenser dispenser = hit.collider.GetComponentInParent<KeyboardDispenser>();
            if (dispenser != null)
            {
                if (_clickInput.action.enabled && dispenser.CanDispense) state.Add("좌클릭", "키보드 받기");
                return;
            }
            WorkbenchKeyboardHolder workbench = hit.collider.GetComponentInParent<WorkbenchKeyboardHolder>();
            MonitorKeyboardHolder monitor = hit.collider.GetComponentInParent<MonitorKeyboardHolder>();
            KeyboardPickupTarget pickup = hit.collider.GetComponentInParent<KeyboardPickupTarget>();
            bool canPickup = workbench != null && workbench.KeyboardBody != null
                || monitor != null && monitor.KeyboardBody != null
                || pickup != null && (hit.collider.attachedRigidbody != null || pickup.GetComponentInChildren<Rigidbody>() != null);
            if (_clickInput.action.enabled && canPickup) state.Add("좌클릭", "키보드 집기");
            if (!_coverInput.action.enabled) return;
            if (monitor != null && monitor.KeyboardBody != null) state.Add("우클릭", "모니터 보기");
            else if (TryGetWorkbenchKeyboard(hit, out _, out _, out _)) state.Add("우클릭", "작업대 보기");
        }

        /// <summary>
        /// 필드에서는 조준점 아래에 안내를 놓고 편집 또는 모니터 포커스에서는 하단 배치를 복원한다.
        /// 현재 포커스 상태와 초기 RectTransform 값을 사용해 안내 UI의 기준점과 위치를 갱신한다.
        /// </summary>
        private void UpdateInteractionPromptPosition()
        {
            if (_isKeyboardEditMode || _isMonitorFocusMode)
            {
                _interactionPromptRectTransform.anchorMin = _interactionPromptDefaultAnchorMin;
                _interactionPromptRectTransform.anchorMax = _interactionPromptDefaultAnchorMax;
                _interactionPromptRectTransform.pivot = _interactionPromptDefaultPivot;
                _interactionPromptRectTransform.anchoredPosition = _interactionPromptDefaultAnchoredPosition;
                return;
            }

            _interactionPromptRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _interactionPromptRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _interactionPromptRectTransform.pivot = new Vector2(0.5f, 1f);
            _interactionPromptRectTransform.anchoredPosition = new Vector2(0f, -INTERACTION_PROMPT_CURSOR_OFFSET);
        }

        /// <summary>
        /// hit이 작업대에 고정된 키보드인지 확인하고 입력 컨트롤러와 조준점을 반환한다.
        /// hit의 WorkbenchKeyboardHolder와 키보드 렌더러 bounds의 중앙을 사용한다.
        /// </summary>
        private bool TryGetWorkbenchKeyboard(
            RaycastHit hit,
            out WorkbenchKeyboardHolder holder,
            out KeyboardInputController keyboard,
            out Vector3 aimPoint)
        {
            holder = hit.collider.GetComponentInParent<WorkbenchKeyboardHolder>();
            keyboard = null;
            aimPoint = Vector3.zero;
            if (holder == null || holder.KeyboardBody == null)
                return false;

            Rigidbody keyboardBody = holder.KeyboardBody;
            keyboard = keyboardBody.GetComponent<KeyboardInputController>();
            KeyboardPickupTarget pickupTarget = keyboardBody.GetComponent<KeyboardPickupTarget>();
            if (keyboard == null || pickupTarget == null)
                return false;

            Vector3 centerOffset = pickupTarget.GetVisualCenterLocalPosition(keyboardBody.transform);
            aimPoint = keyboardBody.transform.TransformPoint(centerOffset);
            return true;
        }

        /// <summary>
        /// 작업대 키보드 입력을 켜고 플레이어를 편집 위치로 이동시키며 카메라를 키보드 중심으로 돌린다.
        /// keyboard, holder, aimPoint를 사용하고 입력 상태를 저장하며 앵커가 없으면 작업대 기준 위치를 계산한다.
        /// </summary>
        private void EnterKeyboardEditMode(
            KeyboardInputController keyboard,
            WorkbenchKeyboardHolder holder,
            Vector3 aimPoint)
        {
            if (_camera == null
                || _moveInput == null
                || _moveInput.action == null
                || _cameraPanTilt == null
                || _cameraInputAxisController == null
                || _editingCinemachineCamera == null)
            {
                Debug.LogError(
                    $"키보드 편집 모드 참조 누락: Camera={_camera != null}, " +
                    $"MoveInput={_moveInput?.action != null}, PanTilt={_cameraPanTilt != null}, " +
                    $"CameraInput={_cameraInputAxisController != null}, " +
                    $"CinemachineCamera={_editingCinemachineCamera != null}.",
                    this);
                return;
            }

            _savedCameraFieldOfView = _editingCinemachineCamera.Lens.FieldOfView;
            _isKeyboardEditMode = true;
            _editingKeyboard = keyboard;
            KeyboardCustomization customization = keyboard.GetComponent<KeyboardCustomization>();
            if (AudioManager.instance != null)
                AudioManager.instance.SelectKeyboardEffects(customization);
            _keyboardEditOverlayUI?.SetSelectedMaterial(customization != null ? customization.KeycapMaterial : null);
            _keyboardEditOverlayUI?.SetSelectedSwitchSound(
                customization != null && !customization.HasAnySwitchForKeycaps() ? null : keyboard.CurrentSwitchSound);
            _keyboardEditOverlayUI?.SetCaseCustomization(customization);
            _keyboardEditOverlayUI?.SetPlateCustomization(customization);
            _keyboardEditOverlayUI?.SetPhotoCustomization(customization);
            _keyboardEditOverlayUI?.SetLEDCustomization(customization);
            if (_keyboardEditOverlay != null)
                _keyboardEditOverlay.SetActive(true);
            Game.Logging.CustomizationTelemetry.Emit(customization, "CustomizationStart");
            _keyboardEditOverlayUI?.ObserveAvailableOptions();
            if (_centerReticle != null)
                _centerReticle.SetActive(false);

            _editingKeyboardBody = holder.KeyboardBody;
            _initialEditingKeyboardRotation = holder.InitialKeyboardRotation;
            KeyboardPickupTarget pickupTarget = _editingKeyboardBody.GetComponent<KeyboardPickupTarget>();
            _editingKeyboardCenterOffset = pickupTarget.GetVisualCenterLocalPosition(_editingKeyboardBody.transform);
            _savedPlayerPosition = transform.position;
            _savedCameraRotation = _camera.transform.rotation;
            _moveInputWasEnabled = _moveInput.action.enabled;
            _cameraInputWasEnabled = _cameraInputAxisController.enabled;

            _moveInput.action.Disable();
            _cameraInputAxisController.enabled = false;
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
            _editingKeyboardAttackWasEnabled = keyboard.AttackEnabled;
            keyboard.SetAttackEnabled(false);
            _keyboardEditOverlayUI?.SetSelectedAttackMode(false);
            keyboard.SetInputEnabled(true);

            Vector3 targetPlayerPosition;
            if (_keyboardEditPosition != null)
            {
                targetPlayerPosition = _keyboardEditPosition.position;
            }
            else
            {
                targetPlayerPosition = holder.transform.position - holder.transform.forward * _keyboardEditDistance;
                targetPlayerPosition.y = transform.position.y;
            }

            Vector3 targetCameraPosition = _camera.transform.position + targetPlayerPosition - transform.position;
            Vector3 direction = aimPoint - targetCameraPosition;
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            BeginCameraTransition(targetRotation, targetPlayerPosition);
        }

        /// <summary>
        /// holder에 고정된 키보드와 모니터 화면이 함께 보이도록 플레이어와 카메라를 이동한다.
        /// holder의 화면 목표와 포커스 거리 및 시야각을 사용하며 이동·카메라 입력과 커서 상태를 전환한다.
        /// </summary>
        private void EnterMonitorFocusMode(MonitorKeyboardHolder holder)
        {
            if (_camera == null
                || _moveInput?.action == null
                || _cameraPanTilt == null
                || _cameraInputAxisController == null
                || _editingCinemachineCamera == null
                || holder.MonitorFocusTarget == null)
            {
                Debug.LogError("모니터 포커스에 필요한 카메라, 입력 또는 화면 목표 참조가 없습니다.", this);
                return;
            }

            _monitorSavedPlayerPosition = transform.position;
            _monitorSavedCameraRotation = _camera.transform.rotation;
            _monitorSavedFieldOfView = _editingCinemachineCamera.Lens.FieldOfView;
            _monitorMoveInputWasEnabled = _moveInput.action.enabled;
            _monitorCameraInputWasEnabled = _cameraInputAxisController.enabled;
            _isMonitorFocusMode = true;
            _focusedMonitorHolder = holder;

            _moveInput.action.Disable();
            _cameraInputAxisController.enabled = false;
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
            if (_centerReticle != null)
                _centerReticle.SetActive(false);

            Vector3 targetPlayerPosition = holder.transform.position
                - holder.transform.forward * _monitorFocusDistance;
            targetPlayerPosition.y = transform.position.y;
            Vector3 targetCameraPosition = _camera.transform.position
                + targetPlayerPosition - transform.position;
            Vector3 aimPoint = Vector3.Lerp(
                holder.transform.position,
                holder.MonitorFocusTarget.position,
                0.5f);
            Quaternion targetRotation = Quaternion.LookRotation(
                aimPoint - targetCameraPosition,
                Vector3.up);
            BeginMonitorFocusTransition(
                targetRotation,
                targetPlayerPosition,
                _monitorFocusFieldOfView,
                false);
        }

        /// <summary>
        /// 모니터 포커스를 해제하고 진입 전의 플레이어 위치와 카메라 시야로 돌아간다.
        /// 저장된 위치·회전·시야각을 사용하며 전환 완료 후 이동 입력과 커서를 복원한다.
        /// </summary>
        private void ExitMonitorFocusMode()
        {
            _isMonitorFocusMode = false;
            _focusedMonitorHolder = null;
            MonitorFocusChanged?.Invoke(null);
            BeginMonitorFocusTransition(
                _monitorSavedCameraRotation,
                _monitorSavedPlayerPosition,
                _monitorSavedFieldOfView,
                true);
        }

        /// <summary>
        /// 모니터 포커스 카메라 전환 코루틴을 시작한다.
        /// targetRotation, targetPlayerPosition, targetFieldOfView와 restoreInput을 전달하며 진행 중 전환을 교체한다.
        /// </summary>
        private void BeginMonitorFocusTransition(
            Quaternion targetRotation,
            Vector3 targetPlayerPosition,
            float targetFieldOfView,
            bool restoreInput)
        {
            if (_monitorFocusTransition != null)
                StopCoroutine(_monitorFocusTransition);

            _monitorFocusTransition = StartCoroutine(TransitionMonitorFocus(
                targetRotation,
                targetPlayerPosition,
                targetFieldOfView,
                restoreInput));
        }

        /// <summary>
        /// 플레이어 위치와 카메라 회전·시야각을 모니터 포커스 목표까지 부드럽게 변경한다.
        /// 목표 위치·회전·시야각과 Time.deltaTime을 사용하며 복귀 전환이 끝나면 입력을 복원한다.
        /// </summary>
        private IEnumerator TransitionMonitorFocus(
            Quaternion targetRotation,
            Vector3 targetPlayerPosition,
            float targetFieldOfView,
            bool restoreInput)
        {
            Vector3 startPlayerPosition = transform.position;
            Quaternion startRotation = _camera.transform.rotation;
            float startFieldOfView = _editingCinemachineCamera.Lens.FieldOfView;
            float elapsed = 0f;
            while (elapsed < _cameraTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _cameraTransitionDuration));
                transform.position = Vector3.Lerp(startPlayerPosition, targetPlayerPosition, progress);
                Quaternion rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
                _cameraPanTilt.ForceCameraPosition(_camera.transform.position, rotation);
                SetEditingCameraFieldOfView(Mathf.Lerp(startFieldOfView, targetFieldOfView, progress));
                yield return null;
            }

            transform.position = targetPlayerPosition;
            _cameraPanTilt.ForceCameraPosition(_camera.transform.position, targetRotation);
            SetEditingCameraFieldOfView(targetFieldOfView);
            yield return null;

            _monitorFocusTransition = null;
            if (restoreInput)
                RestoreMonitorFocusInput();
            else if (_isMonitorFocusMode)
                MonitorFocusChanged?.Invoke(_focusedMonitorHolder);
        }

        /// <summary>
        /// 모니터 포커스 전에 저장한 이동·카메라 입력과 커서 상태를 되돌린다.
        /// 저장된 입력 활성 상태를 사용하며 조준점과 커서 잠금을 필드 상태로 복원한다.
        /// </summary>
        private void RestoreMonitorFocusInput()
        {
            if (_monitorMoveInputWasEnabled)
                _moveInput.action.Enable();
            else
                _moveInput.action.Disable();

            _cameraInputAxisController.enabled = _monitorCameraInputWasEnabled;
            InGameCursor.LockState = CursorLockMode.Locked;
            InGameCursor.Visible = false;
            if (_centerReticle != null)
                _centerReticle.SetActive(true);
        }

        /// <summary>
        /// 비활성화 중 모니터 포커스 전환을 중단하고 저장한 시점과 입력을 복원한다.
        /// 저장된 플레이어 위치·카메라 회전·시야각을 사용하며 포커스 상태를 초기화한다.
        /// </summary>
        private void ResetMonitorFocusMode()
        {
            if (!_isMonitorFocusMode && _monitorFocusTransition == null)
                return;

            if (_monitorFocusTransition != null)
            {
                StopCoroutine(_monitorFocusTransition);
                _monitorFocusTransition = null;
            }

            _isMonitorFocusMode = false;
            _focusedMonitorHolder = null;
            MonitorFocusChanged?.Invoke(null);
            transform.position = _monitorSavedPlayerPosition;
            if (_camera != null && _cameraPanTilt != null)
                _cameraPanTilt.ForceCameraPosition(_camera.transform.position, _monitorSavedCameraRotation);
            if (_editingCinemachineCamera != null)
                SetEditingCameraFieldOfView(_monitorSavedFieldOfView);
            RestoreMonitorFocusInput();
        }

        /// <summary>
        /// 로드된 입력 액션 참조 중 PlayerMove를 찾아 반환한다.
        /// 씬 인스턴스에 이동 입력 참조가 없을 때 같은 액션 에셋을 재사용한다.
        /// </summary>
        private InputActionReference FindMoveInputReference()
        {
            InputActionReference[] inputReferences = Resources.FindObjectsOfTypeAll<InputActionReference>();
            foreach (InputActionReference inputReference in inputReferences)
            {
                if (inputReference.name == "PlayerMove")
                    return inputReference;
            }

            return null;
        }

        /// <summary>
        /// 편집 중인 키보드 입력을 되돌리고 플레이어와 카메라를 진입 전 상태로 복귀시킨다.
        /// 저장한 위치와 회전을 사용하며 전환 완료 후 플레이어 입력과 커서 잠금을 복원한다.
        /// </summary>
        private void ExitKeyboardEditMode()
        {
            if (_editingKeyboard != null)
                Game.Logging.CustomizationTelemetry.Emit(_editingKeyboard.GetComponent<KeyboardCustomization>(),
                    "CustomizationEnd", details: "RightClickExit");
            _isKeyboardEditMode = false;
            StopEditingKeyboardRotation();
            if (_keyboardEditOverlay != null)
                _keyboardEditOverlay.SetActive(false);
            if (_centerReticle != null)
                _centerReticle.SetActive(true);

            if (_editingKeyboard != null)
                _editingKeyboard.SetInputEnabled(false);

            BeginCameraTransition(_savedCameraRotation, _savedPlayerPosition);
        }

        /// <summary>
        /// 카메라 회전과 플레이어 위치를 목표 상태까지 보간하는 코루틴을 시작한다.
        /// targetRotation과 targetPlayerPosition을 사용하며 진행 중 전환을 새 전환으로 교체한다.
        /// </summary>
        private void BeginCameraTransition(Quaternion targetRotation, Vector3 targetPlayerPosition)
        {
            if (_cameraTransition != null)
                StopCoroutine(_cameraTransition);

            _cameraTransition = StartCoroutine(TransitionCameraRotation(targetRotation, targetPlayerPosition));
        }

        /// <summary>
        /// 현재 시야와 플레이어 위치를 목표 상태까지 보간하고 렌즈 시야각을 원래 값으로 복원한다.
        /// targetRotation, targetPlayerPosition, Time.deltaTime을 사용해 전환과 편집 줌 복원을 수행한다.
        /// </summary>
        private IEnumerator TransitionCameraRotation(Quaternion targetRotation, Vector3 targetPlayerPosition)
        {
            Vector3 startPlayerPosition = transform.position;
            Quaternion startRotation = _camera.transform.rotation;
            float startFieldOfView = _editingCinemachineCamera.Lens.FieldOfView;
            float elapsed = 0f;
            while (elapsed < _cameraTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _cameraTransitionDuration));
                Quaternion rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
                transform.position = Vector3.Lerp(startPlayerPosition, targetPlayerPosition, progress);
                _cameraPanTilt.ForceCameraPosition(_camera.transform.position, rotation);
                SetEditingCameraFieldOfView(Mathf.Lerp(startFieldOfView, _savedCameraFieldOfView, progress));
                yield return null;
            }

            transform.position = targetPlayerPosition;
            _cameraPanTilt.ForceCameraPosition(_camera.transform.position, targetRotation);
            yield return null;
            SetEditingCameraFieldOfView(_savedCameraFieldOfView);

            _cameraTransition = null;
            if (_isKeyboardEditMode)
                Game.Tutorials.FirstEncounterTutorial.Request(Game.Tutorials.TutorialTopic.EditControls);
            if (!_isKeyboardEditMode)
                RestoreKeyboardEditInput();
        }

        /// <summary>
        /// 편집 모드 진입 전에 저장한 렌즈 시야각과 키보드·이동 입력 및 커서 상태를 복원한다.
        /// 살아 있는 카메라의 설정과 키보드 입력을 저장 상태로 되돌리고 편집 중인 키보드 참조를 비운다.
        /// </summary>
        private void RestoreKeyboardEditInput()
        {
            if (_editingCinemachineCamera != null)
                SetEditingCameraFieldOfView(_savedCameraFieldOfView);

            if (_editingKeyboard != null)
            {
                if (AudioManager.instance != null)
                    AudioManager.instance.ReleaseKeyboardEffects(_editingKeyboard.GetComponent<KeyboardCustomization>());
                _editingKeyboard.SetInputEnabled(false);
                _editingKeyboard.SetAttackEnabled(_editingKeyboardAttackWasEnabled);
            }

            _editingKeyboard = null;
            _editingKeyboardBody = null;
            _editingKeyboardCenterOffset = Vector3.zero;
            if (_moveInputWasEnabled)
                _moveInput.action.Enable();
            else
                _moveInput.action.Disable();

            InGameCursor.LockState = CursorLockMode.Locked;
            InGameCursor.Visible = false;
            if (_cameraInputAxisController != null)
                _cameraInputAxisController.enabled = _cameraInputWasEnabled;
        }

        /// <summary>
        /// 비활성화 시 진행 중인 전환을 정리하고 플레이어 위치와 입력 및 카메라 상태를 복원한다.
        /// 편집 상태나 전환 코루틴을 정리하고 아직 파괴되지 않은 카메라만 저장한 방향으로 되돌린다.
        /// </summary>
        private void ResetKeyboardEditMode()
        {
            if (_editingKeyboard != null)
                Game.Logging.CustomizationTelemetry.Emit(_editingKeyboard.GetComponent<KeyboardCustomization>(),
                    "CustomizationEnd", success: false, details: "EditInterrupted");
            StopEditingKeyboardRotation();
            if (_keyboardEditOverlay != null)
                _keyboardEditOverlay.SetActive(false);
            if (_centerReticle != null)
                _centerReticle.SetActive(true);

            if (!_isKeyboardEditMode && _editingKeyboard == null && _cameraTransition == null)
                return;

            if (_cameraTransition != null)
            {
                StopCoroutine(_cameraTransition);
                _cameraTransition = null;
            }

            _isKeyboardEditMode = false;
            transform.position = _savedPlayerPosition;
            if (_camera != null && _cameraPanTilt != null)
                _cameraPanTilt.ForceCameraPosition(_camera.transform.position, _savedCameraRotation);
            RestoreKeyboardEditInput();
        }

        /// <summary>
        /// 우클릭 시작 위치가 편집 중인 키보드 위인지 확인하고 드래그 회전을 시작한다.
        /// 마우스 위치와 키보드 렌더 경계 Raycast를 확인하고 앞을 가리는 충돌체를 검사한다.
        /// </summary>
        private void TryBeginEditingKeyboardRotation()
        {
            if (!CanRotateEditingKeyboard()) return;
            _isDraggingEditingKeyboard = true;
            _pendingKeyboardRotationDelta = Vector2.zero;
        }

        /// <summary>현재 공통 커서와 UI 차단 및 키보드 경계로 회전 시작 가능 여부를 반환한다. 입력과 자세는 변경하지 않는다.</summary>
        private bool CanRotateEditingKeyboard()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !InGameCursor.CanProcessVirtualPointer || _editingKeyboardBody == null)
                return false;

            Vector2 pointerPosition = InGameCursor.Position;
            if (_keyboardEditOverlayUI != null && _keyboardEditOverlayUI.IsPointerOverUI(pointerPosition))
                return false;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                PointerEventData pointerEvent = new PointerEventData(eventSystem) { position = pointerPosition };
                eventSystem.RaycastAll(pointerEvent, _uiRaycastResults);
                for (int i = 0; i < _uiRaycastResults.Count; i++)
                {
                    if (_uiRaycastResults[i].gameObject != _keyboardEditOverlay)
                        return false;
                }
            }

            Ray pointerRay = _camera.ScreenPointToRay(pointerPosition);
            float maximumKeyboardHitDistance = _interactionDistance;
            float nearestKeyboardHitDistance = float.PositiveInfinity;
            Renderer[] keyboardRenderers = _editingKeyboardBody.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < keyboardRenderers.Length; i++)
            {
                Renderer renderer = keyboardRenderers[i];
                if (renderer.enabled
                    && renderer.bounds.IntersectRay(pointerRay, out float hitDistance)
                    && hitDistance < nearestKeyboardHitDistance)
                {
                    nearestKeyboardHitDistance = hitDistance;
                }
            }

            if (nearestKeyboardHitDistance > maximumKeyboardHitDistance
                || float.IsPositiveInfinity(nearestKeyboardHitDistance))
            {
                return false;
            }

            if (Physics.Raycast(
                    pointerRay,
                    out RaycastHit obstructionHit,
                    maximumKeyboardHitDistance,
                    _interactionLayers,
                    QueryTriggerInteraction.Ignore)
                && obstructionHit.distance < nearestKeyboardHitDistance)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 키보드 위에서 누른 우클릭이 유지되는 동안 마우스 이동량을 누적한다.
        /// Mouse.delta를 사용하며 물리 갱신에서 적용할 회전 입력을 저장한다.
        /// </summary>
        private void CaptureEditingKeyboardMouseDelta()
        {
            if (!_isKeyboardEditMode || !_isDraggingEditingKeyboard)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null || !InGameCursor.CanProcessVirtualPointer)
            {
                StopEditingKeyboardRotation();
                return;
            }

            _pendingKeyboardRotationDelta += mouse.delta.ReadValue();
        }

        /// <summary>
        /// 편집 중 UI 밖의 휠 입력으로 카메라 렌즈의 시야각을 조절한다.
        /// Mouse.scroll을 사용하며 시야각을 지정한 최소값과 최대값 사이로 제한한다.
        /// </summary>
        private void UpdateEditingCameraZoom()
        {
            if (!_isKeyboardEditMode || _cameraTransition != null || _editingKeyboardBody == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null || !InGameCursor.CanProcessVirtualPointer)
                return;

            float scrollDelta = mouse.scroll.y.ReadValue();
            if (scrollDelta == 0f
                || (_keyboardEditOverlayUI != null && _keyboardEditOverlayUI.IsPointerOverUI(InGameCursor.Position)))
            {
                return;
            }

            float currentFieldOfView = _editingCinemachineCamera.Lens.FieldOfView;
            float fieldOfView = Mathf.Clamp(
                currentFieldOfView - scrollDelta * _keyboardEditFieldOfViewStep,
                _keyboardEditMinFieldOfView,
                _keyboardEditMaxFieldOfView);
            if (!Mathf.Approximately(fieldOfView, currentFieldOfView))
                SetEditingCameraFieldOfView(fieldOfView);
        }

        /// <summary>
        /// Cinemachine 카메라 렌즈에 요청된 시야각을 적용한다.
        /// fieldOfView를 사용하며 카메라 위치를 바꾸지 않고 렌즈 설정을 갱신한다.
        /// </summary>
        private void SetEditingCameraFieldOfView(float fieldOfView)
        {
            LensSettings lens = _editingCinemachineCamera.Lens;
            lens.FieldOfView = fieldOfView;
            _editingCinemachineCamera.Lens = lens;
        }

        /// <summary>
        /// 누적된 마우스 이동량을 사용해 키보드를 시각적 중앙 기준으로 회전한다.
        /// editingKeyboardBody와 editingKeyboardCenterOffset을 사용해 Rigidbody 자세를 갱신한다.
        /// </summary>
        private void ApplyEditingKeyboardRotation()
        {
            if (!_isKeyboardEditMode || !_isDraggingEditingKeyboard || _editingKeyboardBody == null)
                return;

            Vector2 mouseDelta = _pendingKeyboardRotationDelta;
            _pendingKeyboardRotationDelta = Vector2.zero;
            if (mouseDelta.sqrMagnitude <= 0f)
                return;

            Quaternion yawRotation = Quaternion.AngleAxis(
                mouseDelta.x * KEYBOARD_EDIT_ROTATION_SENSITIVITY,
                Vector3.up);
            Quaternion pitchRotation = Quaternion.AngleAxis(
                -mouseDelta.y * KEYBOARD_EDIT_ROTATION_SENSITIVITY,
                _camera.transform.right);
            Quaternion targetRotation = pitchRotation * yawRotation * _editingKeyboardBody.rotation;

            Vector3 visualCenter = _editingKeyboardBody.transform.TransformPoint(_editingKeyboardCenterOffset);
            Vector3 currentCenterOffset = _editingKeyboardBody.transform.TransformVector(_editingKeyboardCenterOffset);
            Quaternion rotationDelta = targetRotation * Quaternion.Inverse(_editingKeyboardBody.rotation);
            _editingKeyboardBody.MovePosition(visualCenter - rotationDelta * currentCenterOffset);
            _editingKeyboardBody.MoveRotation(targetRotation);
        }

        /// <summary>
        /// 키보드 드래그 회전 상태와 대기 중인 마우스 이동량을 초기화한다.
        /// 입력 해제나 편집 모드 종료 시 회전이 더 적용되지 않도록 한다.
        /// </summary>
        private void StopEditingKeyboardRotation()
        {
            _isDraggingEditingKeyboard = false;
            _pendingKeyboardRotationDelta = Vector2.zero;
        }

        /// <summary>
        /// 선택한 type을 편집 대상 커스터마이저에 전달해 축 외형과 타건음을 함께 변경한다.
        /// 커스터마이저가 없는 대상은 타건음만 변경하며 성공한 경우 UI 선택 표시를 갱신한다.
        /// </summary>
        private void HandleSwitchSoundSelected(SwitchType type)
        {
            if (_editingKeyboard == null)
                return;

            KeyboardCustomization customization = _editingKeyboard.GetComponent<KeyboardCustomization>();
            string previous = _editingKeyboard.CurrentSwitchSound.ToString();
            bool applied = customization != null
                ? customization.SetSwitchType(type)
                : _editingKeyboard.SetSwitchSound(type);
            if (applied)
            {
                _keyboardEditOverlayUI.SetSelectedSwitchSound(_editingKeyboard.CurrentSwitchSound);
            }
            Game.Logging.CustomizationTelemetry.Emit(customization, "ElementSelected", "Switch", type.ToString(),
                previous, applied, applied && previous != type.ToString());
        }

        /// <summary>
        /// 선택한 공통 material을 편집 대상의 케이스 전체에 적용한다.
        /// 현재 키보드의 재질을 변경하고 목록의 선택 표시와 변경 기록을 갱신한다.
        /// </summary>
        private void HandleCaseMaterialSelected(Material material)
        {
            if (_editingKeyboard == null)
                return;

            KeyboardCustomization customization = _editingKeyboard.GetComponent<KeyboardCustomization>();
            if (customization != null)
            {
                Material previous = customization.CaseMaterial;
                customization.SetCaseMaterial(material);
                _keyboardEditOverlayUI.RefreshCaseMaterialSelection();
                Game.Logging.CustomizationTelemetry.Emit(customization, "ElementSelected", "Case",
                    material.name, previous != null ? previous.name : "",
                    changed: previous != customization.CaseMaterial);
            }
        }

        /// <summary>
        /// 선택한 보강판 재질을 현재 편집 키보드에 적용한다.
        /// material과 KeyboardCustomization을 사용하며 색상 카드의 선택 표시와 재질 로그를 갱신한다.
        /// </summary>
        private void HandlePlateMaterialSelected(Material material)
        {
            if (_editingKeyboard == null)
                return;

            KeyboardCustomization customization = _editingKeyboard.GetComponent<KeyboardCustomization>();
            if (customization == null) return;
            Material previous = customization.PlateMaterial;
            bool applied = customization.SetPlateMaterial(material);
            Game.Logging.CustomizationTelemetry.Emit(customization, "ElementSelected", "PlateMaterial",
                material != null ? material.name : "", previous != null ? previous.name : "", applied,
                applied && previous != customization.PlateMaterial);
            if (applied)
            {
                _keyboardEditOverlayUI.RefreshPlateSelection();
            }
        }

        /// <summary>
        /// 선택한 보강판 효과를 현재 편집 키보드에 적용한다.
        /// effect와 KeyboardCustomization을 사용하며 효과 카드의 선택 표시와 효과 로그를 갱신한다.
        /// </summary>
        private void HandlePlateEffectSelected(PlateEffect effect)
        {
            if (_editingKeyboard == null)
                return;

            KeyboardCustomization customization = _editingKeyboard.GetComponent<KeyboardCustomization>();
            if (customization == null) return;
            PlateEffect previous = customization.CurrentPlateEffect;
            bool applied = customization.SetPlateEffect(effect);
            Game.Logging.CustomizationTelemetry.Emit(customization, "ElementSelected", "PlateEffect", effect.ToString(),
                previous.ToString(), applied, applied && previous != customization.CurrentPlateEffect);
            if (applied)
            {
                _keyboardEditOverlayUI.RefreshPlateSelection();
            }
        }

        /// <summary>
        /// 편집 모드 버튼의 공격 상태를 현재 키보드에 적용하고 선택 표시를 갱신한다.
        /// attackModeSelected를 사용하며 true면 키 입력 공격을 허용하고 false면 차단한다.
        /// </summary>
        private void HandleAttackModeSelected(bool attackModeSelected)
        {
            if (!_isKeyboardEditMode || _editingKeyboard == null)
                return;

            _editingKeyboard.SetAttackEnabled(attackModeSelected);
            _keyboardEditOverlayUI.SetSelectedAttackMode(attackModeSelected);
            Game.Logging.CustomizationTelemetry.Emit(_editingKeyboard.GetComponent<KeyboardCustomization>(),
                "CustomizationTestModeChanged", element: attackModeSelected ? "AttackEnabled" : "TypingOnly");
        }

        /// <summary>
        /// 손에 든 키보드의 커버를 제거하거나 없으면 프리팹을 자식으로 생성한다.
        /// _heldKeyboardBody와 _keyboardProtectionCoverPrefab을 사용해 키보드의 커버 상태를 전환한다.
        /// </summary>
        private void ToggleHeldKeyboardCover()
        {
            if (_heldKeyboardBody == null)
                return;

            KeyboardProtectionCover cover = _heldKeyboardBody.GetComponentInChildren<KeyboardProtectionCover>();
            if (cover != null)
            {
                cover.RemoveFrom(_heldKeyboardBody.transform);
                return;
            }

            Instantiate(_keyboardProtectionCoverPrefab, _heldKeyboardBody.transform, false);
        }

        /// <summary>
        /// 화면 중앙에서 상호작용 거리 안의 콜라이더나 키보드 보관함을 찾는다.
        /// 카메라와 상호작용 거리 및 레이어를 사용하며 손에 든 키보드를 제외한 가장 가까운 유효 RaycastHit을 반환한다.
        /// </summary>
        private bool TryGetInteractionHit(out RaycastHit hit)
        {
            Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                _interactionHits,
                _interactionDistance,
                _interactionLayers,
                QueryTriggerInteraction.Collide);
            int holderHitIndex = -1;
            int solidHitIndex = -1;

            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = _interactionHits[i].collider;
                if (_heldKeyboardBody != null && candidate.transform.IsChildOf(_heldKeyboardBody.transform))
                    continue;

                if (candidate.GetComponentInParent<WorkbenchKeyboardHolder>() != null
                    || candidate.GetComponentInParent<MonitorKeyboardHolder>() != null)
                {
                    if (holderHitIndex < 0 || _interactionHits[i].distance < _interactionHits[holderHitIndex].distance)
                        holderHitIndex = i;
                }
                else if (!candidate.isTrigger
                    && (solidHitIndex < 0 || _interactionHits[i].distance < _interactionHits[solidHitIndex].distance))
                {
                    solidHitIndex = i;
                }
            }

            if (holderHitIndex >= 0
                && (solidHitIndex < 0 || _interactionHits[holderHitIndex].distance < _interactionHits[solidHitIndex].distance))
            {
                hit = _interactionHits[holderHitIndex];
                return true;
            }

            if (solidHitIndex >= 0)
            {
                hit = _interactionHits[solidHitIndex];
                return true;
            }

            hit = default;
            return false;
        }

        /// <summary>
        /// 조준한 키보드의 시각적 중앙을 손 위치에 맞춰 든 상태로 전환한다.
        /// body의 중앙 오프셋과 기존 물리 설정을 저장하고 물리 이동을 멈춘다.
        /// </summary>
        private void PickUpKeyboard(Rigidbody body)
        {
            foreach (MonoBehaviour behaviour in body.GetComponentsInParent<MonoBehaviour>())
            {
                if (behaviour is not IKeyboardHolder holder)
                    continue;

                holder.ReleaseKeyboard(body);
                body.transform.SetParent(null, true);
                break;
            }

            _heldKeyboardBody = body;
            _heldKeyboardWasKinematic = body.isKinematic;
            _heldKeyboardUsedGravity = body.useGravity;
            IgnoreHeldKeyboardCollisions(body);
            KeyboardPickupTarget pickupTarget = body.GetComponentInParent<KeyboardPickupTarget>();
            if (pickupTarget == null)
                pickupTarget = body.GetComponentInChildren<KeyboardPickupTarget>();

            Vector3 localCenter = pickupTarget != null
                ? pickupTarget.GetVisualCenterLocalPosition(body.transform)
                : Vector3.zero;
            _heldKeyboardCenterOffset = Vector3.Scale(localCenter, body.transform.lossyScale);

            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
            body.useGravity = false;
            Game.Tutorials.FirstEncounterTutorial.Request(Game.Tutorials.TutorialTopic.Workbench);
        }

        /// <summary>
        /// 손에 든 키보드의 시각적 중앙을 커서보다 약간 위의 카메라 앞 위치로 이동시킨다.
        /// heldKeyboardDistance, heldKeyboardCenterHeight와 heldKeyboardEulerAngles를 사용해 목표 자세를 갱신한다.
        /// </summary>
        private void FollowHeldKeyboard()
        {
            if (_heldKeyboardBody == null)
                return;

            Quaternion rotation = _camera.transform.rotation * Quaternion.Euler(_heldKeyboardEulerAngles);
            Vector3 centerPosition = _camera.transform.position
                + _camera.transform.forward * _heldKeyboardDistance
                + _camera.transform.up * _heldKeyboardCenterHeight;
            Vector3 position = centerPosition - rotation * _heldKeyboardCenterOffset;
            _heldKeyboardBody.MovePosition(position);
            _heldKeyboardBody.MoveRotation(rotation);
        }

        /// <summary>
        /// body의 키보드와 플레이어 사이 기존 충돌 설정을 보관하고 보유 중 충돌을 제외한다.
        /// 키보드의 모든 Collider와 플레이어 Collider를 사용하며 주변 환경과의 충돌은 유지한다.
        /// </summary>
        private void IgnoreHeldKeyboardCollisions(Rigidbody body)
        {
            RestoreHeldKeyboardCollisions();
            foreach (Collider keyboardCollider in body.GetComponentsInChildren<Collider>(true))
            {
                foreach (Collider playerCollider in _playerColliders)
                {
                    bool wasIgnored = Physics.GetIgnoreCollision(keyboardCollider, playerCollider);
                    _heldCollisionStates.Add((keyboardCollider, playerCollider, wasIgnored));
                    Physics.IgnoreCollision(keyboardCollider, playerCollider, true);
                }
            }
        }

        /// <summary>
        /// 입력 없이 보유 전의 키보드와 플레이어 충돌 설정을 복원한다.
        /// 이미 삭제된 Collider는 건너뛰고 보관한 충돌 쌍 목록을 비운다.
        /// </summary>
        private void RestoreHeldKeyboardCollisions()
        {
            foreach (var state in _heldCollisionStates)
            {
                if (state.Keyboard != null && state.Player != null)
                    Physics.IgnoreCollision(state.Keyboard, state.Player, state.WasIgnored);
            }
            _heldCollisionStates.Clear();
        }

        /// <summary>
        /// 손에 든 키보드를 놓고 저장한 물리 설정을 복원한다.
        /// heldKeyboardBody를 비우며 원래 중력과 kinematic 상태를 되돌린다.
        /// </summary>
        private void DropHeldKeyboard()
        {
            RestoreHeldKeyboardCollisions();
            if (_heldKeyboardBody == null)
                return;

            Rigidbody body = _heldKeyboardBody;
            _heldKeyboardBody = null;
            body.isKinematic = _heldKeyboardWasKinematic;
            body.useGravity = _heldKeyboardUsedGravity;

            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 손에 든 키보드를 놓고 카메라 정면 속도를 적용한다.
        /// keyboardThrowSpeed와 camera를 사용해 키보드의 Rigidbody 속도와 물리 상태를 변경한다.
        /// </summary>
        private void ThrowHeldKeyboard()
        {
            RestoreHeldKeyboardCollisions();
            if (_heldKeyboardBody == null)
                return;

            Rigidbody body = _heldKeyboardBody;
            _heldKeyboardBody = null;
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = _camera.transform.forward * _keyboardThrowSpeed;
            body.angularVelocity = Vector3.zero;
        }
    }
}
