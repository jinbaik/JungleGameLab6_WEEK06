using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Unity.Cinemachine;

using Game.Logging;
using Game.Tutorials;
using KeyboardModeling;

namespace Game.Player
{
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public sealed partial class WorkshopViewController : MonoBehaviour
    {
        [Header("Cameras")]
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera[] _views;
        [SerializeField] private CinemachineCamera _focusCamera;

        [Header("Monitor Mode")]
        [SerializeField] private Camera _monitorOutputCamera;
        [SerializeField] private CinemachineCamera _monitorCamera;
        [SerializeField] private CinemachineCamera _monitorFocusCamera;
        [SerializeField] private MonitorKeyboardHolder _monitorHolder;
        [SerializeField] private MonitorMiniGameInput _monitorInput;
        [SerializeField] private Collider _monitorScreen;
        private bool _monitorFocused;
        private bool _monitorTransition;

        [Header("Workshop Mode")]
        [SerializeField] private WorkshopKeyboardDragController _keyboardDrag;
        [SerializeField] private WorkbenchKeyboardHolder _workbenchHolder;
        [SerializeField] private WorkshopKeyboardPullerController _pullers;
        [SerializeField] private KeyboardEditOverlayUI _editOverlay;
        [SerializeField] private CanvasGroup _inventoryGroup;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private bool _isFocused;
        private bool _modeTransitionPending;
        private bool _initialized;
        private bool _keyboardInputWasEnabled;
        private bool _keyboardAttackWasEnabled;

        [Header("World Interaction")]
        [SerializeField] private KeyboardDispenser _dispenser;

        [Header("View State")]
        private int _viewIndex;
        private bool _viewSwitchingAllowed = true;

        public bool ViewSwitchingAllowed => _viewSwitchingAllowed;
        public KeyboardDispenser KeyboardDispenser => _dispenser;
        public bool IsKeyboardEditing => _isFocused && !_modeTransitionPending && !_brain.IsBlending;
        public KeyboardInputController EditingKeyboard { get; private set; }

        /// <summary>body를 ray가 가리키는 빈 작업대에 장착한 결과를 반환한다. 성공하면 기존 편집 연결을 종료하고 새 실물을 연결하며 카메라와 재고는 유지한다.</summary>
        public bool TryPlaceInventoryKeyboard(Rigidbody body, Ray ray)
        {
            if (!IsKeyboardEditing || !_keyboardDrag.TryPlaceInventoryKeyboard(body, ray)) return false;
            CloseAssembly();
            OpenAssembly();
            return true;
        }
        public bool CanShowControlHints => isActiveAndEnabled && Application.isFocused && !_brain.IsBlending
            && !(FirstEncounterTutorial.IsBlocking || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked) && !PlaytestResearchSession.IsUploadConfirmationOpen;

        public bool CanEnterKeyboardFocus => isActiveAndEnabled && !_isFocused && !_modeTransitionPending
            && !_monitorFocused && !_monitorTransition
            && _viewSwitchingAllowed && _viewIndex == 0 && !_brain.IsBlending
            && _brain.ActiveVirtualCamera == (ICinemachineCamera)_views[0]
            && !_keyboardDrag.IsDragging && Application.isFocused && InGameCursor.CanProcessPointer
            && !(FirstEncounterTutorial.IsBlocking || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked) && !PlaytestResearchSession.IsUploadConfirmationOpen;

        /// <summary>
        /// 선택한 body가 현재 Holder의 스냅 키보드이면 집중 전환을 시작한다.
        /// 작업대 뷰와 입력 및 UI 조건을 재검사하며 유효하지 않으면 기존 상태를 유지한다.
        /// </summary>
        public void TryEnterKeyboardFocus(Rigidbody body)
        {
            if (CanEnterKeyboardFocus && body != null && _workbenchHolder.KeyboardBody == body
                && !IsPointerOverUI())
                SetFocused(true);
        }

        void OnEnable()
        {
            _focusCamera.Priority = -1;
            if (_monitorFocusCamera != null) _monitorFocusCamera.Priority = -1;
            if (_monitorHolder != null) _monitorHolder.KeyboardChanged += HandleMonitorKeyboardChanged;
            _workbenchHolder.KeyboardChanged += RefreshRequestChecklist;
            KeyboardPickupTarget.RegistryChanged += RefreshRequestChecklist;
            KeyboardRequestRuntime.Changed += RefreshRequestChecklist;
            Game.Logging.CustomizationTelemetry.Observed += HandleChecklistCustomizationObserved;
            if (_requestChecklistPanel != null) RefreshRequestChecklist();
            SelectView(0);
            ApplyCursor();
            _editOverlay.SwitchSoundSelected += HandleSwitchSoundSelected;
            _editOverlay.CaseMaterialSelected += HandleCaseMaterialSelected;
            _editOverlay.PlateMaterialSelected += HandlePlateMaterialSelected;
            _editOverlay.PlateEffectSelected += HandlePlateEffectSelected;
            _editOverlay.AttackModeSelected += HandleAttackModeSelected;
        }

        void Start()
        {
            _initialized = true;
            CloseAssembly();
            InitializeRequestChecklist();
            RefreshRequestChecklist();
        }

        void Update()
        {
            if (!Application.isFocused
                || (FirstEncounterTutorial.IsBlocking || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked) || PlaytestResearchSession.IsUploadConfirmationOpen
                || !InGameCursor.CanProcessPointer)
                return;

            if (_monitorFocused || _monitorTransition)
            {
                if (_monitorFocused && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    EndMonitorFocus();
                return;
            }
            if (_isFocused)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    SetFocused(false);
                return;
            }
            if (!_viewSwitchingAllowed || _modeTransitionPending || _brain.IsBlending)
                return;

            if (!_keyboardDrag.IsDragging && Mouse.current != null
                && Mouse.current.leftButton.wasPressedThisFrame && TryClickDispenser())
                return;

            if (_monitorHolder != null && _brain.ActiveVirtualCamera == (ICinemachineCamera)_monitorCamera
                && !_keyboardDrag.IsDragging && _monitorHolder.KeyboardBody != null
                && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && IsMonitorScreenSelected())
            {
                _monitorFocused = true;
                _monitorTransition = true;
                _keyboardDrag.SetInputAllowed(false);
                _monitorFocusCamera.Priority = 2;
                ApplyCursor();
                return;
            }
            if (Keyboard.current == null)
                return;

            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.TryGetComponent(out InputField inputField) && inputField.isFocused)
                return;

            bool next = Keyboard.current.eKey.wasPressedThisFrame;
            bool previous = Keyboard.current.qKey.wasPressedThisFrame;
            if (next == previous)
                return;

            SelectView((_viewIndex + (next ? 1 : _views.Length - 1)) % _views.Length);
        }

