using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Player;

namespace Game.Workshop
{
    [RequireComponent(typeof(WorkbenchKeyboardHolder))]
    public sealed class WorkshopDeliveryTray : MonoBehaviour
    {
        private static bool _confirmationOpen;

        [Header("Delivery")]
        [SerializeField] private WorkbenchKeyboardHolder _holder;
        [SerializeField] private WorkShop2KeyboardReturnController _returnController;
        private Rigidbody _displayedKeyboard;
        private Rigidbody _confirmedKeyboard;
        private string _confirmedRequestId;
        private bool _sending;
        private string _notice;

        [Header("Carry Highlight")]
        [SerializeField] private MeshRenderer[] _outlineRenderers;
        [SerializeField] private Camera _outputCamera;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField] private Shader _depthShader;
        private WorkshopKeyboardPlacementPreview _carryOutline;
        private bool _keyboardHeld;

        [Header("Delivery Terminal")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private Text _status;
        [SerializeField] private KeyboardEditButton _send;
        private readonly List<RaycastResult> _pointerHits = new List<RaycastResult>();
        private GraphicRaycaster _terminalRaycaster;
        private WorkshopViewController _workshopView;

        [Header("Confirmation")]
        [SerializeField] private GameObject _confirmation;
        [SerializeField] private Text _confirmationBody;
        [SerializeField] private KeyboardEditButton _confirm;
        [SerializeField] private KeyboardEditButton _back;
        private readonly List<InputAction> _pausedActions = new List<InputAction>();
        private bool _modalOpen;
        private bool _closing;
        private bool _dispatchAfterClose;
        private Action<bool> _externalConfirmationResult;
        private bool _externalConfirmation;
        private int _closeFrame;
        private float _savedTimeScale;
        private CursorLockMode _savedCursorLock;
        private bool _savedCursorVisible;
        private Text _confirmLabel;
        private Text _backLabel;
        private string _confirmLabelBeforeExternal;
        private string _backLabelBeforeExternal;

        public bool IsSending => _sending;
        public static bool IsInputBlocked => _confirmationOpen;

        /// <summary>
        /// 새 게임 실행에서 배송 확인 창의 입력 차단 상태를 초기화한다.
        /// 입력과 반환값 없이 이전 실행의 정적 상태를 비운다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            _confirmationOpen = false;
        }

        void Awake()
        {
            _terminalRaycaster = _send.GetComponentInParent<GraphicRaycaster>();
            Camera eventCamera = _terminalRaycaster.eventCamera;
            _workshopView = eventCamera != null ? eventCamera.GetComponent<WorkshopViewController>() : null;
            _confirmation.SetActive(false);
            _confirmLabel = _confirm.GetComponentInChildren<Text>(true);
            _backLabel = _back.GetComponentInChildren<Text>(true);
            _carryOutline = new WorkshopKeyboardPlacementPreview(_outlineMaterial, _depthShader, _outputCamera);
            _carryOutline.PrepareSelectedRenderers(transform, _outlineRenderers);
            Refresh();
        }

        void OnEnable()
        {
            KeyboardRequestRuntime.Changed += Refresh;
            _send.Clicked += HandleSend;
            _confirm.Clicked += HandleConfirm;
            _back.Clicked += HandleBack;
            PlayerInteractionController.KeyboardCarryChanged += HandleKeyboardCarryChanged;
            _keyboardHeld = PlayerInteractionController.HasHeldKeyboard;
            Refresh();
        }

        void LateUpdate()
        {
            if (_keyboardHeld && !_sending && !_modalOpen && !_confirmationOpen && _holder.KeyboardBody == null)
                _carryOutline.Show(new Pose(transform.position, transform.rotation));
        }

