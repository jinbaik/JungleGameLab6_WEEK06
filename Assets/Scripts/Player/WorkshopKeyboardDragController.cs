using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Unity.Cinemachine;

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
        private float _hangerDragWorldX;

        [Header("Drag Plane")]
        [SerializeField, Min(0.5f)] private float _maximumDragDistance = 10f;
        [SerializeField, Min(0f)] private float _dragSurfaceClearance = 0.01f;
        private float _dragDepth;
        private Bounds _dragVisualBounds;

        [Header("Drag State")]
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private readonly List<(Rigidbody Body, bool DetectCollisions)> _dragCollisionStates
            = new List<(Rigidbody Body, bool DetectCollisions)>();
        private Vector3 _centerOffset;
        private Quaternion _dragRotation;
        private bool _isDragging;
        private bool _inputAllowed = true;
        private bool _acceptPress;
        private bool _releasePending;

        public bool IsDragging => _isDragging;

        public bool IsPlaced => !_isDragging && _workbenchHolder.KeyboardBody != null;

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
            if (!_inputAllowed || !_viewController.CanProcessGeneralInput
                || mouse == null)
            {
                HandlePointerCanceled();
                return;
            }

            if (_isDragging)
            {
                if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI())
                    _releasePending = true;
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
                TryBeginDrag();
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
            {
                bool hangerPlane = _hangerCamera != null && _viewController.IsGeneralView(_hangerCamera);
                if (hangerPlane != _usingHangerPlane)
                {
                    _usingHangerPlane = hangerPlane;
                    CacheDragPointer();
                }
            }
            Quaternion rotation = _outputCamera.transform.rotation * _dragRotation;
            // Brain이 출력 카메라를 갱신한 뒤 같은 프레임의 자세를 적용한다.
            _keyboardBody.position = PullDragPositionBeforeSurface(GetDragPosition(rotation), rotation);
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
                _pickupTarget.SetPlacementSoundPending(true);
                bool placed = hanger != null ? hanger.TryAttachKeyboard(_keyboardBody, true)
                    : hasMonitor ? _monitorHolder.TryPlaceKeyboard(_keyboardBody, _centerOffset)
                    : hasHolder && _workbenchHolder.TryPlaceKeyboard(_keyboardBody, _centerOffset);
                if (placed)
                {
                    RestoreDragCollisions();
                    if (hanger != null)
                    {
                        _pickupTarget.SetPlacementSoundPending(false);
                        Pose pose = hanger.GetPlacementPose(_centerOffset);
                        _keyboardBody.position = pose.position;
                        _keyboardBody.rotation = pose.rotation;
                        _keyboardBody.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    }
                    _isDragging = false;
                    _releasePending = false;
                    _acceptPress = false;
                }
                else if (_usingHangerPlane)
                {
                    _releasePending = false;
                }
                else
                    DropKeyboard(true);
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
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
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
            if (_brain.IsBlending || !_viewController.IsGeneralView(_workbenchCamera)
                || _workbenchHolder.KeyboardBody != null || IsPointerOverUI())
                return false;

            Physics.SyncTransforms();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
            if (!_placementArea.Raycast(ray, out RaycastHit areaHit, _outputCamera.farClipPlane))
                return false;

            return !IsPlacementOccluded(ray, areaHit.distance);
        }

        /// <summary>
        /// 공통 커서와 출력 카메라로 작업대 선택 영역을 가리키는지 반환한다.
        /// 현재 Holder에 스냅된 본체와 자식만 가림에서 제외하고 UI와 다른 물체는 차단한다.
        /// </summary>
        public bool IsWorkbenchFocusTarget()
        {
            if (_isDragging || _brain.IsBlending || !_viewController.IsGeneralView(_workbenchCamera)
                || IsPointerOverUI()) return false;
            Physics.SyncTransforms();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
            if (!_placementArea.Raycast(ray, out RaycastHit areaHit, _outputCamera.farClipPlane)) return false;
            Rigidbody snappedBody = _workbenchHolder.KeyboardBody;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, areaHit.distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.collider != _placementArea && hit.distance < areaHit.distance
                    && (snappedBody == null || !hit.transform.IsChildOf(snappedBody.transform)))
                    return false;
            return true;
        }

        /// <summary>body를 ray가 닿는 작업대 영역에 기존 자세와 높이 보정으로 장착한 결과를 반환한다. 자기 Collider를 제외한 가림과 점유를 확인하며 재고와 편집 상태는 변경하지 않는다.</summary>
        public bool TryPlaceInventoryKeyboard(Rigidbody body, Ray ray)
        {
            Rigidbody current = _workbenchHolder.KeyboardBody;
            if (current != null && !current.gameObject.activeInHierarchy)
            {
                _workbenchHolder.ReleaseKeyboard(current);
                current = null;
            }
            if (current == body) return true;
            if (_brain.IsBlending || _isDragging || current != null || IsPointerOverUI(true)) return false;
            Physics.SyncTransforms();
            if (!_placementArea.Raycast(ray, out RaycastHit areaHit, _outputCamera.farClipPlane)) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, areaHit.distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(body.transform) && hit.distance < areaHit.distance) return false;
            KeyboardPickupTarget pickup = body.GetComponent<KeyboardPickupTarget>();
            Vector3 centerOffset = Quaternion.Inverse(body.rotation)
                * body.transform.TransformVector(pickup.GetVisualCenterLocalPosition(body.transform));
            pickup.SetPlacementSoundPending(true);
            return _workbenchHolder.TryPlaceKeyboard(body, centerOffset);
        }

        /// <summary>
        /// 모니터 일반 뷰에서 명시된 빈 Holder 영역을 커서로 가리키는지 반환한다.
        /// 기존 UI와 가림 판정을 재사용하며 운반 중인 본체와 자식은 제외한다.
        /// </summary>
        private bool IsMonitorUnderCursor()
        {
            if (_monitorHolder == null || _brain.IsBlending || IsPointerOverUI()
                || !_viewController.IsGeneralView(_monitorCamera) || _monitorHolder.KeyboardBody != null)
                return false;
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
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
        /// 공통 커서 좌표로 UI Raycast를 수행하여 실제 화면 UI에 적중했는지 반환한다.
        /// ignoreEditBackground가 참이면 편집 전체 배경만 제외하며 버튼·서랍과 일반 본체 집기의 차단은 유지한다.
        /// </summary>
        private bool IsPointerOverUI(bool ignoreEditBackground = false)
        {
            _uiHits.Clear();
            var pointer = new PointerEventData(EventSystem.current) { position = InGameCursor.GetWorldPosition(_outputCamera) };
            EventSystem.current.RaycastAll(pointer, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.module is GraphicRaycaster
                    && !(ignoreEditBackground && hit.gameObject.TryGetComponent<KeyboardEditOverlayUI>(out _)))
                    return true;
            return false;
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
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
            if (!Physics.Raycast(ray, out RaycastHit hit, _outputCamera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<AssemblyFullerTool>() != null)
                return false;

            AssemblyDraggablePart part = hit.collider.GetComponentInParent<AssemblyDraggablePart>();
            bool issuedKeyboard = part != null && part.CatalogEntry != null && part.CatalogEntry.IsStructure
                && part.CatalogEntry.StructureKind == AssemblyStructurePart.Lower && part.GetComponent<KeyboardPickupTarget>() != null;
            if (part != null && !issuedKeyboard && (part.Slot == null || part.Slot.InstalledPart != part))
                return false;
            pickup = hit.collider.GetComponentInParent<KeyboardPickupTarget>();
            if (pickup == null) return false;
            body = pickup.GetComponent<Rigidbody>();
            if (body == null) body = pickup.GetComponentInChildren<Rigidbody>();
            return body != null && hit.transform.IsChildOf(body.transform);
        }

        /// <summary>
        /// 선택한 본체와 현재 커서로 고정 깊이 운반을 시작한다.
        /// 오프셋과 상대 회전을 저장하고 기존 Holder 점유를 해제하며 시작 성공 여부를 반환한다.
        /// </summary>
        private bool TryBeginDrag()
        {
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));

            _usingHangerPlane = _hangerCamera != null && _viewController.IsGeneralView(_hangerCamera);
            CacheDragPointer();
            Plane dragPlane = GetDragPlane();
            if (!dragPlane.Raycast(ray, out float distance) || distance > _maximumDragDistance)
                return false;

            Transform previousParent = _keyboardBody.transform.parent;
            if (previousParent != null && previousParent.TryGetComponent(out Game.Workshop.WorkshopDeliveryTray tray))
            {
                if (tray.IsSending) return false;
                previousParent.GetComponent<WorkbenchKeyboardHolder>().ReleaseKeyboard(_keyboardBody);
                _keyboardBody.transform.SetParent(null, true);
            }
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

            _pickupTarget.SetPlacementSoundPending(false);
            Vector3 center = _keyboardBody.transform.TransformPoint(
                _pickupTarget.GetVisualCenterLocalPosition(_keyboardBody.transform));
            _centerOffset = Quaternion.Inverse(_keyboardBody.rotation) * (center - _keyboardBody.position);
            _dragRotation = Quaternion.Inverse(_outputCamera.transform.rotation) * _keyboardBody.rotation;
            if (!_keyboardBody.isKinematic)
            {
                _keyboardBody.linearVelocity = Vector3.zero;
                _keyboardBody.angularVelocity = Vector3.zero;
            }
            _keyboardBody.isKinematic = true;
            _keyboardBody.useGravity = false;
            DisableDragCollisions();
            _dragVisualBounds = _pickupTarget.GetVisualBoundsLocal(_keyboardBody.transform);
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _releasePending = false;
            _placementPreview.Prepare(_keyboardBody.transform);
            _isDragging = true;
            return true;
        }

        /// <summary>
        /// 현재 키보드 중심과 카메라로 운반 깊이 및 장식장 기준 X를 저장한다.
        /// 집은 지점의 화면 오프셋은 저장하지 않아 시각적 중심이 중앙 조준을 우선 따르게 한다.
        /// </summary>
        private void CacheDragPointer()
        {
            if (_usingHangerPlane)
                _hangerDragWorldX = _keyboardBody.GetComponentInParent<KeyboardWallRack>() != null
                    ? _keyboardBody.position.x : _hangerWorldX;
            Transform camera = _outputCamera.transform;
            Vector3 center = _keyboardBody.transform.TransformPoint(
                _pickupTarget.GetVisualCenterLocalPosition(_keyboardBody.transform));
            _dragDepth = Mathf.Clamp(Vector3.Dot(center - camera.position, camera.forward),
                _outputCamera.nearClipPlane + 0.25f, _maximumDragDistance * 0.85f);
        }

        /// <summary>
        /// 운반 본체와 장착 자식 Rigidbody의 충돌 감지 설정을 보관하고 끈다.
        /// 현재 키보드 계층만 사용하며 Collider 형상과 다른 물체의 물리 설정은 변경하지 않는다.
        /// </summary>
        private void DisableDragCollisions()
        {
            _dragCollisionStates.Clear();
            foreach (Rigidbody body in _keyboardBody.GetComponentsInChildren<Rigidbody>(true))
            {
                _dragCollisionStates.Add((body, body.detectCollisions));
                body.detectCollisions = false;
            }
        }

        /// <summary>
        /// 운반 시작 시 보관한 각 Rigidbody의 충돌 감지 설정을 복원한다.
        /// 파괴된 장착 부품은 건너뛰고 보관 목록을 비워 다음 운반에 이전 상태가 남지 않게 한다.
        /// </summary>
        private void RestoreDragCollisions()
        {
            foreach (var state in _dragCollisionStates)
                if (state.Body != null) state.Body.detectCollisions = state.DetectCollisions;
            _dragCollisionStates.Clear();
        }

        /// <summary>
        /// rotation과 중앙 조준 Ray로 키보드 시각적 중심이 커서를 따르는 본체 목표 위치를 반환한다.
        /// 운반 평면의 교점을 우선 사용하며 교차 불가 또는 최대 거리 밖에서는 저장 깊이로 중앙 추종을 유지한다.
        /// </summary>
        private Vector3 GetDragPosition(Quaternion rotation)
        {
            Plane dragPlane = GetDragPlane();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
            if (!dragPlane.Raycast(ray, out float distance) || distance > _maximumDragDistance)
                distance = _dragDepth;
            return ray.GetPoint(distance) - rotation * _centerOffset;
        }

        /// <summary>
        /// position과 rotation의 키보드 경계를 중앙 조준 Ray로 BoxCast하여 표면 앞의 운반 위치를 반환한다.
        /// 시각적 중심을 Ray 위에 유지한 채 거리만 줄이며 본체 자식과 Trigger 및 물리 충돌 감지는 제외한다.
        /// </summary>
        private Vector3 PullDragPositionBeforeSurface(Vector3 position, Quaternion rotation)
        {
            Vector3 scale = _keyboardBody.transform.lossyScale;
            Vector3 centerOffset = rotation * Vector3.Scale(_dragVisualBounds.center, scale);
            Vector3 halfExtents = Vector3.Max(Vector3.one * 0.001f, Vector3.Scale(_dragVisualBounds.extents,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.GetWorldPosition(_outputCamera));
            float distance = Mathf.Max(0f, Vector3.Dot(position + centerOffset - ray.origin, ray.direction));
            float allowed = distance;
            Physics.SyncTransforms();
            foreach (RaycastHit hit in Physics.BoxCastAll(ray.origin, halfExtents, ray.direction, rotation,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(_keyboardBody.transform)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - _dragSurfaceClearance));
            }
            return ray.GetPoint(allowed) - centerOffset;
        }

        /// <summary>
        /// 장식장에서는 고정 X 평면을, 일반 운반에서는 카메라와 평행한 고정 깊이 평면을 반환한다.
        /// 저장된 운반 깊이와 카메라 자세를 사용하며 키보드 위치는 변경하지 않는다.
        /// </summary>
        private Plane GetDragPlane()
        {
            return _usingHangerPlane ? new Plane(Vector3.right, new Vector3(_hangerDragWorldX, 0f, 0f))
                : new Plane(_outputCamera.transform.forward,
                    _outputCamera.transform.position + _outputCamera.transform.forward * _dragDepth);
        }

        /// <summary>
        /// 운반 중인 본체를 현재 위치에서 중력 낙하 상태로 전환한다.
        /// userReleased로 거치음 예약 여부를 정하고 해제 보류와 클릭 허용 및 속도를 초기화한다.
        /// </summary>
        private void DropKeyboard(bool userReleased = false)
        {
            if (!_isDragging)
                return;
            _isDragging = false;
            _releasePending = false;
            _acceptPress = false;
            RestoreDragCollisions();
            if (_keyboardBody == null) return;
            _pickupTarget.SetPlacementSoundPending(userReleased);
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