        void LateUpdate()
        {
            UpdateRequestChecklistOpacity();
            if (_monitorTransition)
            {
                ICinemachineCamera monitorDestination = _monitorFocused ? _monitorFocusCamera : _monitorCamera;
                if (_brain.IsBlending || _brain.ActiveVirtualCamera != monitorDestination) return;
                _monitorTransition = false;
                if (_monitorFocused) _monitorInput.SetWorkshopFocus(true);
                else _keyboardDrag.SetInputAllowed(true);
            }
            if (!_modeTransitionPending || _brain.IsBlending)
                return;
            ICinemachineCamera destination = _isFocused ? _focusCamera : _views[0];
            if (_brain.ActiveVirtualCamera != destination)
                return;

            _modeTransitionPending = false;
            if (_isFocused)
                OpenAssembly();
            else
                _keyboardDrag.SetInputAllowed(true);
        }

        /// <summary>
        /// 출력 카메라의 커서 Ray가 명시된 자판기에 먼저 닿으면 기존 배출 동작을 한 번 실행한다.
        /// UI 위와 재고 미연결 상태는 무시하며 배출 입력을 처리했는지 반환한다.
        /// </summary>
        private bool TryClickDispenser()
        {
            if (_dispenser == null || !_dispenser.isActiveAndEnabled || !_dispenser.CanDispense || IsPointerOverUI())
                return false;
            Physics.SyncTransforms();
            Ray ray = _monitorOutputCamera.ScreenPointToRay(InGameCursor.Position);
            if (!Physics.Raycast(ray, out RaycastHit hit, _monitorOutputCamera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<KeyboardDispenser>() != _dispenser)
                return false;
            _dispenser.TryDispense(out _);
            return true;
        }

        /// <summary>
        /// 출력 Camera의 공통 커서 Ray로 명시된 모니터 화면을 가장 먼저 가리키는지 반환한다.
        /// 실제 UI 처리 대상과 화면 앞의 물체를 제외하며 카메라와 입력 상태는 변경하지 않는다.
        /// </summary>
        private bool IsMonitorScreenSelected()
        {
            if (IsPointerOverUI()) return false;
            Physics.SyncTransforms();
            Ray ray = _monitorOutputCamera.ScreenPointToRay(InGameCursor.Position);
            if (!_monitorScreen.Raycast(ray, out RaycastHit screen, _monitorOutputCamera.farClipPlane)) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, screen.distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.distance < screen.distance && !hit.transform.IsChildOf(_monitorScreen.transform)) return false;
            return true;
        }