        void Update()
        {
            if (InGameCursor.IsWorldAim && _workshopView != null && _workshopView.CanClickGeneralUI
                && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && _send.IsActive() && _send.IsInteractable() && CanPointAtSendButton())
                _send.OnPointerClick(new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    position = InGameCursor.GetWorldPosition(_terminalRaycaster.eventCamera)
                });
            if (_modalOpen)
            {
                Time.timeScale = 0f;
                if (_closing && Time.frameCount > _closeFrame && InputsReleased()) FinishClose();
            }
            Rigidbody keyboard = _holder.KeyboardBody;
            if (!_sending && keyboard != null && keyboard.transform.parent != _holder.transform)
            {
                _holder.ReleaseKeyboard(keyboard);
                keyboard = null;
            }
            if (_displayedKeyboard != keyboard)
            {
                _displayedKeyboard = keyboard;
                _notice = null;
                Refresh();
            }
        }

        void OnDisable()
        {
            KeyboardRequestRuntime.Changed -= Refresh;
            _send.Clicked -= HandleSend;
            _confirm.Clicked -= HandleConfirm;
            _back.Clicked -= HandleBack;
            PlayerInteractionController.KeyboardCarryChanged -= HandleKeyboardCarryChanged;
            _keyboardHeld = false;
            Action<bool> externalResult = _externalConfirmation ? _externalConfirmationResult : null;
            if (_modalOpen) RestoreInput();
            if (_confirmation != null) _confirmation.SetActive(false);
            if (_panel != null) _panel.SetActive(false);
            externalResult?.Invoke(false);
        }

        void OnDestroy()
        {
            _carryOutline.Dispose();
        }

        /// <summary>
        /// 플레이어의 키보드 운반 Action에 따라 트레이 강조 표시 상태를 갱신한다.
        /// held가 참일 때만 빈 트레이 외곽선을 표시하도록 다음 LateUpdate에 반영한다.
        /// </summary>
        private void HandleKeyboardCarryChanged(bool held)
        {
            _keyboardHeld = held;
        }

        /// <summary>
        /// 런타임 진행 의뢰와 트레이 점유로 배송 상태와 버튼을 갱신한다.
        /// 의뢰 수락은 변경하지 않으며 빈 트레이에도 배송 단말기의 안내를 표시한다.
        /// </summary>
        private void Refresh()
        {
            _panel.SetActive(true);
            KeyboardRequestState target = KeyboardRequestRuntime.ActiveRequest;
            Rigidbody keyboard = _holder.KeyboardBody;
            bool available = target != null && target.Phase == KeyboardRequestPhase.InProgress;
            bool excludedKeyboard = IsExcludedFromRequests(keyboard);
            _status.text = _sending ? "수거 중"
                : excludedKeyboard ? "시작 지급 키보드는 의뢰에 제출할 수 없습니다."
                : keyboard == null ? "키보드를 트레이에 올려 주세요."
                : target == null ? "메일에서 의뢰를 수락해 주세요."
                : !available ? "의뢰 결과를 확인해 주세요." : "배송 대기";
            if (target != null) _status.text += "\n" + GetCustomerLabel(target);
            if (!string.IsNullOrEmpty(_notice)) _status.text += "\n" + _notice;
            _send.interactable = available && !_sending && !_modalOpen && keyboard != null && !excludedKeyboard;
        }

        /// <summary>keyboard의 pickup 상태로 의뢰 제출 제외 여부를 반환한다. 트레이 소유권과 키보드 구성은 변경하지 않는다.</summary>
        private static bool IsExcludedFromRequests(Rigidbody keyboard)
        {
            return keyboard != null && keyboard.GetComponent<KeyboardPickupTarget>()?.IsExcludedFromRequests == true;
        }

        /// <summary>
        /// request의 최초 메일 고객과 의뢰 제목을 표시용 문자열로 반환한다.
        /// 원본 메일과 의뢰 데이터는 수정하지 않는다.
        /// </summary>
        private static string GetCustomerLabel(KeyboardRequestState request)
        {
            MonitorMailMessage initial = request.Definition.InitialMail;
            return initial == null ? request.Definition.DisplayName : $"{initial.Sender}\n{initial.Subject}";
        }

        /// <summary>
        /// 발송 버튼 입력으로 현재 키보드와 의뢰를 표시한 확인 창을 연다.
        /// button을 사용하며 기존 일시정지 정책처럼 게임 입력과 시간만 보관하고 UI 입력을 유지한다.
        /// </summary>
        private void HandleSend(KeyboardEditButton button)
        {
            KeyboardRequestState target = KeyboardRequestRuntime.ActiveRequest;
            if (_sending || _modalOpen || _holder.KeyboardBody == null
                || IsExcludedFromRequests(_holder.KeyboardBody) || target == null
                || target.Phase != KeyboardRequestPhase.InProgress || Time.timeScale <= 0f
                || Game.Tutorials.FirstEncounterTutorial.IsBlocking || !CanPointAtSendButton()) return;
            _confirmedKeyboard = _holder.KeyboardBody;
            _confirmedRequestId = target.Definition.RequestId;
            _confirmationBody.text = "진행 중인 의뢰로 키보드를 발송합니다.\n발송한 키보드는 돌려받을 수 없습니다.\n\n"
                + GetCustomerLabel(target);
            OpenConfirmation();
            Refresh();
        }

        /// <summary>body와 결과 콜백으로 기존 확인 팝업을 연다. 같은 입력 차단·커서·시간 복원 경로를 사용하고 버튼 결과만 호출자에게 전달한다.</summary>
        public bool TryOpenDismantleConfirmation(Action<bool> result)
        {
            if (_modalOpen || _confirmationOpen || _sending || Time.timeScale <= 0f || result == null) return false;
            _externalConfirmation = true;
            _externalConfirmationResult = result;
            _confirmLabelBeforeExternal = _confirmLabel != null ? _confirmLabel.text : null;
            _backLabelBeforeExternal = _backLabel != null ? _backLabel.text : null;
            if (_confirmLabel != null) _confirmLabel.text = "해체·반환";
            if (_backLabel != null) _backLabel.text = "뒤로";
            _confirmationBody.text = "현재 키보드를 없애고 모든 부품을 인벤토리로 반환합니다.\n"
                + "이 키보드의 커스터마이징은 사라지며, 의뢰 충족 상태가 다시 계산됩니다.";
            OpenConfirmation();
            return true;
        }

        /// <summary>확인 팝업의 시간·입력·커서 상태를 저장하고 UI 외 게임 입력을 차단한다. 활성 버튼과 팝업을 표시한다.</summary>
        private void OpenConfirmation()
        {
            _savedTimeScale = Time.timeScale;
            _savedCursorLock = InGameCursor.LockState;
            _savedCursorVisible = InGameCursor.Visible;
            _modalOpen = true;
            _confirmationOpen = true;
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                if (string.Equals(action.actionMap?.name, "UI", StringComparison.OrdinalIgnoreCase)) continue;
                _pausedActions.Add(action);
                action.Disable();
            }
            Time.timeScale = 0f;
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
            _confirm.interactable = true;
            _back.interactable = true;
            _confirmation.SetActive(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// 중앙 조준 또는 기존 자유 포인터 좌표로 UI Raycast를 수행하여 발송 버튼이 맨 앞의 클릭 대상인지 반환한다.
        /// 월드 Canvas의 이벤트 카메라와 3D 가림 판정을 재사용하며 가려진 버튼의 탐색 입력도 거부한다.
        /// </summary>
        private bool CanPointAtSendButton()
        {
            if (!InGameCursor.CanProcessPointer || EventSystem.current == null
                || !InGameCursor.IsWorldAim && !InGameCursor.CanProcessVirtualPointer) return false;
            _pointerHits.Clear();
            var pointer = new PointerEventData(EventSystem.current)
                { position = InGameCursor.GetWorldPosition(_terminalRaycaster.eventCamera) };
            EventSystem.current.RaycastAll(pointer, _pointerHits);
            return _pointerHits.Count > 0
                && ExecuteEvents.GetEventHandler<IPointerClickHandler>(_pointerHits[0].gameObject) == _send.gameObject;
        }

        /// <summary>
        /// button의 확인 클릭으로 표시했던 의뢰와 키보드의 발송 가능 여부를 다시 검사한다.
        /// 유효할 때만 입력 해제 후 발송을 예약하며 팝업 열기 자체로는 제출하지 않는다.
        /// </summary>
        private void HandleConfirm(KeyboardEditButton button)
        {
            if (!_modalOpen || _closing) return;
            if (_externalConfirmation) { BeginClose(true); return; }
            bool valid = IsConfirmationValid();
            if (!valid) _notice = "키보드 또는 진행 의뢰가 바뀌었습니다. 다시 확인해 주세요.";
            BeginClose(valid);
        }

        /// <summary>
        /// button의 뒤로 클릭으로 소비 없이 팝업 종료를 예약한다.
        /// 키보드와 진행 의뢰 상태는 그대로 유지한다.
        /// </summary>
        private void HandleBack(KeyboardEditButton button)
        {
            if (_modalOpen && !_closing) BeginClose(false);
        }

        /// <summary>
        /// 팝업에 표시한 키보드와 의뢰가 현재도 트레이와 진행 의뢰로 유지되는지 반환한다.
        /// 검수나 보상은 수행하지 않는다.
        /// </summary>
        private bool IsConfirmationValid()
        {
            KeyboardRequestState target = KeyboardRequestRuntime.ActiveRequest;
            return !_sending && _confirmedKeyboard != null && _holder.KeyboardBody == _confirmedKeyboard
                && !IsExcludedFromRequests(_confirmedKeyboard)
                && _confirmedKeyboard.transform.parent == _holder.transform && target != null
                && target.Definition.RequestId == _confirmedRequestId && target.Phase == KeyboardRequestPhase.InProgress;
        }

        /// <summary>
        /// dispatch를 보관하고 확인 버튼을 비활성화하여 클릭의 게임 입력 전파를 막는다.
        /// 시간과 입력은 버튼에서 손을 뗀 뒤 복원한다.
        /// </summary>
        private void BeginClose(bool dispatch)
        {
            _dispatchAfterClose = dispatch;
            _closing = true;
            _closeFrame = Time.frameCount;
            _confirm.interactable = false;
            _back.interactable = false;
        }

        /// <summary>
        /// 눌린 키와 마우스 버튼이 모두 해제되었는지 반환한다.
        /// 입력 상태만 읽으며 ESC를 발송 확인으로 사용하지 않는다.
        /// </summary>
        private static bool InputsReleased()
        {
            if (Keyboard.current != null)
                foreach (var key in Keyboard.current.allKeys) if (key.isPressed) return false;
            Mouse mouse = Mouse.current;
            return mouse == null || (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed);
        }

        /// <summary>
        /// 팝업의 시간과 입력을 복원한 뒤 확정된 키보드와 의뢰를 기존 연출에 전달한다.
        /// 요청이 거부되면 실물을 유지하고 배송 상태를 다시 표시한다.
        /// </summary>
        private void FinishClose()
        {
            bool external = _externalConfirmation;
            Action<bool> externalResult = _externalConfirmationResult;
            bool externalConfirmed = _dispatchAfterClose;
            bool dispatch = _dispatchAfterClose && IsConfirmationValid();
            _confirmation.SetActive(false);
            RestoreInput();
            if (external)
            {
                externalResult?.Invoke(externalConfirmed);
                Refresh();
                return;
            }
            if (dispatch)
            {
                _sending = true;
                _holder.enabled = false;
                if (!_returnController.RequestDelivery(_confirmedKeyboard, _confirmedRequestId, HandleCollected))
                    HandleCollected(false);
            }
            Refresh();
        }

        /// <summary>
        /// 보관한 시간과 게임 입력 및 가상 커서 상태를 복원한다.
        /// 모달 소유권을 해제하여 수거 코루틴이 기존 Time.deltaTime으로 진행되게 한다.
        /// </summary>
        private void RestoreInput()
        {
            Time.timeScale = _savedTimeScale;
            foreach (InputAction action in _pausedActions) action.Enable();
            _pausedActions.Clear();
            InGameCursor.LockState = _savedCursorLock;
            InGameCursor.Visible = _savedCursorVisible;
            _modalOpen = false;
            _confirmationOpen = false;
            _closing = false;
            _dispatchAfterClose = false;
            if (_externalConfirmation)
            {
                if (_confirmLabel != null) _confirmLabel.text = _confirmLabelBeforeExternal;
                if (_backLabel != null) _backLabel.text = _backLabelBeforeExternal;
                _confirmLabelBeforeExternal = null;
                _backLabelBeforeExternal = null;
                _externalConfirmation = false;
                _externalConfirmationResult = null;
            }
        }

        /// <summary>
        /// collected 결과로 트레이 점유를 해제하거나 보존한 키보드를 다시 조작 가능하게 한다.
        /// 검수 결과와 보상 및 결과 메일은 기존 런타임이 소유한다.
        /// </summary>
        private void HandleCollected(bool collected)
        {
            if (this == null) return;
            if (collected) _holder.ReleaseKeyboard(_confirmedKeyboard);
            _holder.enabled = true;
            _sending = false;
            _notice = collected ? null : "발송하지 못했습니다. 키보드와 의뢰를 확인해 주세요.";
            _confirmedKeyboard = null;
            _confirmedRequestId = null;
            Refresh();
        }
    }
}
