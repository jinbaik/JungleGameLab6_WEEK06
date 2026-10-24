using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Unity.Cinemachine;

using Game.Logging;
using Game.Tutorials;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class WorkshopKeyboardDragController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private KeyboardPickupTarget _pickupTarget;
        [SerializeField] private Rigidbody _keyboardBody;
        [SerializeField] private Camera _outputCamera;
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private WorkshopViewController _viewController;

        [Header("Workbench Placement")]
        [SerializeField] private WorkbenchKeyboardHolder _workbenchHolder;
        [SerializeField] private Collider _placementArea;
        [SerializeField] private CinemachineCamera _workbenchCamera;
        [SerializeField] private Material _placementOutlineMaterial;
        [SerializeField] private Shader _placementDepthShader;
        private WorkshopKeyboardPlacementPreview _placementPreview;

        [Header("Monitor Placement")]
        [SerializeField] private MonitorKeyboardHolder _monitorHolder;
        [SerializeField] private Collider _monitorPlacementArea;
        [SerializeField] private CinemachineCamera _monitorCamera;

        [Header("Hanger Placement")]
        [SerializeField] private CinemachineCamera _hangerCamera;
        [SerializeField] private KeyboardWallRack[] _hangerHolders = new KeyboardWallRack[0];
        [SerializeField] private Collider[] _hangerAreas = new Collider[0];
        [SerializeField] private float _hangerWorldX = -3.18f;
        private bool _usingHangerPlane;
        private bool _awaitingHangerPlacement;

        [Header("Drag Plane")]
        [SerializeField, Tooltip("본체를 운반할 수평면의 절대 월드 Y 높이입니다.")]
        private float _dragWorldHeight = 0.145f;

        [Header("Drag State")]
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private Vector3 _pointerOffset;
        private Vector3 _centerOffset;
        private Quaternion _dragRotation;
        private bool _isDragging;
        private bool _inputAllowed = true;
        private bool _acceptPress;
        private bool _releasePending;
        private bool _clickCandidate;
        private Vector2 _pressPosition;

        public bool IsDragging => _isDragging;

        public bool IsPlaced => !_isDragging && _workbenchHolder.KeyboardBody != null;
        private bool IsSelectedOnWorkbench => _keyboardBody != null && _workbenchHolder.KeyboardBody == _keyboardBody;

        void Awake()
        {
            _placementPreview = new WorkshopKeyboardPlacementPreview(
                _placementOutlineMaterial, _placementDepthShader, _outputCamera);
        }

        void OnEnable()
        {
            _acceptPress = false;
            InGameCursor.PointerCanceled += HandlePointerCanceled;
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (!_inputAllowed || !Application.isFocused || !InGameCursor.CanProcessPointer
                || FirstEncounterTutorial.IsBlocking || PlaytestResearchSession.IsUploadConfirmationOpen
                || mouse == null)
            {
                HandlePointerCanceled();
                return;
            }

            if (_isDragging)
            {
                if (_awaitingHangerPlacement)
                {
                    if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI())
                        _releasePending = true;
                    else if (mouse.leftButton.wasReleasedThisFrame)
                        _releasePending = true;
                }
                else if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                    _releasePending = true;
                return;
            }

            if (_clickCandidate)
            {
                if (!IsSelectedOnWorkbench || !_viewController.CanEnterKeyboardFocus)
                {
                    _clickCandidate = false;
                    _acceptPress = false;
                    return;
                }
                float threshold = EventSystem.current.pixelDragThreshold;
                if (((Vector2)InGameCursor.Position - _pressPosition).sqrMagnitude > threshold * threshold)
                {
                    _clickCandidate = false;
                    _acceptPress = false;
                    if (!IsPointerOverUI() && TryBeginDrag(false) && !mouse.leftButton.isPressed)
                        _releasePending = true;
                }
                else if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                {
                    _clickCandidate = false;
                    _acceptPress = false;
                    if (!IsPointerOverUI() && IsKeyboardUnderCursor())
                        _viewController.TryEnterKeyboardFocus(_keyboardBody);
                }
                return;
            }

            if (!_acceptPress)
            {
                _acceptPress = !mouse.leftButton.isPressed;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !_brain.IsBlending
                && _viewController.ViewSwitchingAllowed && !IsPointerOverUI())
            {
                if (!TryGetKeyboardUnderCursor(out KeyboardPickupTarget pickup, out Rigidbody body))
                    return;
                _pickupTarget = pickup;
                _keyboardBody = body;
                if (IsSelectedOnWorkbench && _viewController.CanEnterKeyboardFocus)
                {
                    _clickCandidate = true;
                    _pressPosition = InGameCursor.Position;
                }
                else
                    TryBeginDrag(false);
            }
        }

        void LateUpdate()
        {
            if (!_isDragging)
                return;
            if (_keyboardBody == null)
            {
                HandlePointerCanceled();
                return;
            }

            if (!_brain.IsBlending)
                _usingHangerPlane = _hangerCamera != null && _brain.ActiveVirtualCamera == (ICinemachineCamera)_hangerCamera;
            if (!_brain.IsBlending && !_usingHangerPlane && _awaitingHangerPlacement)
                _releasePending = true;
            Quaternion rotation = _outputCamera.transform.rotation * _dragRotation;
            // Brain이 출력 카메라를 갱신한 뒤 같은 프레임의 자세를 적용한다.
            if (TryGetDragPosition(out Vector3 position))
                _keyboardBody.position = position;
            _keyboardBody.rotation = rotation;
            KeyboardWallRack hanger = _usingHangerPlane ? GetHangerUnderCursor() : null;
            bool hasHolder = !_usingHangerPlane && IsHolderUnderCursor();
            bool hasMonitor = !_usingHangerPlane && IsMonitorUnderCursor();
            if (!_brain.IsBlending && !_releasePending)
            {
                if (hanger != null) _placementPreview.Show(hanger.GetPlacementPose(_centerOffset));
                else if (hasMonitor) _placementPreview.Show(_monitorHolder.GetPlacementPose(_centerOffset));
                else if (hasHolder) _placementPreview.Show(_workbenchHolder.GetPlacementPose(_centerOffset, _keyboardBody));
            }
            if (_releasePending && !_brain.IsBlending)
            {
                bool placed = hanger != null ? hanger.TryAttachKeyboard(_keyboardBody, true)
                    : hasMonitor ? _monitorHolder.TryPlaceKeyboard(_keyboardBody, _centerOffset)
                    : hasHolder && _workbenchHolder.TryPlaceKeyboard(_keyboardBody, _centerOffset);
                if (placed)
                {
                    if (hanger != null)
                    {
                        Pose pose = hanger.GetPlacementPose(_centerOffset);
                        _keyboardBody.position = pose.position;
                        _keyboardBody.rotation = pose.rotation;
                        _keyboardBody.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    }
                    _isDragging = false;
                    _releasePending = false;
                    _awaitingHangerPlacement = false;
                    _acceptPress = false;
                }
                else if (_usingHangerPlane)
                {
                    _releasePending = false;
                    _awaitingHangerPlacement = true;
                }
                else
                    DropKeyboard();
            }
        }

        /// <summary>
        /// 명시된 장식장 영역 중 커서 Ray가 가장 먼저 닿는 빈 Holder를 반환한다.
        /// UI와 블렌드 및 실제 물체 가림을 검사하고 운반 본체와 장착 자식은 제외한다.
        /// </summary>
        private KeyboardWallRack GetHangerUnderCursor()
        {
            if (_brain.IsBlending || IsPointerOverUI()) return null;
            Physics.SyncTransforms();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            float nearest = _outputCamera.farClipPlane;
            KeyboardWallRack holder = null;
            for (int i = 0; i < _hangerHolders.Length; i++)
            {
                if (_hangerAreas[i].Raycast(ray, out RaycastHit hit, nearest))
                {
                    nearest = hit.distance;
                    holder = _hangerHolders[i];
                }
            }
            return holder != null && holder.KeyboardBody == null && !IsPlacementOccluded(ray, nearest) ? holder : null;
        }

        /// <summary>
        /// ray에서 배치 영역의 distance 앞에 실제 가림 물체가 있으면 true를 반환한다.
        /// 운반 본체와 그 자식 충돌체는 제외하고 물체 상태는 변경하지 않는다.
        /// </summary>
        private bool IsPlacementOccluded(Ray ray, float distance)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(ray, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(_keyboardBody.transform) && hit.distance < distance)
                    return true;
            return false;
        }

        /// <summary>
        /// 공통 커서 Ray로 명시한 작업대 배치 영역과 가림 물체를 검사하여 Holder 선택 가능 여부를 반환한다.
        /// 운반 본체와 장착 자식은 제외하며 점유와 UI 및 블렌드와 현재 뷰 조건도 함께 확인한다.
        /// </summary>
        private bool IsHolderUnderCursor()
        {
            if (_brain.IsBlending || _brain.ActiveVirtualCamera != (ICinemachineCamera)_workbenchCamera
                || _workbenchHolder.KeyboardBody != null || IsPointerOverUI())
                return false;

            Physics.SyncTransforms();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            if (!_placementArea.Raycast(ray, out RaycastHit areaHit, _outputCamera.farClipPlane))
                return false;

            return !IsPlacementOccluded(ray, areaHit.distance);
        }

        /// <summary>
        /// 모니터 일반 뷰에서 명시된 빈 Holder 영역을 커서로 가리키는지 반환한다.
        /// 기존 UI와 가림 판정을 재사용하며 운반 중인 본체와 자식은 제외한다.
        /// </summary>
        private bool IsMonitorUnderCursor()
        {
            if (_monitorHolder == null || _brain.IsBlending || IsPointerOverUI()
                || _brain.ActiveVirtualCamera != (ICinemachineCamera)_monitorCamera || _monitorHolder.KeyboardBody != null)
                return false;
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            return _monitorPlacementArea.Raycast(ray, out RaycastHit hit, _outputCamera.farClipPlane)
                && !IsPlacementOccluded(ray, hit.distance);
        }

        /// <summary>
        /// allowed를 사용하여 본체 운반 입력 허용 상태를 변경한다.
        /// false이면 현재 위치에서 중력으로 놓고 새 클릭을 기다리며 후속 집중 모드의 차단점으로 사용한다.
        /// </summary>
        public void SetInputAllowed(bool allowed)
        {
            _inputAllowed = allowed;
            if (!allowed)
                HandlePointerCanceled();
        }

        /// <summary>
        /// 공통 커서 좌표로 UI Raycast를 수행하여 GraphicRaycaster에 적중했는지 반환한다.
        /// 물리 Raycaster 결과는 제외하고 실제 화면 UI 위에서 본체를 집는 것을 막는다.
        /// </summary>
        private bool IsPointerOverUI()
        {
            _uiHits.Clear();
            var pointer = new PointerEventData(EventSystem.current) { position = InGameCursor.Position };
            EventSystem.current.RaycastAll(pointer, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.module is GraphicRaycaster)
                    return true;
            return false;
        }

        /// <summary>
        /// 커서의 가장 가까운 유효 키보드가 현재 선택 본체인지 반환한다.
        /// 클릭 후보 해제 시 같은 키보드를 계속 가리키는지 확인하며 선택 참조는 변경하지 않는다.
        /// </summary>
        private bool IsKeyboardUnderCursor()
        {
            return TryGetKeyboardUnderCursor(out _, out Rigidbody body) && body == _keyboardBody;
        }

        /// <summary>
        /// 출력 카메라의 커서 Ray에서 가장 가까운 키보드의 집기 표식과 본체를 반환한다.
        /// 가림 물체와 제거 도구 및 분리 부품은 거부하며 기존 배치 키보드와 런타임 배출 인스턴스를 모두 허용한다.
        /// </summary>
        private bool TryGetKeyboardUnderCursor(out KeyboardPickupTarget pickup, out Rigidbody body)
        {
            pickup = null;
            body = null;
            Physics.SyncTransforms();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            if (!Physics.Raycast(ray, out RaycastHit hit, _outputCamera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<AssemblyFullerTool>() != null)
                return false;

            AssemblyDraggablePart part = hit.collider.GetComponentInParent<AssemblyDraggablePart>();
            if (part != null && (part.Slot == null || part.Slot.InstalledPart != part))
                return false;
            pickup = hit.collider.GetComponentInParent<KeyboardPickupTarget>();
            if (pickup == null) return false;
            body = pickup.GetComponent<Rigidbody>();
            if (body == null) body = pickup.GetComponentInChildren<Rigidbody>();
            return body != null && hit.transform.IsChildOf(body.transform);
        }

        /// <summary>
        /// requireHit이 true면 본체 선택을 검사하고 현재 커서에서 수평면 운반을 시작한다.
        /// 오프셋과 상대 회전을 저장하고 기존 Holder 점유를 해제하며 시작 성공 여부를 반환한다.
        /// </summary>
        private bool TryBeginDrag(bool requireHit)
        {
            if (requireHit && !IsKeyboardUnderCursor())
                return false;
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);

            _usingHangerPlane = _hangerCamera != null && _brain.ActiveVirtualCamera == (ICinemachineCamera)_hangerCamera;
            Plane dragPlane = GetDragPlane();
            if (!dragPlane.Raycast(ray, out float distance) || distance > _outputCamera.farClipPlane)
                return false;

            _workbenchHolder.ReleaseKeyboard(_keyboardBody);
            bool detach = _keyboardBody.transform.parent == _workbenchHolder.transform;
            if (_monitorHolder != null && _monitorHolder.KeyboardBody == _keyboardBody)
            {
                _monitorHolder.ReleaseKeyboard(_keyboardBody);
                detach |= _keyboardBody.transform.parent == _monitorHolder.transform;
            }
            foreach (KeyboardWallRack holder in _hangerHolders)
            {
                if (holder.KeyboardBody != _keyboardBody) continue;
                holder.ReleaseKeyboard(_keyboardBody);
                detach |= _keyboardBody.transform.parent == holder.transform;
            }
            if (detach) _keyboardBody.transform.SetParent(null, true);

            Vector3 center = _keyboardBody.transform.TransformPoint(
                _pickupTarget.GetVisualCenterLocalPosition(_keyboardBody.transform));
            _pointerOffset = _keyboardBody.position - ray.GetPoint(distance);
            if (_usingHangerPlane) _pointerOffset.x = 0f;
            else _pointerOffset.y = 0f;
            _centerOffset = Quaternion.Inverse(_keyboardBody.rotation) * (center - _keyboardBody.position);
            _dragRotation = Quaternion.Inverse(_outputCamera.transform.rotation) * _keyboardBody.rotation;
            if (!_keyboardBody.isKinematic)
            {
                _keyboardBody.linearVelocity = Vector3.zero;
                _keyboardBody.angularVelocity = Vector3.zero;
            }
            _keyboardBody.isKinematic = true;
            _keyboardBody.useGravity = false;
            Vector3 position = _keyboardBody.position;
            if (_usingHangerPlane) position.x = _hangerWorldX;
            else position.y = _dragWorldHeight;
            _keyboardBody.position = position;
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _releasePending = false;
            _awaitingHangerPlacement = false;
            _placementPreview.Prepare(_keyboardBody.transform);
            _isDragging = true;
            return true;
        }

        /// <summary>
        /// 현재 커서 Ray와 뷰별 고정 X 또는 Y 평면으로 본체 목표 위치를 계산한다.
        /// 이동 가능한 오프셋 성분만 적용하며 교차 실패와 카메라 범위 밖 교점은 false를 반환해 마지막 위치를 유지한다.
        /// </summary>
        private bool TryGetDragPosition(out Vector3 position)
        {
            Plane dragPlane = GetDragPlane();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            position = _keyboardBody.position;
            if (!dragPlane.Raycast(ray, out float distance) || distance > _outputCamera.farClipPlane)
                return false;
            Vector3 offset = _pointerOffset;
            if (_usingHangerPlane) offset.x = 0f;
            else offset.y = 0f;
            position = ray.GetPoint(distance) + offset;
            if (_usingHangerPlane) position.x = _hangerWorldX;
            else position.y = _dragWorldHeight;
            return true;
        }

        /// <summary>
        /// 현재 운반 뷰에 맞는 고정 X의 YZ 평면 또는 고정 Y의 XZ 평면을 반환한다.
        /// Inspector의 월드 좌표를 사용하며 키보드 위치는 변경하지 않는다.
        /// </summary>
        private Plane GetDragPlane()
        {
            return _usingHangerPlane ? new Plane(Vector3.right, new Vector3(_hangerWorldX, 0f, 0f))
                : new Plane(Vector3.up, new Vector3(0f, _dragWorldHeight, 0f));
        }

        /// <summary>
        /// 운반 중인 본체를 현재 위치에서 중력 낙하 상태로 전환한다.
        /// 해제 보류와 클릭 허용을 초기화하고 속도와 각속도를 비우며 외부 전환 차단과 슬롯 및 재고는 변경하지 않는다.
        /// </summary>
        private void DropKeyboard()
        {
            if (!_isDragging)
                return;
            _isDragging = false;
            _releasePending = false;
            _awaitingHangerPlacement = false;
            _acceptPress = false;
            if (_keyboardBody == null) return;
            _keyboardBody.isKinematic = false;
            _keyboardBody.useGravity = true;
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _keyboardBody.linearVelocity = Vector3.zero;
            _keyboardBody.angularVelocity = Vector3.zero;
            _keyboardBody.WakeUp();
        }

        /// <summary>
        /// 포인터 취소 알림을 받아 현재 운반을 중력 낙하로 정리한다.
        /// 입력값 없이 클릭 허용을 해제하여 버튼을 놓은 뒤 새로 눌러야 집을 수 있게 한다.
        /// </summary>
        private void HandlePointerCanceled()
        {
            _clickCandidate = false;
            _awaitingHangerPlacement = false;
            _releasePending = false;
            DropKeyboard();
            _acceptPress = false;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                HandlePointerCanceled();
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= HandlePointerCanceled;
            HandlePointerCanceled();
        }

        void OnDestroy()
        {
            _placementPreview.Dispose();
        }
    }
}