        /// <summary>
        /// 모니터 입력을 즉시 종료하고 Cam_Monitor 복귀 블렌드를 시작한다.
        /// 기존 앱 종료와 타건 및 공격 상태 복원을 재사용하며 완료 전까지 본체 운반은 차단한다.
        /// </summary>
        private void EndMonitorFocus()
        {
            if (!_monitorFocused) return;
            _monitorInput.SetWorkshopFocus(false);
            _monitorFocused = false;
            _monitorTransition = true;
            _monitorFocusCamera.Priority = -1;
            ApplyCursor();
        }

        /// <summary>
        /// installed가 false이면 사용 중인 모니터를 종료한다.
        /// 키보드 제거 이벤트를 사용하며 진열 및 재고 상태는 변경하지 않는다.
        /// </summary>
        private void HandleMonitorKeyboardChanged(bool installed)
        {
            if (!installed) EndMonitorFocus();
        }

        /// <summary>
        /// 선택한 type을 편집 대상 커스터마이저에 전달해 축 외형과 타건음을 함께 변경한다.
        /// 커스터마이저가 없는 대상은 타건음만 변경하며 성공한 경우 UI 선택 표시를 갱신한다.
        /// </summary>
        private void HandleSwitchSoundSelected(SwitchType type)
        {
            if (!IsKeyboardEditing || EditingKeyboard == null)
                return;

            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            string previous = EditingKeyboard.CurrentSwitchSound.ToString();
            bool applied = customization != null
                ? customization.SetSwitchType(type)
                : EditingKeyboard.SetSwitchSound(type);
            if (applied)
            {
                _editOverlay.SetSelectedSwitchSound(EditingKeyboard.CurrentSwitchSound);
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
            if (!IsKeyboardEditing || EditingKeyboard == null)
                return;

            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (customization != null)
            {
                Material previous = customization.CaseMaterial;
                customization.SetCaseMaterial(material);
                _editOverlay.RefreshCaseMaterialSelection();
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
            if (!IsKeyboardEditing || EditingKeyboard == null)
                return;

            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (customization == null) return;
            Material previous = customization.PlateMaterial;
            bool applied = customization.SetPlateMaterial(material);
            Game.Logging.CustomizationTelemetry.Emit(customization, "ElementSelected", "PlateMaterial",
                material != null ? material.name : "", previous != null ? previous.name : "", applied,
                applied && previous != customization.PlateMaterial);
            if (applied)
            {
                _editOverlay.RefreshPlateSelection();
            }
        }

        /// <summary>
        /// 선택한 보강판 효과를 현재 편집 키보드에 적용한다.
        /// effect와 KeyboardCustomization을 사용하며 효과 카드의 선택 표시와 효과 로그를 갱신한다.
        /// </summary>
        private void HandlePlateEffectSelected(PlateEffect effect)
        {
            if (!IsKeyboardEditing || EditingKeyboard == null)
                return;

            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (customization == null) return;
            PlateEffect previous = customization.CurrentPlateEffect;
            bool applied = customization.SetPlateEffect(effect);
            Game.Logging.CustomizationTelemetry.Emit(customization, "ElementSelected", "PlateEffect", effect.ToString(),
                previous.ToString(), applied, applied && previous != customization.CurrentPlateEffect);
            if (applied)
            {
                _editOverlay.RefreshPlateSelection();
            }
        }

        /// <summary>
        /// 편집 모드 버튼의 공격 상태를 현재 키보드에 적용하고 선택 표시를 갱신한다.
        /// attackModeSelected를 사용하며 true면 키 입력 공격을 허용하고 false면 차단한다.
        /// </summary>
        private void HandleAttackModeSelected(bool attackModeSelected)
        {
            if (!IsKeyboardEditing || EditingKeyboard == null)
                return;

            EditingKeyboard.SetAttackEnabled(attackModeSelected);
            _editOverlay.SetSelectedAttackMode(attackModeSelected);
            Game.Logging.CustomizationTelemetry.Emit(EditingKeyboard.GetComponent<KeyboardCustomization>(),
                "CustomizationTestModeChanged", element: attackModeSelected ? "AttackEnabled" : "TypingOnly");
        }

        /// <summary>
        /// focused에 따라 기존 가상 카메라의 우선순위와 본체 운반 허용 상태를 전환한다.
        /// 종료 시 조립 취소와 재고 유지 정책을 실행하며 카메라와 Holder의 자세는 변경하지 않는다.
        /// </summary>
        private void SetFocused(bool focused)
        {
            CloseAssembly();
            _isFocused = focused;
            _modeTransitionPending = true;
            _keyboardDrag.SetInputAllowed(false);
            _focusCamera.Priority = focused ? 2 : -1;
            if (!focused)
                SelectView(0);
            ApplyCursor();
        }

        /// <summary>
        /// 집중 블렌드 완료 후 Holder의 실제 키보드를 기존 Workshop UI와 조립 입력의 대상으로 연결한다.
        /// 대상이 있으면 제작 로그 회차를 시작하며, 대상이 없어도 인벤토리는 표시하고 대상 의존 조작을 막는다.
        /// </summary>
        private void OpenAssembly()
        {
            Rigidbody body = _workbenchHolder.KeyboardBody;
            EditingKeyboard = body != null ? body.GetComponent<KeyboardInputController>() : null;
            KeyboardCustomization customization = EditingKeyboard != null
                ? EditingKeyboard.GetComponent<KeyboardCustomization>() : null;
            _editOverlay.SetSelectedMaterial(customization != null ? customization.KeycapMaterial : null);
            _editOverlay.SetSelectedSwitchSound(EditingKeyboard != null ? EditingKeyboard.CurrentSwitchSound : null);
            _editOverlay.SetCaseCustomization(customization);
            _editOverlay.SetPlateCustomization(customization);
            _editOverlay.SetPhotoCustomization(customization);
            _editOverlay.SetLEDCustomization(customization);
            if (EditingKeyboard != null)
            {
                CustomizationTelemetry.Emit(customization, "CustomizationStart");
                _keyboardInputWasEnabled = EditingKeyboard.InputEnabled;
                _keyboardAttackWasEnabled = EditingKeyboard.AttackEnabled;
                EditingKeyboard.SetAttackEnabled(false);
                EditingKeyboard.SetInputEnabled(true);
                _editOverlay.ObserveAvailableOptions();
            }
            _editOverlay.SetSelectedAttackMode(false);
            _editOverlay.SetWorkshopVisible(true);
            _inventoryGroup.alpha = 1f;
            _inventoryGroup.interactable = EditingKeyboard != null;
            _inventoryGroup.blocksRaycasts = true;
        }

        /// <summary>
        /// 기존 Workshop 취소 경로로 부품 드래그와 두 도구를 정리한 뒤 목록을 닫고 인벤토리를 숨긴다.
        /// 현재 대상의 제작 로그 회차를 마감하고 입력 상태를 복원하며 장착 결과와 재고는 유지한다.
        /// </summary>
        private void CloseAssembly()
        {
            _pullers.CancelDrag();
            _editOverlay.SetWorkshopVisible(false);
            if (EditingKeyboard != null)
            {
                CustomizationTelemetry.Emit(EditingKeyboard.GetComponent<KeyboardCustomization>(),
                    "CustomizationEnd", details: "FocusClosed");
                EditingKeyboard.SetInputEnabled(_keyboardInputWasEnabled);
                EditingKeyboard.SetAttackEnabled(_keyboardAttackWasEnabled);
            }
            EditingKeyboard = null;
            if (_inventoryGroup != null)
            {
                _inventoryGroup.alpha = 0f;
                _inventoryGroup.interactable = false;
                _inventoryGroup.blocksRaycasts = false;
            }
        }

        /// <summary>
        /// 공통 커서 좌표의 UI Raycast에서 클릭을 처리할 UI가 있는지 반환한다.
        /// 장식용 이미지와 조준점은 제외하고 UI 클릭을 작업 모드 진입으로 중복 사용하지 않는다.
        /// </summary>
        private bool IsPointerOverUI()
        {
            _uiHits.Clear();
            var pointer = new PointerEventData(EventSystem.current) { position = InGameCursor.Position };
            EventSystem.current.RaycastAll(pointer, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.module is GraphicRaycaster
                    && (ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject) != null
                        || ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) != null))
                    return true;
            return false;
        }

