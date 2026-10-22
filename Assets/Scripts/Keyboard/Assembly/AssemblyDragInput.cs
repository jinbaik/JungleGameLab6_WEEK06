using System.Collections;
using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyDragInput : MonoBehaviour, IKeyboardPullerContext
    {
        private const float EXTRACTED_RETURN_DELAY = 2f;

        [Header("Scene References")]
        [SerializeField] private Camera _camera;
        [SerializeField] private AssemblyPartSlot _partSlot;
        [SerializeField] private AssemblyPartSlot _switchSlot;
        [SerializeField] private AssemblyPartSlot[] _slots = new AssemblyPartSlot[0];
        [SerializeField] private AssemblyFullerTool _fullerTool;
        [SerializeField] private AssemblyFullerTool _switchPullerTool;
        private readonly List<AssemblyDraggablePart> _parts = new List<AssemblyDraggablePart>();
        private AssemblyDraggablePart _activePart;
        private AssemblyCatalogUI _catalog;
        private AssemblyCatalogEntry _inventoryEntry;
        private bool _fromInventory;
        private bool _showingIcon;
        private bool _inputAllowed = true;

        [Header("Drag Height")]
        [SerializeField, Min(0f)] private float _heightOffset = 0.3f;
        private Plane _dragPlane;
        private Vector3 _grabOffset;
        private float _dragHeight;
        private bool _isDragging;

        [Header("Pointer Tracking")]
        [SerializeField, Min(0.1f), Tooltip("드래그 중 실제 부품의 시각 중심을 배치할 카메라 전방 깊이입니다.")] private float _pointerDepth = 0.5f;
        private AssemblyDraggablePart _trackedPart;
        private Vector3 _trackingCenter;
        private Vector3 _trackingScale;
        private Quaternion _visualRotation;
        private float _visualDepth;
        private float _visualProjectionSize;
        private Vector3 _freeWorldPosition;
        private readonly Dictionary<Collider, bool> _heldColliders = new Dictionary<Collider, bool>();

        [Header("Bulk Placement")]
        private bool _isBulkPlacing;
        private AssemblyCatalogEntry _bulkEntry;
        private AssemblyDraggablePart _bulkPreview;
        private Vector2 _previousBulkPosition;
        private AssemblyPartSlot _hoverSlot;
        private Vector3 _freeScale;
        private Quaternion _freeRotation;
        private readonly HashSet<AssemblyPartSlot> _pathSlots = new HashSet<AssemblyPartSlot>();
        private bool _hasPreviousBulkPosition;
        private readonly HashSet<AssemblyPartSlot> _bulkVisited = new HashSet<AssemblyPartSlot>();
        private readonly List<KeyValuePair<float, AssemblyPartSlot>> _bulkCandidates = new List<KeyValuePair<float, AssemblyPartSlot>>();

        [Header("UI Input")]
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private PointerEventData _pointerEvent;
        private EventSystem _pointerEventSystem;

        [Header("Tool Returns")]
        private readonly Dictionary<AssemblyDraggablePart, int> _extractedReturns = new Dictionary<AssemblyDraggablePart, int>();

        public AssemblyPartSlot[] Slots => _slots;
        public IReadOnlyList<AssemblyDraggablePart> RegisteredParts => _parts;
        public bool InputAllowed => _inputAllowed;
        public bool IsDragging => _isDragging;
        public AssemblyPartSlot KeycapSlot => _partSlot;
        public AssemblyPartSlot SwitchSlot => _switchSlot;
        public Camera InputCamera => _camera;
        public float DragWorldHeight => _partSlot.transform.position.y + _heightOffset;
        public event Action<AssemblyDraggablePart> PartPicked;
        public event Action<AssemblyDraggablePart> ExtractedReturnScheduled;

        /// <summary>part가 현재 드래그나 일괄 배치 미리보기 대상인지 반환한다. 보이지 않는 UI 드래그 실물도 제외한다.</summary>
        public bool IsHoldingPart(AssemblyDraggablePart part)
        {
            return (_isDragging && _activePart == part) || _bulkPreview == part;
        }

        /// <summary>part의 현재 조작 버전에 유효한 도구 회수 예약이 있는지 반환한다. 오래된 예약은 대상에서 제외한다.</summary>
        public bool HasExtractedReturn(AssemblyDraggablePart part)
        {
            return _extractedReturns.TryGetValue(part, out int version) && version == part.InteractionVersion;
        }

        /// <summary>part의 집기 등록을 해제하고 기존 보관 경로로 반환한다. 실물이 활성 상태일 때만 재고를 한 번 늘리고 UI를 갱신한다.</summary>
        public void ReturnLoosePart(AssemblyDraggablePart part)
        {
            if (part == null || !part.gameObject.activeSelf || part.CatalogEntry == null)
                return;
            _parts.Remove(part);
            part.CatalogEntry.StorePart(part, true);
            _catalog.RefreshInventory();
        }

        /// <summary>hit에서 현재 슬롯에 등록된 지정 종류 파츠를 찾아 속도값으로 분리하고 지연 회수를 예약한다. 성공 여부를 반환한다.</summary>
        public bool TryExtractPart(Collider hit, bool extractSwitches, Vector3 velocity, Vector3 angularVelocity)
        {
            AssemblyDraggablePart part = hit != null ? hit.GetComponentInParent<AssemblyDraggablePart>() : null;
            if (part == null || part.IsSwitch != extractSwitches || Array.IndexOf(_slots, part.Slot) < 0
                || !part.TryPopOff(velocity, angularVelocity)) return false;
            ScheduleExtractedReturn(part);
            return true;
        }

        /// <summary>불러온 part를 기존 집기 대상에 등록한다. 중복 등록 없이 입력 대상 목록만 갱신한다.</summary>
        public void RegisterRestoredPart(AssemblyDraggablePart part)
        {
            if (!_parts.Contains(part)) _parts.Add(part);
        }

        void Awake()
        {
            var switchSlots = new Dictionary<Key, AssemblyPartSlot>();
            foreach (AssemblyPartSlot slot in _slots)
                if (slot.IsSwitch)
                    switchSlots.Add(slot.Key, slot);
            foreach (AssemblyPartSlot slot in _slots)
                if (!slot.IsSwitch)
                    slot.SetInstallationPrerequisite(switchSlots[slot.Key]);
        }

        void Start()
        {
            foreach (AssemblyPartSlot slot in _slots)
                _parts.Add(slot.OriginalPart);
        }

        void Update()
        {
            if (!_inputAllowed || Game.Tutorials.FirstEncounterTutorial.IsBlocking)
            {
                CancelDrag();
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null || !InGameCursor.CanProcessPointer)
            {
                CancelDrag();
                return;
            }

            Vector2 screenPosition = InGameCursor.Position;
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            if (_fullerTool != null && _fullerTool.ProcessPointer(ray, screenPosition, mouse))
                return;
            if (_switchPullerTool != null && _switchPullerTool.ProcessPointer(ray, screenPosition, mouse))
                return;
            if (!_isDragging && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI(screenPosition))
                TryBeginDrag(ray);

            if (!_isDragging)
                return;

            if (_isBulkPlacing)
            {
                UpdateBulkPlacement(ray, screenPosition);
                if (_isBulkPlacing && (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed))
                    EndBulkPlacement();
                return;
            }

            _hoverSlot = null;
            bool overUI = IsPointerOverUI(screenPosition);
            bool inReturnArea = _catalog != null && _catalog.IsInReturnArea(screenPosition);
            if (_inventoryEntry != null && (overUI || inReturnArea))
            {
                _showingIcon = true;
                if (_activePart != null)
                    _activePart.gameObject.SetActive(false);
                _catalog.ShowDragIcon(_inventoryEntry, screenPosition);
            }
            else if (TryShowWorldPart(ray))
            {
                _showingIcon = false;
                if (_catalog != null)
                    _catalog.HideDragIcon();
            }
            else if (_inventoryEntry != null)
            {
                _showingIcon = true;
                if (_activePart != null)
                    _activePart.gameObject.SetActive(false);
                _catalog.ShowDragIcon(_inventoryEntry, screenPosition);
            }

            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                CompleteDrag(inReturnArea, overUI);
        }

        /// <summary>
        /// allowed와 inputCamera로 조립 입력 허용 여부와 출력 카메라를 연결한다.
        /// 입력을 차단할 때 기존 조작만 취소하며 지연 회수 코루틴과 장착 결과는 유지한다.
        /// </summary>
        public void SetInputAllowed(bool allowed, Camera inputCamera)
        {
            if (!allowed)
                CancelDrag();
            _camera = inputCamera;
            _inputAllowed = allowed;
        }

        /// <summary>
        /// catalog를 이 입력의 인벤토리 UI로 연결한다.
        /// 월드에서 집은 부품의 반환 영역과 아이콘 표시를 같은 UI에서 처리한다.
        /// </summary>
        public void ConnectCatalog(AssemblyCatalogUI catalog)
        {
            _catalog = catalog;
        }

        /// <summary>
        /// 일괄로 분리한 part를 기존 카탈로그 항목으로 회수할 타이머를 시작한다.
        /// 연결된 항목과 현재 조작 버전을 사용하며 개별 분리에는 적용하지 않는다.
        /// </summary>
        public void ScheduleExtractedReturn(AssemblyDraggablePart part)
        {
            if (part.CatalogEntry == null || HasExtractedReturn(part))
                return;
            _extractedReturns[part] = part.InteractionVersion;
            ExtractedReturnScheduled?.Invoke(part);
            StartCoroutine(ReturnExtractedPart(part, part.InteractionVersion));
        }

        /// <summary>
        /// part가 interactionVersion 이후 다시 조작되지 않았으면 기존 도구 회수 지연 후 재고로 회수한다.
        /// 시간은 deltaTime으로 누적하며 실물의 집기 등록을 해제하고 재고 한 개와 UI를 갱신한다.
        /// </summary>
        private IEnumerator ReturnExtractedPart(AssemblyDraggablePart part, int interactionVersion)
        {
            float elapsed = 0f;
            try
            {
                while (elapsed < EXTRACTED_RETURN_DELAY)
                {
                    yield return null;
                    if (part == null || part.InteractionVersion != interactionVersion)
                        yield break;
                    elapsed += Time.deltaTime;
                }
                ReturnLoosePart(part);
            }
            finally
            {
                if (_extractedReturns.TryGetValue(part, out int version) && version == interactionVersion)
                    _extractedReturns.Remove(part);
            }
        }

        /// <summary>
        /// 예약된 entry를 screenPosition의 UI 아이콘으로 집는다.
        /// 입력을 사용할 수 없거나 이미 드래그 중이면 false를 반환하며 실물은 생성하지 않는다.
        /// </summary>
        public bool TryBeginInventoryDrag(AssemblyCatalogEntry entry, Vector2 screenPosition)
        {
            if (!_inputAllowed || _isDragging || !isActiveAndEnabled || Game.Tutorials.FirstEncounterTutorial.IsBlocking)
                return false;

            if (_fullerTool != null)
                _fullerTool.ReturnToOrigin();
            if (_switchPullerTool != null)
                _switchPullerTool.ReturnToOrigin();
            _inventoryEntry = entry;
            _fromInventory = true;
            _activePart = null;
            _showingIcon = true;
            _grabOffset = Vector3.zero;
            _dragHeight = _partSlot.transform.position.y + _heightOffset;
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, _dragHeight, 0f));
            _isDragging = true;
            _freeWorldPosition = new Vector3(_partSlot.transform.position.x, _dragHeight, _partSlot.transform.position.z);
            _catalog.ShowDragIcon(entry, screenPosition);
            return true;
        }

        /// <summary>
        /// entry의 키캡 또는 스위치를 screenPosition에서 일괄 배치할 미리보기로 준비한다.
        /// 재고와 입력 상태를 검사하고 실제 부품 등록이나 슬롯 점유 없이 배치를 시작한다.
        /// </summary>
        public bool TryBeginBulkPlacement(AssemblyCatalogEntry entry, Vector2 screenPosition)
        {
            if (!_inputAllowed || _isDragging || !isActiveAndEnabled || entry.AvailableQuantity <= 0)
                return false;

            if (_fullerTool != null)
                _fullerTool.ReturnToOrigin();
            if (_switchPullerTool != null)
                _switchPullerTool.ReturnToOrigin();
            _bulkEntry = entry;
            _dragHeight = _partSlot.transform.position.y + _heightOffset;
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, _dragHeight, 0f));
            _bulkPreview = Instantiate(entry.Prefab);
            _bulkPreview.name = entry.IsSwitch ? "BulkSwitchPreview" : "BulkKeycapPreview";
            AssemblyPartSlot previewSlot = entry.IsSwitch ? _switchSlot : _partSlot;
            _bulkPreview.Initialize(previewSlot, _bulkPreview.PartCollider, false);
            _bulkPreview.SetCatalogEntry(entry);
            _bulkPreview.PrepareInventoryPart(entry, _partSlot.KeycapShape);
            _bulkPreview.gameObject.SetActive(false);
            _freeScale = _bulkPreview.transform.localScale;
            _freeRotation = _bulkPreview.transform.rotation;
            _freeWorldPosition = new Vector3(_partSlot.transform.position.x, _dragHeight, _partSlot.transform.position.z);
            BeginPointerTracking(_bulkPreview);
            _bulkVisited.Clear();
            _hasPreviousBulkPosition = false;
            _isBulkPlacing = true;
            _isDragging = true;
            UpdateBulkPlacement(_camera.ScreenPointToRay(screenPosition), screenPosition);
            return true;
        }

        /// <summary>
        /// ray를 기존 드래그 평면에 투영하여 미리보기와 이전 프레임부터의 설치 경로를 갱신한다.
        /// screenPosition이 UI 위면 아이콘을 표시하며 재고가 소진되면 미리보기와 배치를 즉시 종료한다.
        /// </summary>
        private void UpdateBulkPlacement(Ray ray, Vector2 screenPosition)
        {
            if (_bulkEntry.AvailableQuantity <= 0)
            {
                EndBulkPlacement();
                return;
            }

            if (_dragPlane.Raycast(ray, out float distance))
            {
                _freeWorldPosition = ray.GetPoint(distance);
                _freeWorldPosition.y = _dragHeight;
            }
            Vector2 pointer = screenPosition;
            _hoverSlot = IsPointerOverUI(pointer) ? null : FindPointerSlot(ray, _bulkPreview);
            bool overUI = IsPointerOverUI(screenPosition);
            _bulkPreview.gameObject.SetActive(!overUI);
            if (!overUI)
                UpdatePointerTracking(ray);
            if (overUI)
                _catalog.ShowDragIcon(_bulkEntry, screenPosition);
            else
                _catalog.HideDragIcon();
            InstallAlongBulkPath(_hasPreviousBulkPosition ? _previousBulkPosition : pointer, pointer);
            if (_bulkEntry.AvailableQuantity <= 0)
            {
                EndBulkPlacement();
                return;
            }
            _previousBulkPosition = pointer;
            _hasPreviousBulkPosition = true;
        }

        /// <summary>
        /// 화면 start와 end 사이 커서 경로를 검사하여 유효한 빈 슬롯을 이동 순서대로 장착한다.
        /// UI에 가려진 지점과 이미 방문한 슬롯을 제외하고 성공한 설치마다 재고 한 개를 소비한다.
        /// </summary>
        private void InstallAlongBulkPath(Vector2 start, Vector2 end)
        {
            if (_bulkEntry.AvailableQuantity <= 0)
                return;

            _bulkCandidates.Clear();
            _pathSlots.Clear();
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / 3f));
            for (int index = 0; index <= steps; index++)
            {
                float progress = (float)index / steps;
                Vector2 pointer = Vector2.Lerp(start, end, progress);
                if (IsPointerOverUI(pointer))
                    continue;
                AssemblyPartSlot slot = index == steps ? _hoverSlot
                    : FindPointerSlot(_camera.ScreenPointToRay(pointer), _bulkPreview);
                if (slot != null && !slot.IsInstalled && !_bulkVisited.Contains(slot) && _pathSlots.Add(slot))
                    _bulkCandidates.Add(new KeyValuePair<float, AssemblyPartSlot>(progress, slot));
            }

            _bulkCandidates.Sort(CompareBulkCandidates);
            bool inventoryChanged = false;
            foreach (KeyValuePair<float, AssemblyPartSlot> candidate in _bulkCandidates)
            {
                AssemblyPartSlot slot = candidate.Value;
                if (slot.IsInstalled)
                    continue;
                if (!_bulkEntry.TryReserve())
                    break;
                AssemblyDraggablePart part = _bulkEntry.TakeStoredPart();
                if (part == null)
                    part = Instantiate(_bulkEntry.Prefab, slot.transform.position, slot.transform.rotation);
                part.Initialize(slot, part.PartCollider, false);
                part.SetCatalogEntry(_bulkEntry);
                part.PrepareInventoryPart(_bulkEntry, _partSlot.KeycapShape);
                part.gameObject.SetActive(true);
                bool installed = slot.IsCompatible(part) && slot.Install(part, true);
                _bulkEntry.FinishReservation(installed);
                if (installed)
                {
                    _parts.Add(part);
                    _bulkVisited.Add(slot);
                    inventoryChanged = true;
                }
                else
                {
                    _bulkEntry.StorePart(part, false);
                }
            }
            if (inventoryChanged)
                _catalog.RefreshInventory();
        }

        /// <summary>
        /// left와 right의 경로 진행률을 비교한 정렬 값을 반환한다.
        /// 빠른 드래그에서도 먼저 지나간 슬롯부터 남은 재고를 사용하도록 한다.
        /// </summary>
        private static int CompareBulkCandidates(KeyValuePair<float, AssemblyPartSlot> left,
            KeyValuePair<float, AssemblyPartSlot> right)
        {
            return left.Key.CompareTo(right.Key);
        }

        /// <summary>
        /// 일괄 배치의 미리보기와 경로 상태를 제거한다.
        /// 설치된 부품과 재고 및 UI의 선택 모드는 유지하고 입력 점유만 해제한다.
        /// </summary>
        private void EndBulkPlacement()
        {
            if (!_isBulkPlacing)
                return;

            EndPointerTracking();
            _bulkPreview.gameObject.SetActive(false);
            Destroy(_bulkPreview.gameObject);
            _catalog.HideDragIcon();
            _bulkPreview = null;
            _bulkEntry = null;
            _bulkVisited.Clear();
            _bulkCandidates.Clear();
            _hasPreviousBulkPosition = false;
            _isBulkPlacing = false;
            _isDragging = false;
        }

        /// <summary>
        /// ray로 월드 Drop 위치와 설치 대상을 계산하고 같은 실물 부품을 카메라 앞에서 추적한다.
        /// 첫 출고에서만 보관 인스턴스나 Prefab을 사용하며 교점이 없으면 마지막 월드 위치를 유지한다.
        /// </summary>
        private bool TryShowWorldPart(Ray ray)
        {
            if (_dragPlane.Raycast(ray, out float distance))
            {
                _freeWorldPosition = ray.GetPoint(distance) + _grabOffset;
                _freeWorldPosition.y = _dragHeight;
            }
            Vector3 position = _freeWorldPosition;
            if (_activePart == null)
            {
                if (!_fromInventory)
                    return false;

                AssemblyPartSlot slot = _inventoryEntry.IsSwitch ? _switchSlot : _partSlot;
                _activePart = _inventoryEntry.TakeStoredPart();
                if (_activePart == null)
                    _activePart = Instantiate(_inventoryEntry.Prefab, position, slot.transform.rotation);
                _activePart.Initialize(slot, _activePart.PartCollider, false);
                _activePart.SetCatalogEntry(_inventoryEntry);
                _activePart.PrepareInventoryPart(_inventoryEntry, _partSlot.KeycapShape);
                _activePart.transform.SetPositionAndRotation(position, slot.transform.rotation);
                _activePart.BeginDrag();
                _parts.Add(_activePart);
                _freeScale = _activePart.transform.localScale;
                _freeRotation = _activePart.transform.rotation;
                BeginPointerTracking(_activePart);
            }
            _activePart.gameObject.SetActive(true);
            _hoverSlot = FindPointerSlot(ray, _activePart);
            UpdatePointerTracking(ray);
            return true;
        }

        /// <summary>
        /// part의 실제 형상 중심과 카메라 기준 자세를 저장하고 충돌체를 잠시 비활성화한다.
        /// 별도 표시 모델을 만들지 않으며 월드 Drop 좌표와 원본 스케일을 별도로 유지한다.
        /// </summary>
        private void BeginPointerTracking(AssemblyDraggablePart part)
        {
            _trackedPart = part;
            _visualRotation = Quaternion.Inverse(_camera.transform.rotation) * _freeRotation;
            float referenceDepth = Mathf.Max(_camera.nearClipPlane + 0.1f,
                Vector3.Dot(_partSlot.transform.position - _camera.transform.position, _camera.transform.forward));
            _visualDepth = Mathf.Max(_camera.nearClipPlane + 0.1f, _pointerDepth);
            _trackingScale = _freeScale * (_camera.orthographic ? 1f : _visualDepth / referenceDepth);
            _visualProjectionSize = _camera.orthographic ? _camera.orthographicSize
                : Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            Bounds total = new Bounds();
            bool hasBounds = false;
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                bool active = true;
                for (Transform child = renderer.transform; child != part.transform; child = child.parent)
                    active &= child.gameObject.activeSelf;
                if (!active)
                    continue;
                Bounds bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = part.transform.InverseTransformPoint(renderer.transform.TransformPoint(
                        bounds.center + Vector3.Scale(bounds.extents, sign)));
                    if (hasBounds)
                        total.Encapsulate(point);
                    else
                    {
                        total = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                }
            }
            _trackingCenter = total.center;
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                _heldColliders.Add(collider, collider.enabled);
                collider.enabled = false;
            }
        }

        /// <summary>
        /// ray를 현재 카메라 전방의 일정 깊이 평면에 투영해 실물 부품의 시각 중심을 맞춘다.
        /// 투영 변화만 임시 스케일에 반영하며 슬롯 형상이나 점유 및 재고는 변경하지 않는다.
        /// </summary>
        private void UpdatePointerTracking(Ray ray)
        {
            Plane plane = new Plane(_camera.transform.forward,
                _camera.transform.position + _camera.transform.forward * _visualDepth);
            if (!plane.Raycast(ray, out float distance))
                return;
            float projectionSize = _camera.orthographic ? _camera.orthographicSize
                : Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            Transform part = _trackedPart.transform;
            part.rotation = _camera.transform.rotation * _visualRotation;
            part.localScale = _trackingScale * (projectionSize / _visualProjectionSize);
            part.position += ray.GetPoint(distance) - part.TransformPoint(_trackingCenter);
        }

        /// <summary>
        /// 실제 부품을 마지막 유효한 월드 Drop 위치와 원래 회전 및 크기로 되돌린다.
        /// 임시로 비활성화한 Collider 상태를 복구하고 추적 참조를 해제한다.
        /// </summary>
        private void EndPointerTracking()
        {
            if (_trackedPart != null)
            {
                _trackedPart.transform.SetPositionAndRotation(_freeWorldPosition, _freeRotation);
                _trackedPart.transform.localScale = _freeScale;
            }
            foreach (KeyValuePair<Collider, bool> state in _heldColliders)
                if (state.Key != null)
                    state.Key.enabled = state.Value;
            _heldColliders.Clear();
            _trackedPart = null;
        }

        /// <summary>
        /// inReturnArea와 overUI에 따라 반환, 취소 또는 기존 월드 Drop을 확정한다.
        /// 실물과 예약을 한 번만 처리하고 아이콘과 드래그 상태를 해제한다.
        /// </summary>
        private void CompleteDrag(bool inReturnArea, bool overUI)
        {
            EndPointerTracking();
            _isDragging = false;
            if (_inventoryEntry != null && inReturnArea)
                ReturnHeldPart();
            else if (overUI || _showingIcon)
                RestoreHeldPart();
            else if (_activePart != null)
            {
                AssemblyPartSlot target = _hoverSlot != null && _hoverSlot.CanInstall(_activePart) ? _hoverSlot : null;
                if (target != null)
                    _activePart.AttachSlot(target);
                _activePart.EndDrag(target != null);
                if (_fromInventory)
                    _catalog.FinishReservation(_inventoryEntry, true);
            }
            else if (_fromInventory)
                _catalog.FinishReservation(_inventoryEntry, false);
            ClearDrag();
        }

        /// <summary>
        /// ray가 통과하는 가장 가까운 슬롯 영역과 part의 장착 조건을 검사한다.
        /// 도구와 들고 있는 부품을 제외한 장애물에 가리거나 유효하지 않으면 null을 반환한다.
        /// </summary>
        private AssemblyPartSlot FindPointerSlot(Ray ray, AssemblyDraggablePart part)
        {
            Physics.SyncTransforms();
            AssemblyPartSlot nearest = null;
            float distance = float.PositiveInfinity;
            foreach (AssemblyPartSlot slot in _slots)
            {
                if (slot.IsSwitch != part.IsSwitch || !slot.TryGetPointerHit(ray, out float hitDistance))
                    continue;
                if (hitDistance < distance)
                {
                    distance = hitDistance;
                    nearest = slot;
                }
            }
            if (nearest == null || !nearest.CanInstall(part))
                return null;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(part.transform) || IsToolCollider(hit.collider))
                    continue;
                if (hit.distance < distance - 0.001f)
                    return null;
            }
            return nearest;
        }

        /// <summary>
        /// collider가 연결된 Puller나 복귀 표시의 충돌체인지 반환한다.
        /// 표시용 도구를 슬롯과 표면 레이캐스트에서 제외한다.
        /// </summary>
        public bool IsToolCollider(Collider collider)
        {
            return (_fullerTool != null && _fullerTool.OwnsCollider(collider))
                || (_switchPullerTool != null && _switchPullerTool.OwnsCollider(collider));
        }

        /// <summary>
        /// 들고 있는 부품을 연결된 항목의 재고로 반환한다.
        /// UI에서 시작했으면 예약을 취소하고 월드에서 시작했으면 한 개를 늘리며 선택 등록을 해제한다.
        /// </summary>
        private void ReturnHeldPart()
        {
            if (_activePart != null)
                _parts.Remove(_activePart);
            _inventoryEntry.StorePart(_activePart, !_fromInventory);
            if (_fromInventory)
                _catalog.FinishReservation(_inventoryEntry, false);
            else
                _catalog.RefreshInventory();
        }

        /// <summary>
        /// UI로 표시 중인 부품을 드래그 시작 전 상태로 복원한다.
        /// UI에서 시작했으면 예약과 실물을 회수하고 월드에서 시작했으면 원래 물리 상태를 복원한다.
        /// </summary>
        private void RestoreHeldPart()
        {
            if (_fromInventory)
            {
                if (_activePart != null)
                    _parts.Remove(_activePart);
                _inventoryEntry.StorePart(_activePart, false);
                _catalog.FinishReservation(_inventoryEntry, false);
            }
            else if (_activePart != null)
                _activePart.RestoreDragOrigin();
        }

        /// <summary>
        /// 종료된 드래그의 임시 참조와 아이콘을 해제한다.
        /// 별도 입력 없이 정리하며 확정된 재고 수량과 슬롯 상태는 변경하지 않는다.
        /// </summary>
        private void ClearDrag()
        {
            EndPointerTracking();
            if (_catalog != null)
                _catalog.HideDragIcon();
            _activePart = null;
            _hoverSlot = null;
            _inventoryEntry = null;
            _fromInventory = false;
            _showingIcon = false;
        }

        /// <summary>
        /// screenPosition에서 현재 UI를 직접 레이캐스트하여 UI 입력 여부를 반환한다.
        /// EventSystem 갱신 순서와 무관하게 UI를 누를 때 뒤쪽 키캡의 집기 시작을 막는다.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            EventSystem events = EventSystem.current;
            if (events == null)
                return false;

            if (_pointerEvent == null || _pointerEventSystem != events)
            {
                _pointerEventSystem = events;
                _pointerEvent = new PointerEventData(events);
            }
            _pointerEvent.Reset();
            _pointerEvent.position = screenPosition;
            _uiHits.Clear();
            events.RaycastAll(_pointerEvent, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.module is GraphicRaycaster)
                    return true;
            return false;
        }

        void OnDisable()
        {
            CancelDrag();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                CancelDrag();
        }

        /// <summary>
        /// ray가 등록된 부품의 충돌체에 닿으면 슬롯의 월드 Y좌표와 heightOffset으로 이동 평면을 설정한다.
        /// 상승한 위치와 평면 교점의 오프셋을 저장하여 XZ 위치를 유지하고 드래그 상태로 변경한다.
        /// </summary>
        private void TryBeginDrag(Ray ray)
        {
            Physics.SyncTransforms();
            AssemblyDraggablePart picked = null;
            float nearest = _camera.farClipPlane;
            for (int index = _parts.Count - 1; index >= 0; index--)
            {
                AssemblyDraggablePart part = _parts[index];
                if (part == null)
                {
                    _parts.RemoveAt(index);
                    continue;
                }
                if (part.gameObject.activeInHierarchy && part.PartCollider.Raycast(ray, out RaycastHit hit, nearest))
                {
                    nearest = hit.distance;
                    picked = part;
                }
            }
            if (picked == null || (picked.Slot != null && picked.Slot.InstalledPart == picked && !picked.Slot.CanRemove()))
                return;

            _dragHeight = _partSlot.transform.position.y + _heightOffset;
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, _dragHeight, 0f));
            if (!_dragPlane.Raycast(ray, out float distance))
                return;

            Vector3 liftedPosition = picked.transform.position;
            liftedPosition.y = _dragHeight;
            _grabOffset = liftedPosition - ray.GetPoint(distance);
            _grabOffset.y = 0f;
            picked.BeginDrag();
            _freeScale = picked.transform.localScale;
            _freeRotation = picked.transform.rotation;
            _freeWorldPosition = liftedPosition;
            BeginPointerTracking(picked);
            UpdatePointerTracking(ray);
            _activePart = picked;
            _inventoryEntry = picked.CatalogEntry;
            _fromInventory = false;
            _showingIcon = false;
            _isDragging = true;
            PartPicked?.Invoke(picked);
        }

        /// <summary>
        /// 입력 포커스나 컴포넌트가 해제되면 진행 중인 드래그를 종료한다.
        /// UI 표시 중에는 시작 상태로 복구하고 월드 표시 중에는 기존 복귀 및 낙하 정책과 예약을 확정한다.
        /// </summary>
        public void CancelDrag()
        {
            if (_fullerTool != null)
                _fullerTool.ReturnToOrigin();
            if (_switchPullerTool != null)
                _switchPullerTool.ReturnToOrigin();
            if (!_isDragging)
                return;

            if (_isBulkPlacing)
            {
                EndBulkPlacement();
                return;
            }

            _isDragging = false;
            EndPointerTracking();
            if (_showingIcon)
                RestoreHeldPart();
            else
            {
                if (_activePart != null)
                    _activePart.EndDrag(false);
                if (_fromInventory)
                    _catalog.FinishReservation(_inventoryEntry, _activePart != null);
            }
            ClearDrag();
        }
    }
}
