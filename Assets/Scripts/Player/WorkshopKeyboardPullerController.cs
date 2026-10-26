using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

using Game.Logging;
using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    [DefaultExecutionOrder(210)]
    public sealed class WorkshopKeyboardPullerController : MonoBehaviour, IKeyboardPullerContext
    {
        [Header("Workshop")]
        [SerializeField] private PlayerInteractionController _interaction;
        [SerializeField] private WorkshopViewController _workshopView;
        [SerializeField] private Camera _camera;
        [SerializeField] private AssemblyFullerTool _keycapPuller;
        [SerializeField] private AssemblyFullerTool _switchPuller;
        [SerializeField] private WorkshopAssemblyGuidePaper _guidePaper;
        [SerializeField] private WorkshopHammerTool _hammerPrefab;
        [SerializeField] private WorkshopKeycapPlacement _keycapPlacement;
        private WorkshopHammerTool _hammer;
        private bool _hammerStrikePending;

        [Header("Hover Outline")]
        [SerializeField] private bool _showToolOutlines = true;
        [SerializeField] private Material _hoverOutlineMaterial;
        [SerializeField] private Shader _hoverDepthShader;
        private WorkshopKeyboardPlacementPreview _keycapHoverPreview;
        private WorkshopKeyboardPlacementPreview _switchHoverPreview;

        [Header("Removal")]
        [SerializeField, Min(0f)] private float _heightOffset = 0.2f;
        [SerializeField, Min(0.01f)] private float _returnDelay = 2f;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private readonly List<GameObject> _fragments = new List<GameObject>();
        private KeyboardInputController _editingKeyboard;
        private float _dragWorldHeight;
        private bool _pcbDragging;
        private bool _screwPointerCaptured;
        private bool _drawerTransitionLocked;
        private WorkshopKeycapDrawerTransition _toolTray;
        private WorkshopPullerTrayItem[] _toolItems;
        private readonly HashSet<AssemblyFullerTool> _deployedTools = new HashSet<AssemblyFullerTool>();

        public bool IsEditing => _workshopView != null ? _workshopView.IsKeyboardEditing
            : _interaction != null && _interaction.IsKeyboardEditing;
        public KeyboardInputController EditingKeyboard => _workshopView != null ? _workshopView.EditingKeyboard
            : _interaction != null ? _interaction.EditingKeyboard : null;

        public bool EditingPointerAllowed => IsEditing && !_drawerTransitionLocked
            && !(Game.Tutorials.FirstEncounterTutorial.IsBlocking || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked)
            && !PlaytestResearchSession.IsUploadConfirmationOpen
            && InGameCursor.CanProcessVirtualPointer;
        public bool InputAllowed => EditingPointerAllowed && EditingKeyboard != null;
        public bool IsManipulatingPart => _keycapPuller.IsSelected || _switchPuller.IsSelected
            || (_guidePaper != null && (_guidePaper.IsHeld || _guidePaper.IsReading))
            || (_hammer != null && _hammer.IsHeld) || _hammerStrikePending
            || _pcbDragging || _screwPointerCaptured || (_keycapPlacement != null && (_keycapPlacement.IsDragging || _keycapPlacement.IsInventoryScrolling));
        public bool IsPcbDragging => _pcbDragging;
        public bool IsScrewPointerCaptured => _screwPointerCaptured;
        public bool HasGuidePaper => _guidePaper != null;
        public bool HasHammer => _hammerPrefab != null;
        public Sprite HammerIcon => _hammerPrefab != null ? _hammerPrefab.Icon : null;
        public bool IsGuideReading => _guidePaper != null && _guidePaper.IsReading;
        public KeyboardDispenser KeyboardDispenser => _workshopView.KeyboardDispenser;
        public bool CanCreateLowerHousing => _workshopView.CanCreateLowerHousing;
        public GameObject LowerHousingPreviewPrefab => _workshopView.LowerHousingPreviewPrefab;
        public bool CanIssueLowerHousing => _workshopView != null && _workshopView.IsWorkbenchEmpty && IsEditing;

        /// <summary>현재 실제로 집은 도구의 Transform을 반환한다. 읽기·재입력 대기·망치 타격은 제외하고 풀러는 기존 편집 대상과 출고 목록 조건을 유지한다.</summary>
        public bool TryGetHeldReturnTool(out Transform tool)
        {
            tool = null;
            if (!isActiveAndEnabled || !EditingPointerAllowed || _hammerStrikePending) return false;
            if (_guidePaper != null && _guidePaper.isActiveAndEnabled && _guidePaper.IsReturnHoldActive) tool = _guidePaper.transform;
            else if (_hammer != null && _hammer.isActiveAndEnabled && _hammer.IsHeld && !_hammer.IsStored) tool = _hammer.transform;
            else if (InputAllowed)
            {
                if (_keycapPuller.IsSelected && _deployedTools.Contains(_keycapPuller)) tool = _keycapPuller.ToolTransform;
                else if (_switchPuller.IsSelected && _deployedTools.Contains(_switchPuller)) tool = _switchPuller.ToolTransform;
            }
            return tool != null && tool.gameObject.activeInHierarchy;
        }
        public Camera InputCamera => _camera;
        public float DragWorldHeight => _dragWorldHeight;

        /// <summary>작업 뷰의 작업대 기준 하부 하우징 생성 경로를 호출해 keyboardRoot와 성공 여부를 반환한다. 재고 예약과 설치는 호출자가 관리한다.</summary>
        public bool TryCreateLowerHousing(out GameObject keyboardRoot)
        {
            return _workshopView.TryCreateLowerHousing(out keyboardRoot);
        }

        /// <summary>extractsSwitches에 해당하는 씬의 실제 풀러 모델을 카탈로그 이미지로 렌더링해 반환한다. 원본 자세와 활성 상태는 유지하며 호출자가 이미지 수명을 관리한다.</summary>
        public Texture2D CreateToolPreview(bool extractsSwitches)
        {
            AssemblyFullerTool tool = extractsSwitches ? _switchPuller : _keycapPuller;
            return AssemblyCatalogPreview.RenderParts(new[] { tool.ToolTransform });
        }

        /// <summary>조립 안내 종이의 실물 메시로 도구함 카드 이미지를 만든다. 종이의 보관 및 집기 상태는 유지하며 미리보기 Texture2D를 반환한다.</summary>
        public Texture2D CreateGuidePreview()
        {
            return AssemblyCatalogPreview.RenderParts(new[] { _guidePaper.transform });
        }

        /// <summary>
        /// 일반 시점의 우클릭으로 종이를 읽거나 이전 자세로 되돌린다.
        /// 일반 중앙 조준 또는 집중 자유 포인터 좌표와 UI 가림 상태를 사용하며 종이 입력을 처리했는지 반환한다.
        /// </summary>
        public bool TryToggleGuideReading()
        {
            if (_guidePaper == null || Mouse.current == null || !Application.isFocused
                || !InGameCursor.CanProcessPointer) return false;
            Vector2 pointer = InGameCursor.GetWorldPosition(_camera);
            return _guidePaper.TryToggleReading(pointer, IsPointerOverUI(pointer));
        }

        /// <summary>body를 ray가 가리키는 작업대에 배치하고 편집 대상으로 연결한 결과를 반환한다. 기존 작업대 판정과 설치 경로를 사용하며 재고는 변경하지 않는다.</summary>
        public bool TryPlaceLowerHousing(Rigidbody body, Ray ray)
        {
            return _workshopView.TryPlaceInventoryKeyboard(body, ray);
        }

        /// <summary>keyboard의 하단부 보관 완료를 기존 작업 뷰에 전달한다. 해당 실물이 편집 대상이었다면 빈 작업대 참조로 갱신하며 집기와 재고는 변경하지 않는다.</summary>
        public void RefreshStoredKeyboard(KeyboardInputController keyboard)
        {
            _workshopView.RefreshStoredKeyboard(keyboard);
        }

        /// <summary>tray와 도구 카드를 풀러·종이·망치 출고에 연결한다. C# 누름 이벤트를 구독하며 반환값은 없다.</summary>
        public void ConnectToolTray(WorkshopKeycapDrawerTransition tray, WorkshopPullerTrayItem[] items)
        {
            SetToolTraySubscriptions(false);
            _toolTray = tray;
            _toolItems = items;
            if (_guidePaper != null) _guidePaper.Initialize(_camera, tray);
            if (isActiveAndEnabled) SetToolTraySubscriptions(true);
            RefreshToolVisibility();
        }

        /// <summary>subscribe에 따라 트레이 카드의 누름 이벤트를 연결 또는 해제한다. 도구와 이미 예약된 회수는 변경하지 않는다.</summary>
        private void SetToolTraySubscriptions(bool subscribe)
        {
            if (_toolItems == null) return;
            foreach (WorkshopPullerTrayItem item in _toolItems)
            {
                if (subscribe) item.Pressed += HandleToolTrayPressed;
                else item.Pressed -= HandleToolTrayPressed;
            }
        }

        /// <summary>item에 대응하는 풀러·종이·망치를 도구함에서 꺼낸다. 열린 트레이와 공통 입력 점유를 확인하며 도구 상태를 변경한다.</summary>
        private void HandleToolTrayPressed(WorkshopPullerTrayItem item)
        {
            if (!EditingPointerAllowed || IsManipulatingPart || !_toolTray.CanInteractWithTray
                || !_toolTray.IsCategoryOpen(WorkshopDrawerCategory.ScrewsTools)
                || Mouse.current == null || !Mouse.current.leftButton.isPressed) return;
            if (item.IsGuidePaper)
            {
                _guidePaper.BeginFromTray(_camera, _camera.ScreenPointToRay(InGameCursor.Position));
                return;
            }
            if (item.IsHammer)
            {
                _hammer.BeginTrayHold(_camera.ScreenPointToRay(InGameCursor.Position));
                return;
            }
            if (!InputAllowed) return;
            AssemblyFullerTool tool = item.ExtractsSwitches ? _switchPuller : _keycapPuller;
            if (_deployedTools.Contains(tool)) return;
            tool.ToolTransform.gameObject.SetActive(true);
            tool.SetContext(this, true, false, true);
            if (tool.BeginTrayHold(_camera.ScreenPointToRay(InGameCursor.Position))) _deployedTools.Add(tool);
            RefreshToolVisibility();
        }

        /// <summary>트레이에서 꺼낸 풀러와 선택 중인 풀러를 표시한다. 배치된 실물은 원래 도구 위치에 남기며 서랍에 보관된 풀러만 숨긴다.</summary>
        private void RefreshToolVisibility()
        {
            if (_toolTray == null) return;
            _keycapPuller.ToolTransform.gameObject.SetActive(_keycapPuller.IsSelected || _deployedTools.Contains(_keycapPuller));
            _switchPuller.ToolTransform.gameObject.SetActive(_switchPuller.IsSelected || _deployedTools.Contains(_switchPuller));
        }

        /// <summary>locked로 서랍 전환 중 공통 부품·도구 입력을 차단한다. 재고와 장착 상태는 변경하지 않는다.</summary>
        public void SetDrawerTransitionLocked(bool locked)
        {
            _drawerTransitionLocked = locked;
        }

        /// <summary>dragging으로 PCB·보강판·상단부가 공통 포인터를 사용하는지 기록한다. 다른 부품 드래그와 풀러 입력을 막으며 조립 상태는 변경하지 않는다.</summary>
        public void SetPcbDragging(bool dragging)
        {
            _pcbDragging = dragging;
        }

        /// <summary>captured로 나사 클릭의 포인터 점유를 기록한다. 같은 클릭이 풀러나 뒤쪽 부품 조작으로 전달되지 않게 한다.</summary>
        public void SetScrewPointerCaptured(bool captured)
        {
            _screwPointerCaptured = captured;
        }

        /// <summary>
        /// collider가 W08의 풀러·망치 또는 해당 복귀 표시에 속하는지 반환한다.
        /// 도구와 입력 상태는 변경하지 않는다.
        /// </summary>
        public bool IsToolCollider(Collider collider)
        {
            return _keycapPuller.OwnsCollider(collider) || _switchPuller.OwnsCollider(collider)
                || (_hammer != null && _hammer.OwnsCollider(collider));
        }

        void Awake()
        {
            if (_guidePaper != null) _guidePaper.Initialize(_camera, null);
            if (_hammerPrefab != null)
            {
                _hammer = Instantiate(_hammerPrefab);
                _hammer.Initialize(_camera);
            }
            _keycapPuller.SetContext(this, true, _showToolOutlines);
            _switchPuller.SetContext(this, true, _showToolOutlines);
            if (_showToolOutlines && _hoverOutlineMaterial != null && _hoverDepthShader != null)
            {
                _keycapHoverPreview = new WorkshopKeyboardPlacementPreview(
                    _hoverOutlineMaterial, _hoverDepthShader, _camera, Color.white);
                _switchHoverPreview = new WorkshopKeyboardPlacementPreview(
                    _hoverOutlineMaterial, _hoverDepthShader, _camera, Color.white);
                _keycapHoverPreview.Prepare(_keycapPuller.ToolTransform);
                _switchHoverPreview.Prepare(_switchPuller.ToolTransform);
            }
        }

        void LateUpdate()
        {
            RefreshToolVisibility();
            if (_keycapHoverPreview == null || !Application.isFocused || !InputAllowed
                || IsManipulatingPart || IsPointerOverUI(InGameCursor.Position))
                return;

            Physics.SyncTransforms();
            Ray ray = _camera.ScreenPointToRay(InGameCursor.Position);
            if (!Physics.Raycast(ray, out RaycastHit hit, _camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;
            if (_keycapPuller.isActiveAndEnabled && _keycapPuller.ToolTransform.gameObject.activeInHierarchy
                && hit.transform.IsChildOf(_keycapPuller.ToolTransform))
                _keycapHoverPreview.Show(new Pose(_keycapPuller.ToolTransform.position, _keycapPuller.ToolTransform.rotation));
            else if (_switchPuller.isActiveAndEnabled && _switchPuller.ToolTransform.gameObject.activeInHierarchy
                && hit.transform.IsChildOf(_switchPuller.ToolTransform))
                _switchHoverPreview.Show(new Pose(_switchPuller.ToolTransform.position, _switchPuller.ToolTransform.rotation));
        }

        void OnEnable()
        {
            InGameCursor.PointerCanceled += CancelDrag;
            SetToolTraySubscriptions(true);
            _keycapPuller.HeldToolChanged += HandleHeldToolChanged;
            _switchPuller.HeldToolChanged += HandleHeldToolChanged;
        }

        /// <summary>
        /// tool의 집기와 복귀 알림으로 기존 파츠 렌더러의 풀러 표시를 갱신한다.
        /// null이면 원래 레이어로 복원하며 실제 도구 위치와 제거 동작은 변경하지 않는다.
        /// </summary>
        private void HandleHeldToolChanged(Transform tool)
        {
            _keycapPlacement.ShowToolPreview(tool);
        }

        void Update()
        {
            KeyboardInputController target = IsEditing ? EditingKeyboard : null;
            if (target != _editingKeyboard)
            {
                CancelDrag();
                _editingKeyboard = target;
                if (target != null) target.GetComponent<KeyboardCustomization>().PrepareSwitchRemovalTargets();
                UpdateDragHeight();
            }
            if (!EditingPointerAllowed || Mouse.current == null)
            {
                if (!IsGuideReading) CancelDrag();
                return;
            }
            Vector2 pointer = InGameCursor.Position;
            if (_guidePaper != null && (_guidePaper.IsHeld || _guidePaper.IsReading || !IsManipulatingPart)
                && _guidePaper.ProcessPointer(Mouse.current, pointer, IsPointerOverUI(pointer))) return;
            if (_hammer != null && (_hammer.IsHeld || !IsManipulatingPart)
                && _hammer.ProcessPointer(Mouse.current, _camera.ScreenPointToRay(pointer), pointer,
                    IsPointerOverUI(pointer), _toolTray, TryStrikeWithHammer)) return;
            if (_hammerStrikePending) return;
            if (EditingKeyboard == null) return;
            if (_pcbDragging || _screwPointerCaptured || _keycapPlacement != null && (_keycapPlacement.IsDragging || _keycapPlacement.IsInventoryScrolling)) return;
            Ray ray = _camera.ScreenPointToRay(pointer);
            if (ProcessDeployedTool(_keycapPuller, ray, pointer)) return;
            if (ProcessDeployedTool(_switchPuller, ray, pointer)) return;
            if (Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUI(pointer))
                TryRemoveClickedPart(ray);
        }

        /// <summary>tool의 출고 상태로 월드 집기와 즉시 사용을 연결한다. 해제 입력은 수납장 반납을 먼저 검사하고 기존 pointer의 도구함 반환·표면 배치를 유지한다.</summary>
        private bool ProcessDeployedTool(AssemblyFullerTool tool, Ray ray, Vector2 pointer)
        {
            if (_toolTray != null && !tool.IsSelected && !_deployedTools.Contains(tool)) return false;
            if (!tool.IsSelected) tool.SetContext(this, true, false, true);
            if (tool.IsSelected && _toolTray != null && _toolTray.CanReturnHeldPart(tool.ToolTransform))
            {
                Transform returned = tool.ToolTransform;
                tool.ReturnToOrigin();
                _deployedTools.Remove(tool);
                RefreshToolVisibility();
                _toolTray.CompletePartReturn(returned);
                return true;
            }
            bool store = tool.IsSelected && Mouse.current.leftButton.wasReleasedThisFrame && _toolTray != null
                && _toolTray.IsCategoryOpen(WorkshopDrawerCategory.ScrewsTools) && _toolTray.ContainsReturnPoint(pointer);
            bool handled = tool.ProcessPointer(ray, pointer, Mouse.current);
            if (store && !tool.IsSelected) _deployedTools.Remove(tool);
            return handled;
        }

        /// <summary>ray가 가리킨 작업대 키보드의 재고 반환을 예약하고 기존 망치 타격·키캡 파동·본체 파괴를 시작한다. 승인 여부를 반환한다.</summary>
        private bool TryStrikeWithHammer(Ray ray)
        {
            if (!InputAllowed || _hammerStrikePending) return false;
            KeyboardInputController keyboard = EditingKeyboard;
            Rigidbody body = keyboard.GetComponent<Rigidbody>();
            WorkbenchKeyboardHolder holder = keyboard.GetComponentInParent<WorkbenchKeyboardHolder>();
            if (body == null || holder == null || holder.KeyboardBody != body) return false;
            RaycastHit targetHit = default;
            float targetDistance = float.PositiveInfinity;
            float obstructionDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, _camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(keyboard.transform))
                {
                    if (hit.distance < targetDistance) { targetHit = hit; targetDistance = hit.distance; }
                }
                else obstructionDistance = Mathf.Min(obstructionDistance, hit.distance);
            }
            if (targetDistance >= obstructionDistance) return false;

            KeyboardDestruction destruction = keyboard.GetComponent<KeyboardDestruction>();
            KeyboardFeedbackManager feedback = keyboard.GetComponent<KeyboardFeedbackManager>();
            KeyboardCustomization customization = keyboard.GetComponent<KeyboardCustomization>();
            if (destruction == null || feedback == null || customization == null
                || !_keycapPlacement.TryPrepareKeyboardInventoryReturn(customization, out Action completeReturn)
                || !destruction.BeginWorkshopHammerBreak()) return false;

            var keycaps = new List<KeycapHealth>();
            var positions = new List<Vector2>();
            int targetIndex = -1;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < customization.KeycapCount; index++)
            {
                if (!customization.IsPcbKeycapInstalled(index)) continue;
                KeycapHealth health = customization.GetKeycapSlotTransform(index).GetComponent<KeycapHealth>();
                if (health == null) continue;
                float distance = (health.transform.position - targetHit.point).sqrMagnitude;
                if (distance < nearest) { nearest = distance; targetIndex = keycaps.Count; }
                keycaps.Add(health);
                Vector3 local = keyboard.transform.InverseTransformPoint(health.transform.position);
                positions.Add(new Vector2(local.x, local.z));
            }

            _hammerStrikePending = true;
            Action onFinished = null;
            onFinished = () =>
            {
                destruction.Finished -= onFinished;
                completeReturn();
                _workshopView.RemoveDestroyedWorkbenchKeyboard(body);
                _hammerStrikePending = false;
            };
            destruction.Finished += onFinished;
            KeycapHealth[] installedKeycaps = keycaps.ToArray();
            Vector2[] installedPositions = positions.ToArray();
            feedback.PlayHammerStrike(targetIndex >= 0 ? installedKeycaps[targetIndex].transform : keyboard.transform, () =>
            {
                if (destruction == null) return;
                if (targetIndex < 0) { destruction.CompleteWorkshopHammerBreak(); return; }
                feedback.PlayHammerShockwave(keyboard, installedKeycaps, installedPositions, targetIndex,
                    health =>
                    {
                        if (health == null || health.CurrentHP <= 0) return;
                        health.ClearStatuses();
                        health.TakeDamage(health.CurrentHP);
                    },
                    requireInput: false, onFinished: destruction.CompleteWorkshopHammerBreak);
            });
            return true;
        }

        /// <summary>
        /// ray에 닿는 현재 키보드의 제거 가능한 파츠 중 가장 가까운 hit와 종류를 반환한다.
        /// 본체의 넓은 집기 충돌체는 건너뛰며 키캡이 덮고 있는 축과 외부 물체 뒤의 파츠는 제외한다.
        /// </summary>
        private bool TryGetPartHit(Ray ray, out RaycastHit hit, out bool extractSwitches)
        {
            extractSwitches = false;
            hit = default;
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            float nearest = float.PositiveInfinity;
            float obstruction = float.PositiveInfinity;
            foreach (RaycastHit candidate in Physics.RaycastAll(ray, _camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Transform hitTransform = candidate.collider.transform;
                if (!hitTransform.IsChildOf(customization.transform))
                {
                    obstruction = Mathf.Min(obstruction, candidate.distance);
                    continue;
                }
                if (candidate.distance < nearest && customization.TryGetRemovablePart(hitTransform, out _, out bool isSwitch))
                {
                    hit = candidate;
                    nearest = candidate.distance;
                    extractSwitches = isSwitch;
                }
            }
            return nearest < obstruction;
        }

        /// <summary>
        /// ray가 가리키는 설치 파츠 한 개를 집어 인벤토리 반환 또는 원본 복귀 드래그를 시작한다.
        /// 재고 항목을 연결할 수 없으면 원본 설치 상태를 복원한다. 반환값은 없다.
        /// </summary>
        private void TryRemoveClickedPart(Ray ray)
        {
            if (!TryGetPartHit(ray, out RaycastHit hit, out bool extractSwitches)) return;
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (!customization.TryRemovePart(hit.collider.transform, extractSwitches, out Transform removed)) return;
            if (!extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedKeycap(removed, false, Vector3.zero, Vector3.zero))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return;
            }
            if (extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedSwitch(removed, false, Vector3.zero, Vector3.zero))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return;
            }
            removed.gameObject.SetActive(true);
            if (extractSwitches)
                EditingKeyboard.SetSwitchSound(customization.GetInstalledSwitchType(removed.GetComponent<KeyboardSwitch>()));
        }

        /// <summary>
        /// 현재 편집 키보드의 메시 범위와 높이 설정으로 카메라 아래의 풀러 이동 평면 높이를 갱신한다.
        /// 입력값과 반환값은 없으며 작업대에 배치한 풀러의 위치와 크기는 유지한다.
        /// </summary>
        private void UpdateDragHeight()
        {
            if (_editingKeyboard == null) return;
            Bounds bounds = new Bounds(_editingKeyboard.transform.position, Vector3.zero);
            bool found = false;
            foreach (MeshRenderer renderer in _editingKeyboard.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            _dragWorldHeight = Mathf.Min(bounds.max.y + bounds.size.magnitude * _heightOffset,
                _camera.transform.position.y - _camera.nearClipPlane - 0.05f);
        }

        /// <summary>
        /// screenPosition의 UI 레이캐스트에서 편집 화면 배경을 제외하고 실제 UI가 있는지 반환한다.
        /// 풀러 상태는 변경하지 않으며 버튼과 패널 위에서 도구 선택을 차단한다.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
            _uiHits.Clear();
            EventSystem.current.RaycastAll(pointer, _uiHits);
            foreach (RaycastResult hit in _uiHits)
            {
                if (hit.gameObject.TryGetComponent<KeyboardEditOverlayUI>(out _)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// screenPosition이 풀러를 가리키거나 이미 풀러를 들고 있는지 반환한다.
        /// 현재 입력 허용 상태와 카메라 레이를 사용하며 UI 선택과 도구 상태는 변경하지 않는다.
        /// </summary>
        public bool IsPullerInteraction(Vector2 screenPosition)
        {
            if (!InputAllowed) return false;
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            return _keycapPuller.IsSelected || _switchPuller.IsSelected
                || _keycapPuller.IsPointerOverTool(ray) || _switchPuller.IsPointerOverTool(ray);
        }

        /// <summary>state에 현재 망치·풀러 사용과 실제 제거 대상의 조작을 추가한다. 도구 상태와 키보드 분리 조건을 읽는다.</summary>
        public bool CollectControlHints(WorkshopControlHintState state)
        {
            if (!InputAllowed || (_keycapPlacement != null && _keycapPlacement.IsDragging)) return false;
            if (_hammer != null && _hammer.IsHeld)
            {
                state.Title = "망치";
                state.Add("좌클릭 놓기", "키보드 타격");
                state.Add("도구함에 놓기", "망치 보관");
                return true;
            }
            Vector2 pointer = InGameCursor.Position;
            AssemblyFullerTool selected = _keycapPuller.IsSelected ? _keycapPuller : _switchPuller.IsSelected ? _switchPuller : null;
            Ray ray = _camera.ScreenPointToRay(pointer);
            if (selected != null)
            {
                state.Title = selected.ExtractsSwitches ? "스위치 풀러" : "키캡 풀러";
                Collider target = selected.GetExtractionTarget(pointer);
                KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
                if (target != null && customization.TryGetRemovablePart(target.transform, out _, out bool isSwitch)
                    && isSwitch == selected.ExtractsSwitches)
                    state.Add("좌클릭 유지·이동", isSwitch ? "스위치 연속 뽑기" : "키캡 연속 뽑기");
                state.Add("좌클릭 놓기", "풀러 내려놓기");
                return true;
            }
            if (IsPointerOverUI(pointer)) return false;
            AssemblyFullerTool tool = _keycapPuller.IsPointerOverTool(ray) ? _keycapPuller
                : _switchPuller.IsPointerOverTool(ray) ? _switchPuller : null;
            if (tool != null)
            {
                state.Title = tool.ExtractsSwitches ? "스위치 풀러" : "키캡 풀러";
                state.Add("좌클릭 유지", "풀러 집기");
                return true;
            }
            if (!TryGetPartHit(ray, out _, out bool extractSwitches)) return false;
            state.Add("좌클릭 드래그", extractSwitches ? "스위치 뽑기" : "키캡 뽑기");
            return true;
        }

        /// <summary>현재 포인터가 풀러 선택이나 사용에 해당하는지 반환한다. 기존 키보드 드래그 회전과 입력을 구분한다.</summary>
        public bool ConsumesPointer()
        {
            if (!InputAllowed || Mouse.current == null) return false;
            if (_keycapPlacement != null && _keycapPlacement.IsDragging) return true;
            Vector2 pointer = InGameCursor.Position;
            if (IsPointerOverUI(pointer)) return false;
            Ray ray = _camera.ScreenPointToRay(pointer);
            return _keycapPuller.IsSelected || _switchPuller.IsSelected
                || _keycapPuller.IsPointerOverTool(ray) || _switchPuller.IsPointerOverTool(ray)
                || (_keycapPlacement != null && _keycapPlacement.IsPointerOverLoosePart(ray))
                || TryGetPartHit(ray, out _, out _);
        }

        /// <summary>선택 중인 두 풀러를 원래 자리로 되돌린다. 이미 제거된 파츠와 키보드 상태는 유지한다.</summary>
        public void CancelDrag()
        {
            if (_guidePaper != null) _guidePaper.CancelInteraction();
            if (_hammer != null) _hammer.CancelInteraction();
            if (_keycapPlacement != null) _keycapPlacement.CancelDrag();
            _keycapPuller.ReturnToOrigin();
            _switchPuller.ReturnToOrigin();
        }

        /// <summary>hit가 현재 편집 키보드의 파츠이면 interactionId와 제거 결과를 기록하고 분리한다. 성공 여부를 반환한다.</summary>
        public bool TryExtractPart(Collider hit, bool extractSwitches, Vector3 velocity, Vector3 angularVelocity, string interactionId)
        {
            if (!InputAllowed || hit == null || _editingKeyboard == null) return false;
            KeyboardCustomization customization = _editingKeyboard.GetComponent<KeyboardCustomization>();
            int index = FindTargetIndex(customization, hit.transform, extractSwitches);
            if (index < 0) return false;
            bool removedSuccessfully = customization.TryRemovePart(hit.transform, extractSwitches, out Transform removed);
            string partId = extractSwitches
                ? customization.GetInstalledSwitchType(customization.GetSwitchSlotTransform(index).GetComponent<KeyboardSwitch>()).ToString()
                : GetKeycapPartId(customization, index);
            CustomizationTelemetry.Emit(customization, "PartRemoveAttempted", extractSwitches ? "Switch" : "Keycap", partId,
                success: removedSuccessfully, targetId: customization.GetKeycapSlotId(index),
                actionSource: "Tool", interactionId: interactionId,
                outcomeReason: removedSuccessfully ? "" : extractSwitches ? "KeycapBlocksSwitchRemoval" : "RemovalRejected");
            if (!removedSuccessfully) return false;
            if (!extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedKeycap(removed, true, velocity, angularVelocity))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return true;
            }
            if (extractSwitches && _keycapPlacement != null
                && _keycapPlacement.TakeRemovedSwitch(removed, true, velocity, angularVelocity))
            {
                AudioManager.PlayPartSound(removed.position, true);
                return true;
            }
            CreateDetachedVisual(removed, velocity, angularVelocity);
            AudioManager.PlayPartSound(removed.position, true);
            return true;
        }

        /// <summary>
        /// 현재 도구 집기나 내려놓기 행동을 열린 제작 회차에 전달한다.
        /// type, toolId와 interactionId를 기록하며 키보드 상태는 변경하지 않는다.
        /// </summary>
        public void RecordToolEvent(string type, string toolId, string interactionId)
        {
            CustomizationTelemetry.Emit(CustomizationLogger.CurrentTarget, type, "Tool", toolId,
                interactionId: interactionId, actionSource: "Tool");
        }

        /// <summary>
        /// hit가 지정 종류 슬롯의 장착 파츠 자식인지 확인하고 슬롯 인덱스를 반환한다.
        /// 일치하는 활성 파츠가 없으면 -1을 반환하며 설치 상태는 변경하지 않는다.
        /// </summary>
        private static int FindTargetIndex(KeyboardCustomization customization, Transform hit, bool extractSwitches)
        {
            for (int index = 0; index < customization.KeycapCount; index++)
            {
                Transform target = extractSwitches
                    ? customization.GetSwitchSlotTransform(index)
                    : customization.GetKeycapSlotTransform(index);
                if (target != null && target.gameObject.activeInHierarchy && hit.IsChildOf(target)) return index;
            }
            return -1;
        }

        /// <summary>
        /// index 키캡의 현재 정의 ID 또는 material ID를 반환한다.
        /// 해당 슬롯의 로그 정보를 사용하며 값이 없으면 Keycap을 반환한다.
        /// </summary>
        private static string GetKeycapPartId(KeyboardCustomization customization, int index)
        {
            foreach (string[] value in customization.GetKeycapLogValues())
                if (value[2] == index.ToString()) return value[1];
            return "Keycap";
        }

        /// <summary>
        /// removed의 메시와 재질을 복사하고 velocity와 angularVelocity를 적용한 Rigidbody를 반환한다.
        /// scheduleRemoval이 true이면 지연 제거를 예약하며 복사할 메시가 없으면 null을 반환한다.
        /// </summary>
        public Rigidbody CreateDetachedVisual(Transform removed, Vector3 velocity, Vector3 angularVelocity,
            bool scheduleRemoval = true)
        {
            GameObject visual = new GameObject("Removed_" + removed.name);
            visual.transform.position = removed.position;
            Bounds bounds = new Bounds();
            bool found = false;
            foreach (MeshRenderer source in removed.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!source.enabled || !IsVisibleChild(source.transform, removed)) continue;
                MeshFilter filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                GameObject mesh = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                mesh.transform.SetParent(visual.transform, false);
                mesh.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                mesh.transform.localScale = source.transform.lossyScale;
                mesh.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                var properties = new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);
                renderer.SetPropertyBlock(properties);
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            if (!found) { Destroy(visual); return null; }
            BoxCollider collider = visual.AddComponent<BoxCollider>();
            collider.center = visual.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
            if (_interaction != null)
                foreach (Collider player in _interaction.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(collider, player);
            foreach (Collider keyboardCollider in EditingKeyboard.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(collider, keyboardCollider);
            Rigidbody body = visual.AddComponent<Rigidbody>();
            body.mass = 0.02f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            _fragments.RemoveAll(fragment => fragment == null);
            _fragments.Add(visual);
            if (scheduleRemoval) Destroy(visual, _returnDelay);
            return body;
        }

        /// <summary>child와 root 사이에 비활성 장식이 있는지 검사해 실제 표시 대상인지 반환한다. 제거로 비활성화한 root 자체는 제외한다.</summary>
        private bool IsVisibleChild(Transform child, Transform root)
        {
            while (child != root)
            {
                if (!child.gameObject.activeSelf) return false;
                child = child.parent;
            }
            return true;
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= CancelDrag;
            SetToolTraySubscriptions(false);
            CancelDrag();
            RefreshToolVisibility();
            _keycapPuller.HeldToolChanged -= HandleHeldToolChanged;
            _switchPuller.HeldToolChanged -= HandleHeldToolChanged;
            _editingKeyboard = null;
            UpdateDragHeight();
        }

        void OnDestroy()
        {
            if (_hammer != null) Destroy(_hammer.gameObject);
            _keycapHoverPreview?.Dispose();
            _switchHoverPreview?.Dispose();
            foreach (GameObject fragment in _fragments) if (fragment != null) Destroy(fragment);
        }
    }
}