        /// <summary>
        /// 현재 일반 뷰와 작업 모드 및 운반 상태에 맞는 공통 조작 안내를 state에 추가한다.
        /// Holder에 배치된 키보드의 클릭과 드래그를 구분하여 안내한다.
        /// </summary>
        public void CollectControlHints(WorkshopControlHintState state)
        {
            if (!CanShowControlHints)
                return;
            if (_monitorFocused || _monitorTransition)
            {
                state.Title = "모니터 사용";
                state.Add("Esc", "모니터 뷰 복귀");
                return;
            }
            state.Title = _isFocused ? "작업 모드" : "일반 뷰";
            if (_isFocused)
                state.Add("Esc", "작업대 뷰 복귀");
            else
            {
                if (_viewSwitchingAllowed)
                    state.Add("Q / E", "이전 / 다음 뷰");
                if (_keyboardDrag.IsDragging)
                    state.Add("좌클릭 놓기", "배치 또는 내려놓기");
                else if (_monitorHolder != null && _viewIndex == 1 && _monitorHolder.KeyboardBody != null)
                    state.Add("화면 좌클릭", "모니터 사용");
                else if (_viewIndex == 0 && _keyboardDrag.IsPlaced)
                    state.Add("좌클릭 / 드래그", "배치된 키보드 작업 모드 / 운반");
            }
        }

