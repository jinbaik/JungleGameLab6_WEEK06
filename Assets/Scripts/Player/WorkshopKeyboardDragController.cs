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
        private float _hangerDragWorldX;
        private bool _awaitingHangerPlacement;

        [Header("Drag Plane")]
        [SerializeField, Min(0.5f)] private float _maximumDragDistance = 10f;
        [SerializeField, Min(0.001f)] private float _dragCollisionSkin = 0.01f;
        private float _dragDepth;
        private Bounds _dragLocalBounds;
        private readonly List<Collider> _dragColliders = new List<Collider>();

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
                || (FirstEncounterTutorial.IsBlocking || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked) || PlaytestResearchSession.IsUploadConfirmationOpen
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
            {
                bool hangerPlane = _hangerCamera != null && _brain.ActiveVirtualCamera == (ICinemachineCamera)_hangerCamera;
                if (hangerPlane != _usingHangerPlane)
                {
                    _usingHangerPlane = hangerPlane;
                    CacheDragPointer();
                }
            }
            if (!_brain.IsBlending && !_usingHangerPlane && _awaitingHangerPlacement)
                _releasePending = true;
            Quaternion rotation = _outputCamera.transform.rotation * _dragRotation;
            // Brain이 출력 카메라를 갱신한 뒤 같은 프레임의 자세를 적용한다.
            Physics.SyncTransforms();
            if (IsDragPoseBlocked(_keyboardBody.position, rotation)) rotation = _keyboardBody.rotation;
            if (TryGetDragPosition(out Vector3 position))
                _keyboardBody.position = LimitDragMovement(position, rotation);
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
            return _workbenchHolder.TryPlaceKeyboard(body, centerOffset);
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
        /// 공통 커서 좌표로 UI Raycast를 수행하여 실제 화면 UI에 적중했는지 반환한다.
        /// ignoreEditBackground가 참이면 편집 전체 배경만 제외하며 버튼·서랍과 일반 본체 집기의 차단은 유지한다.
        /// </summary>
        private bool IsPointerOverUI(bool ignoreEditBackground = false)
        {
            _uiHits.Clear();
            var pointer = new PointerEventData(EventSystem.current) { position = InGameCursor.Position };
            EventSystem.current.RaycastAll(pointer, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.module is GraphicRaycaster
                    && !(ignoreEditBackground && hit.gameObject.TryGetComponent<KeyboardEditOverlayUI>(out _)))
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
        /// requireHit이 true면 본체 선택을 검사하고 현재 커서에서 고정 깊이 운반을 시작한다.
        /// 오프셋과 상대 회전을 저장하고 기존 Holder 점유를 해제하며 시작 성공 여부를 반환한다.
        /// </summary>
        private bool TryBeginDrag(bool requireHit)
        {
            if (requireHit && !IsKeyboardUnderCursor())
                return false;
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);

            _usingHangerPlane = _hangerCamera != null && _brain.ActiveVirtualCamera == (ICinemachineCamera)_hangerCamera;
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
            CacheDragBounds();
            _keyboardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _releasePending = false;
            _awaitingHangerPlacement = false;
            _placementPreview.Prepare(_keyboardBody.transform);
            _isDragging = true;
            return true;
        }

        /// <summary>
        /// 현재 키보드 중심과 커서로 운반 깊이와 집은 지점의 오프셋을 저장한다.
        /// 일반 운반 오프셋은 카메라 로컬 좌표로 저장해 뷰 전환 후에도 상대 깊이를 유지한다.
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
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            if (!GetDragPlane().Raycast(ray, out float distance)) return;
            Vector3 offset = _keyboardBody.position - ray.GetPoint(distance);
            _pointerOffset = _usingHangerPlane ? offset : camera.InverseTransformVector(offset);
        }

        /// <summary>
        /// 운반 본체에 연결된 활성 충돌체의 경계를 키보드 로컬 공간에 캐시한다.
        /// 트리거와 별도 Rigidbody는 제외하며 이동 중 검사할 상자 크기를 저장한다.
        /// </summary>
        private void CacheDragBounds()
        {
            bool found = false;
            _dragColliders.Clear();
            foreach (Collider collider in _keyboardBody.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != _keyboardBody) continue;
                _dragColliders.Add(collider);
                Bounds bounds = GetColliderLocalBounds(collider);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = _keyboardBody.transform.InverseTransformPoint(collider.transform.TransformPoint(bounds.center
                        + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
                    if (found) _dragLocalBounds.Encapsulate(point);
                    else { _dragLocalBounds = new Bounds(point, Vector3.zero); found = true; }
                }
            }
        }

        /// <summary>collider의 형상 기준 로컬 경계를 반환한다. 회전으로 부풀어진 월드 경계를 제외하며 기타 형상은 월드 경계를 보수적으로 변환한다.</summary>
        private static Bounds GetColliderLocalBounds(Collider collider)
        {
            if (collider is BoxCollider box) return new Bounds(box.center, box.size);
            if (collider is MeshCollider mesh && mesh.sharedMesh != null) return mesh.sharedMesh.bounds;
            if (collider is SphereCollider sphere) return new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
            if (collider is CapsuleCollider capsule)
            {
                Vector3 size = Vector3.one * capsule.radius * 2f;
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                return new Bounds(capsule.center, size);
            }
            Bounds world = collider.bounds;
            Bounds local = new Bounds(collider.transform.InverseTransformPoint(world.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                local.Encapsulate(collider.transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f))));
            return local;
        }

        /// <summary>
        /// 캐시된 본체 경계와 현재 배율로 접촉 여유를 제외한 월드 반크기를 반환한다.
        /// 충돌체와 실제 키보드 크기는 변경하지 않는다.
        /// </summary>
        private Vector3 GetDragHalfExtents()
        {
            Vector3 scale = _keyboardBody.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return Vector3.Max(Vector3.one * 0.001f,
                Vector3.Scale(_dragLocalBounds.extents, scale) - Vector3.one * _dragCollisionSkin);
        }

        /// <summary>position과 rotation에서 키보드 경계의 월드 중심을 반환한다. 루트 피벗과 실제 중심의 차이를 반영한다.</summary>
        private Vector3 GetDragBoundsCenter(Vector3 position, Quaternion rotation)
        {
            return position + rotation * Vector3.Scale(_dragLocalBounds.center, _keyboardBody.transform.lossyScale);
        }

        /// <summary>collider가 자기 본체가 아닌 운반 장애물인지 반환한다. 장착 자식과 트리거는 충돌 검사에서 제외한다.</summary>
        private bool IsDragObstacle(Collider collider)
        {
            return collider.attachedRigidbody != _keyboardBody && !collider.transform.IsChildOf(_keyboardBody.transform);
        }

        /// <summary>
        /// obstacle과 접촉한 본체가 direction으로 관통을 악화시키지 않는지 반환한다.
        /// 실제 분리 방향과 접선 이동을 허용하며 검사 상자의 빈 공간은 실제 접촉으로 취급하지 않는다.
        /// </summary>
        private bool IsLeavingDragOverlap(Collider obstacle, Vector3 direction)
        {
            foreach (Collider collider in _dragColliders)
            {
                if (!Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation, out Vector3 separation, out _)) continue;
                if (Vector3.Dot(direction, separation) < -0.001f) return false;
            }
            return true;
        }

        /// <summary>position과 rotation의 실제 Collider가 새로 관통하거나 기존 관통을 심화하는지 반환한다. 빈 경계 공간과 기존 지지면 접촉은 회전을 막지 않는다.</summary>
        private bool IsDragPoseBlocked(Vector3 position, Quaternion rotation)
        {
            foreach (Collider collider in Physics.OverlapBox(GetDragBoundsCenter(position, rotation),
                GetDragHalfExtents(), rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsDragObstacle(collider)) continue;
                Quaternion delta = rotation * Quaternion.Inverse(_keyboardBody.rotation);
                foreach (Collider source in _dragColliders)
                {
                    Vector3 candidate = position + delta * (source.transform.position - _keyboardBody.position);
                    if (!Physics.ComputePenetration(source, candidate, delta * source.transform.rotation,
                        collider, collider.transform.position, collider.transform.rotation, out _, out float depth)) continue;
                    Physics.ComputePenetration(source, source.transform.position, source.transform.rotation,
                        collider, collider.transform.position, collider.transform.rotation, out _, out float previousDepth);
                    if (depth > Mathf.Max(previousDepth, _dragCollisionSkin) + 0.0001f) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// target까지 각 Collider의 실제 로컬 경계로 이동 검사하고 장애물 앞 위치를 반환한다.
        /// rotation과 접촉 여유를 사용하며 자기 충돌과 트리거 및 기존 접촉을 악화시키지 않는 이동은 제외한다.
        /// </summary>
        private Vector3 LimitDragMovement(Vector3 target, Quaternion rotation)
        {
            Vector3 start = _keyboardBody.position;
            Vector3 movement = target - start;
            float distance = movement.magnitude;
            if (distance < 0.00001f) return start;
            Vector3 direction = movement / distance;
            float allowed = distance;
            Quaternion delta = rotation * Quaternion.Inverse(_keyboardBody.rotation);
            foreach (Collider source in _dragColliders)
            {
                Bounds bounds = GetColliderLocalBounds(source);
                Vector3 center = start + delta * (source.transform.TransformPoint(bounds.center) - start);
                Vector3 scale = source.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Vector3 extents = Vector3.Max(Vector3.one * 0.001f,
                    Vector3.Scale(bounds.extents, scale) - Vector3.one * _dragCollisionSkin);
                foreach (RaycastHit hit in Physics.BoxCastAll(center, extents, direction,
                    delta * source.transform.rotation, distance + _dragCollisionSkin,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (!IsDragObstacle(hit.collider)) continue;
                    if (hit.distance <= _dragCollisionSkin && IsLeavingDragOverlap(hit.collider, direction)) continue;
                    allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - _dragCollisionSkin));
                }
            }
            if (IsDragPoseBlocked(start + direction * allowed, rotation))
            {
                float lower = 0f;
                float upper = allowed;
                for (int step = 0; step < 12; step++)
                {
                    float middle = (lower + upper) * 0.5f;
                    if (IsDragPoseBlocked(start + direction * middle, rotation)) upper = middle;
                    else lower = middle;
                }
                allowed = lower;
            }
            return start + direction * allowed;
        }

        /// <summary>
        /// 현재 커서 Ray와 운반 평면으로 본체 목표 위치를 계산한다.
        /// 카메라 상대 오프셋을 적용하며 교차 실패와 최대 운반 거리 밖 교점은 false를 반환해 마지막 위치를 유지한다.
        /// </summary>
        private bool TryGetDragPosition(out Vector3 position)
        {
            Plane dragPlane = GetDragPlane();
            Ray ray = _outputCamera.ScreenPointToRay(InGameCursor.Position);
            position = _keyboardBody.position;
            if (!dragPlane.Raycast(ray, out float distance) || distance > _maximumDragDistance)
                return false;
            Vector3 offset = _usingHangerPlane ? _pointerOffset : _outputCamera.transform.TransformVector(_pointerOffset);
            if (_usingHangerPlane) offset.x = 0f;
            position = ray.GetPoint(distance) + offset;
            if (_usingHangerPlane) position.x = _hangerDragWorldX;
            return true;
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