        /// <summary>
        /// allowed를 사용하여 일반 뷰 전환 허용 상태를 변경한다.
        /// 후속 작업 모드에서 진입 시 false, 종료 시 true를 전달하며 카메라 자세는 변경하지 않는다.
        /// </summary>
        public void SetViewSwitchingAllowed(bool allowed)
        {
            _viewSwitchingAllowed = allowed;
        }

        /// <summary>
        /// index에 해당하는 일반 카메라의 우선순위를 높이고 나머지를 낮춘다.
        /// Inspector의 뷰 배열을 사용하며 현재 뷰 인덱스만 저장하고 Transform과 렌즈는 보존한다.
        /// </summary>
        private void SelectView(int index)
        {
            _viewIndex = index;
            for (int i = 0; i < _views.Length; i++)
                _views[i].Priority = i == index ? 1 : 0;
        }

        /// <summary>
        /// 프로젝트 공통 커서 API에 잠금 해제와 표시를 요청한다.
        /// 입력값 없이 커서 요청 상태를 변경하며 시스템 대화상자와 튜토리얼 정책은 유지한다.
        /// </summary>
        private void ApplyCursor()
        {
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                ApplyCursor();
            else
            {
                _pullers.CancelDrag();
                EndMonitorFocus();
            }
        }

        void OnDisable()
        {
            if (_monitorHolder != null) _monitorHolder.KeyboardChanged -= HandleMonitorKeyboardChanged;
            _workbenchHolder.KeyboardChanged -= RefreshRequestChecklist;
            KeyboardPickupTarget.RegistryChanged -= RefreshRequestChecklist;
            KeyboardRequestRuntime.Changed -= RefreshRequestChecklist;
            Game.Logging.CustomizationTelemetry.Observed -= HandleChecklistCustomizationObserved;
            if (_requestChecklistGroup != null) _requestChecklistGroup.alpha = 1f;
            if (_requestChecklistPanel != null) _requestChecklistPanel.gameObject.SetActive(false);
            if (_monitorFocused) _monitorInput.SetWorkshopFocus(false);
            _monitorFocused = false;
            _monitorTransition = false;
            if (_monitorFocusCamera != null) _monitorFocusCamera.Priority = -1;
            _editOverlay.SwitchSoundSelected -= HandleSwitchSoundSelected;
            _editOverlay.CaseMaterialSelected -= HandleCaseMaterialSelected;
            _editOverlay.PlateMaterialSelected -= HandlePlateMaterialSelected;
            _editOverlay.PlateEffectSelected -= HandlePlateEffectSelected;
            _editOverlay.AttackModeSelected -= HandleAttackModeSelected;
            if (!_initialized)
                return;
            CloseAssembly();
            _isFocused = false;
            _modeTransitionPending = false;
            _focusCamera.Priority = -1;
            SelectView(0);
            _keyboardDrag.SetInputAllowed(true);
            ApplyCursor();
        }
    }
}
