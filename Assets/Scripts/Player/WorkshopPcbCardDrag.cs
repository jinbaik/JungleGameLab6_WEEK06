using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Logging;

using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    [DefaultExecutionOrder(-50)]
    public sealed partial class WorkshopPcbCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("PCB·보강판·상단부 Drag")]
        private WorkshopKeyboardPullerController _pullers;
        private KeyboardEditOverlayUI _overlay;
        private KeyboardAssemblyState _assembly;
        private readonly List<WorkshopKeyboardPlacementPreview> _outlines = new List<WorkshopKeyboardPlacementPreview>();
        private AssemblyStructurePart _kind;
        private IReadOnlyList<Transform> _parts;
        private Vector3[] _partOffsets;
        private Material _outlineMaterial;
        private Shader _depthShader;
        private Material _iconMaterial;
        private WorkshopDragPreviewRenderer _preview;
        private KeyboardEditButton _button;
        private RawImage _icon;
        private RawImage _returnIcon;
        private RectTransform _returnTarget;
        private Texture2D _texture;
        private Plane _dragPlane;
        private Vector3 _centerOffset;
        private bool _selected;
        private bool _dragging;
        private bool _returning;
        private bool _worldDragging;
        private bool _worldPickupEnabled;
        private bool _wasInstalled;
        private readonly List<Rigidbody> _looseBodies = new List<Rigidbody>();
        private Vector3[] _worldStartPositions;
        private Quaternion[] _worldStartRotations;
        private readonly List<(Collider Collider, bool Enabled)> _colliders = new List<(Collider, bool)>();
        private readonly Dictionary<MeshFilter, MeshCollider> _upperSurfaceColliders = new Dictionary<MeshFilter, MeshCollider>();

        /// <summary>pullers와 kind 및 기존 아웃라인 에셋을 카드에 연결한다. 트레이로 이동하기 전 소유 UI 참조를 보관하고 아이콘을 만들며 설치 상태는 유지한다.</summary>
        public void Initialize(WorkshopKeyboardPullerController pullers, Material outlineMaterial, Shader depthShader,
            AssemblyStructurePart kind = AssemblyStructurePart.Pcb, bool showCard = true, bool worldPickupEnabled = true)
        {
            _pullers = pullers;
            _overlay = GetComponentInParent<KeyboardEditOverlayUI>();
            _button = GetComponent<KeyboardEditButton>();
            _kind = kind;
            _worldPickupEnabled = worldPickupEnabled;
            _outlineMaterial = outlineMaterial;
            _depthShader = depthShader;
            _preview = gameObject.AddComponent<WorkshopDragPreviewRenderer>();
            _preview.Initialize(pullers.InputCamera);
            _icon = new GameObject("PcbIcon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _icon.transform.SetParent(transform, false);
            _icon.rectTransform.anchorMin = _icon.rectTransform.anchorMax = _icon.rectTransform.pivot = Vector2.up;
            _icon.rectTransform.anchoredPosition = new Vector2(13f, -6f);
            _icon.rectTransform.sizeDelta = new Vector2(105f, 105f);
            _icon.raycastTarget = false;
            _icon.gameObject.SetActive(showCard);
            _returnTarget = _icon.rectTransform;
            _returnIcon = new GameObject("PcbReturnIcon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _returnIcon.transform.SetParent(GetComponentInParent<Canvas>().transform, false);
            _returnIcon.rectTransform.sizeDelta = new Vector2(105f, 105f);
            _returnIcon.raycastTarget = false;
            _returnIcon.gameObject.SetActive(false);
        }

        /// <summary>assembly와 selected로 현재 카드와 탭을 연결한다. 대상이 바뀌면 원본 메시로 아이콘과 아웃라인을 다시 준비하고 기존 드래그를 취소한다.</summary>
        public void SetContext(KeyboardAssemblyState assembly, bool selected)
        {
            if (_assembly != assembly)
            {
                CancelDrag();
                _looseBodies.Clear();
                ClearUpperSurfaceColliders();
                foreach (var outline in _outlines) outline.Dispose();
                _outlines.Clear();
                _assembly = assembly;
                if (assembly != null)
                {
                    _parts = assembly.GetStructureParts(_kind);
                    foreach (Transform part in _parts)
                    {
                        var outline = new WorkshopKeyboardPlacementPreview(_outlineMaterial, _depthShader, _pullers.InputCamera);
                        outline.Prepare(part, true, _kind == AssemblyStructurePart.Pcb
                            ? part.Find("PCB_Board").GetComponent<MeshRenderer>() : null);
                        _outlines.Add(outline);
                    }
                    BuildIcon();
                }
            }
            _selected = selected;
            if (!selected && !_worldDragging) CancelDrag();
            if (assembly != null && !_dragging && !_returning && _iconMaterial != assembly.GetStructureMaterial(_kind)) BuildIcon();
            _button.interactable = selected && assembly != null && assembly.CanInstallStructure(_kind)
                && !(_kind == AssemblyStructurePart.Pcb && assembly.PcbLoose
                    || _kind == AssemblyStructurePart.Lower && assembly.LowerLoose) && !_returning;
        }

        /// <summary>현재 PCB·보강판·상단부의 모든 렌더 메시와 재질을 카탈로그 아이콘으로 합친다. 런타임 미리보기만 생성하며 메시 에셋과 원본 모델은 유지한다.</summary>
        private void BuildIcon()
        {
            if (_texture != null) Destroy(_texture);
            _texture = AssemblyCatalogPreview.RenderParts(_parts);
            _icon.texture = _returnIcon.texture = _texture != null ? _texture : Texture2D.whiteTexture;
            _iconMaterial = _assembly.GetStructureMaterial(_kind);
        }

        void OnEnable() { InGameCursor.PointerCanceled += CancelDrag; }

        void Update()
        {
            if (_stockEnabled && ProcessStockDrag()) return;
            if (!_dragging && _worldPickupEnabled) TryBeginWorldDrag();
            if (!_dragging) return;
            if (_worldDragging) { UpdateWorldDrag(); return; }
            if (!_selected || !_pullers.InputAllowed || Mouse.current == null
                || !_assembly.CanInstallStructure(_kind) || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelDrag();
                return;
            }
            Vector2 point = InGameCursor.Position;
            Ray ray = _pullers.InputCamera.ScreenPointToRay(point);
            MoveParts(point);
            if (Mouse.current.leftButton.isPressed) return;
            if (!Mouse.current.leftButton.wasReleasedThisFrame) { CancelDrag(); return; }
            bool overUi = _pullers.IsPointerOverUI(point);
            bool blocked = !overUi && IsObstructed(ray);
            bool valid = !overUi && !blocked && _assembly.TryPlaceStructure(_kind, ray);
            if (valid)
            {
                RecordStructureEvent("PartInstallAttempted", true, "InventoryDrag");
                FinishDrag();
                AudioManager.PlayPartSound(_parts[0].position, false);
            }
            else if (_kind == AssemblyStructurePart.Lower && !_pullers.IsPointerOverUI(point))
            {
                _assembly.DropLooseLower();
                _wasInstalled = false;
                _worldDragging = true;
                UpdateWorldDrag();
            }
            else
            {
                RecordStructureEvent(overUi ? "StructureDragCancelled" : "PartInstallAttempted",
                    false, "InventoryDrag", overUi ? "Cancelled" : GetPlacementFailureReason(blocked));
                StartReturn(point);
            }
        }

        /// <summary>가상 커서 아래의 설치 또는 바닥 구조 부품을 집는다. 분리 조건을 확인한 뒤 실제 성공 시에만 점유를 해제한다.</summary>
        private void TryBeginWorldDrag()
        {
            if (_stockEnabled && _kind == AssemblyStructurePart.Lower) return;
            if (_assembly == null || _returning || !_pullers.InputAllowed || _pullers.IsManipulatingPart
                || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            Vector2 point = InGameCursor.Position;
            if (_pullers.IsPointerOverUI(point) || _pullers.IsPullerInteraction(point)) return;
            Ray ray = _pullers.InputCamera.ScreenPointToRay(point);
            Physics.SyncTransforms();
            Vector3 boardPoint = default;
            bool boardHit = _kind == AssemblyStructurePart.Pcb
                && _assembly.TryGetInstalledPcbHit(ray, out boardPoint);
            Vector3 hitPoint = boardHit ? boardPoint : Vector3.zero;
            float nearest = float.PositiveInfinity;
            if (boardHit) nearest = Vector3.Distance(ray.origin, boardPoint);
            if (_kind == AssemblyStructurePart.Upper && _assembly.UpperInstalled)
            {
                if (!TryGetUpperSurfaceHit(ray, out hitPoint, out nearest)) return;
            }
            else if (_kind == AssemblyStructurePart.Plate && _assembly.PlateInstalled
                || _kind == AssemblyStructurePart.Lower && _assembly.LowerInstalled)
            {
                foreach (Transform part in _parts)
                {
                    MeshFilter filter = part.GetComponent<MeshFilter>();
                    Ray localRay = new Ray(part.InverseTransformPoint(ray.origin), part.InverseTransformVector(ray.direction));
                    Bounds bounds = filter.sharedMesh.bounds;
                    bounds.Expand(new Vector3(0f, 0.02f, 0f));
                    if (!bounds.IntersectRay(localRay, out float distance)) continue;
                    Vector3 pointOnPart = part.TransformPoint(localRay.GetPoint(distance));
                    float worldDistance = Vector3.Distance(ray.origin, pointOnPart);
                    if (worldDistance >= nearest) continue;
                    hitPoint = pointOnPart;
                    nearest = worldDistance;
                }
            }
            float obstruction = float.PositiveInfinity;
            KeyboardCustomization customization = _kind == AssemblyStructurePart.Pcb
                ? _assembly.GetComponent<KeyboardCustomization>() : null;
            foreach (RaycastHit candidate in Physics.RaycastAll(ray, _pullers.InputCamera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                bool belongs = false;
                foreach (Transform part in _parts)
                    if (candidate.transform == part || candidate.transform.IsChildOf(part)) { belongs = true; break; }
                if (_kind == AssemblyStructurePart.Upper)
                {
                    if (!belongs) obstruction = Mathf.Min(obstruction, candidate.distance);
                    continue;
                }
                if (!candidate.transform.IsChildOf(_assembly.transform) && !belongs)
                    obstruction = Mathf.Min(obstruction, candidate.distance);
                if (!belongs && customization != null && _assembly.CanRemovePcb)
                    for (int index = 0; index < customization.KeycapCount && !belongs; index++)
                        belongs = customization.IsPcbKeycapInstalled(index)
                            && candidate.transform.IsChildOf(customization.GetKeycapSlotTransform(index))
                            || customization.IsPcbSwitchInstalled(index)
                            && candidate.transform.IsChildOf(customization.GetSwitchSlotTransform(index));
                if (!belongs || candidate.distance >= nearest) continue;
                hitPoint = candidate.point;
                nearest = candidate.distance;
            }
            if (nearest >= obstruction) return;
            if (_kind == AssemblyStructurePart.Plate && _assembly.PlateInstalled && !_assembly.CanRemovePlate)
            {
                RecordStructureEvent("PartRemoveAttempted", false, "WorldDrag", "PrerequisiteMissing");
                Debug.LogWarning("보강판 분리 불가: 상단부 또는 설치된 키캡이 남아 있습니다.", _assembly);
                return;
            }
            _wasInstalled = _kind == AssemblyStructurePart.Pcb ? _assembly.PcbPlaced
                : _kind == AssemblyStructurePart.Upper ? _assembly.UpperInstalled
                : _kind == AssemblyStructurePart.Lower ? _assembly.LowerInstalled : _assembly.PlateInstalled;
            if (_wasInstalled && !(_kind == AssemblyStructurePart.Pcb
                ? _assembly.TryRemovePcb() : _assembly.TryRemoveStructure(_kind)))
            {
                RecordStructureEvent("PartRemoveAttempted", false, "WorldDrag", "PrerequisiteMissing");
                return;
            }
            _worldDragging = true;
            _dragging = true;
            _pullers.SetPcbDragging(true);
            foreach (Rigidbody body in _looseBodies) if (body != null) body.isKinematic = true;
            _partOffsets = new Vector3[_parts.Count];
            _worldStartPositions = new Vector3[_parts.Count];
            _worldStartRotations = new Quaternion[_parts.Count];
            for (int index = 0; index < _parts.Count; index++)
            {
                _partOffsets[index] = _parts[index].position - _parts[0].position;
                _worldStartPositions[index] = _parts[index].position;
                _worldStartRotations[index] = _parts[index].rotation;
            }
            _centerOffset = _parts[0].position - hitPoint;
            _dragPlane = new Plane(_pullers.InputCamera.transform.forward, hitPoint);
            _colliders.Clear();
            foreach (Transform part in _parts)
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                _colliders.Add((collider, collider.enabled));
                collider.enabled = false;
            }
            _preview.Show(_parts);
            MoveParts(point);
        }

        /// <summary>ray와 상부 하우징의 실제 메시 표면을 검사한다. 가장 가까운 표면 좌표와 거리를 반환해 빈 구멍 선택을 막는다.</summary>
        private bool TryGetUpperSurfaceHit(Ray ray, out Vector3 hitPoint, out float hitDistance)
        {
            hitPoint = Vector3.zero;
            hitDistance = float.PositiveInfinity;
            foreach (Transform part in _parts)
            foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                MeshCollider collider = GetUpperSurfaceCollider(filter);
                Transform meshTransform = filter.transform;
                collider.transform.SetPositionAndRotation(meshTransform.position, meshTransform.rotation);
                collider.transform.localScale = meshTransform.lossyScale;
                collider.gameObject.SetActive(true);
                try
                {
                    Physics.SyncTransforms();
                    if (!collider.Raycast(ray, out RaycastHit meshHit, _pullers.InputCamera.farClipPlane)) continue;
                    float distance = Vector3.Distance(ray.origin, meshHit.point);
                    if (distance >= hitDistance) continue;
                    hitPoint = meshHit.point;
                    hitDistance = distance;
                }
                finally
                {
                    collider.gameObject.SetActive(false);
                }
            }
            return !float.IsPositiveInfinity(hitDistance);
        }

        /// <summary>filter의 실제 공유 메시를 사용하는 비활성 판정 콜라이더를 반환한다. 메시 가독성 설정과 무관하게 형상을 재사용한다.</summary>
        private MeshCollider GetUpperSurfaceCollider(MeshFilter filter)
        {
            if (_upperSurfaceColliders.TryGetValue(filter, out MeshCollider collider) && collider != null) return collider;
            GameObject proxy = new GameObject("UpperHousingRaycastProxy");
            proxy.hideFlags = HideFlags.HideAndDontSave;
            proxy.layer = 2;
            proxy.SetActive(false);
            collider = proxy.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.isTrigger = true;
            _upperSurfaceColliders[filter] = collider;
            return collider;
        }

        /// <summary>현재 편집 대상에 만든 임시 표면 콜라이더를 제거한다. 실제 부품과 재질 및 원래 콜라이더는 변경하지 않는다.</summary>
        private void ClearUpperSurfaceColliders()
        {
            foreach (MeshCollider collider in _upperSurfaceColliders.Values)
                if (collider != null) Destroy(collider.gameObject);
            _upperSurfaceColliders.Clear();
        }

        /// <summary>가상 커서를 따라 실물을 이동하고 버튼을 놓을 때 목록 반환·설치·바닥 드롭 중 하나를 확정한다.</summary>
        private void UpdateWorldDrag()
        {
            if (!_pullers.InputAllowed || Mouse.current == null || Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame) { CancelDrag(); return; }
            Vector2 point = InGameCursor.Position;
            Ray ray = _pullers.InputCamera.ScreenPointToRay(point);
            MoveParts(point);
            if (Mouse.current.leftButton.isPressed) return;
            if (!Mouse.current.leftButton.wasReleasedThisFrame) { CancelDrag(); return; }
            KeyboardEditOverlayUI overlay = _overlay;
            bool returnedToTray = overlay != null && (_kind == AssemblyStructurePart.Upper
                ? _stockTray != null && _stockTray.IsPointerOverOpenTrayViewport(point)
                : _stockEnabled ? overlay.IsInStructureTray(point, _kind) : overlay.IsInAssemblyReturnArea(point));
            if (returnedToTray)
            {
                if (_kind == AssemblyStructurePart.Pcb && !overlay.TryReturnPcbBundle(_assembly))
                {
                    RecordStructureEvent("PartReturned", false, "InventoryReturn", "InventoryReturnFailed");
                    CancelDrag();
                    return;
                }
                FinishWorldDrag();
                _assembly.ReturnStructureInventory(_kind);
                if (_kind == AssemblyStructurePart.Pcb) _assembly.ReleasePcbParts();
                if (_kind == AssemblyStructurePart.Pcb) _assembly.ReturnPcbToInventory();
                else if (_kind == AssemblyStructurePart.Lower) _assembly.ReturnLowerToInventory();
                else _assembly.RestoreStructurePose(_kind);
                if (_wasInstalled) RecordStructureEvent("PartRemoveAttempted", true, "InventoryReturn");
                RecordStructureEvent("PartReturned", true, "InventoryReturn");
                return;
            }
            if (_pullers.IsPointerOverUI(point)) { CancelDrag(); return; }
            bool blocked = IsObstructed(ray);
            bool installed = !blocked && _assembly.TryPlaceStructure(_kind, ray);
            RecordStructureEvent("PartInstallAttempted", installed, "WorldDrag",
                installed ? "" : GetPlacementFailureReason(blocked), _wasInstalled && installed ? "Reinstalled" : "");
            if (!installed && _kind == AssemblyStructurePart.Upper)
            {
                CancelDrag();
                return;
            }
            Vector3 position = _parts[0].position;
            FinishWorldDrag();
            if (installed) { AudioManager.PlayPartSound(position, false); return; }
            DropWorldParts(ray);
            if (_wasInstalled) RecordStructureEvent("PartRemoveAttempted", true, "WorldDrag", details: "DroppedToWorld");
        }

        /// <summary>ray의 기존 작업대 평면에 구조 부품을 내려놓고 중력·충돌을 복구한다. 원래 부품 참조와 재질 및 낙하 물리 경로를 유지한다.</summary>
        private void DropWorldParts(Ray ray)
        {
            if (!new Plane(Vector3.up, _assembly.transform.position).Raycast(ray, out float distance))
                distance = Mathf.Max(1f, Vector3.Distance(_pullers.InputCamera.transform.position, _assembly.transform.position));
            Vector3 drop = ray.GetPoint(distance) + Vector3.up * 0.3f;
            for (int index = 0; index < _parts.Count; index++)
            {
                Transform part = _parts[index];
                part.position = drop + _partOffsets[index];
                Rigidbody body = part.GetComponent<Rigidbody>();
                if (body == null) body = part.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = true;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (!_looseBodies.Contains(body)) _looseBodies.Add(body);
            }
        }

        /// <summary>실물 드래그의 표시와 충돌체를 복구한다. 설치 여부는 변경하지 않고 입력 점유만 해제한다.</summary>
        private void FinishWorldDrag()
        {
            _preview.Hide();
            foreach (var entry in _colliders) entry.Collider.enabled = entry.Enabled;
            _colliders.Clear();
            _dragging = false;
            _worldDragging = false;
            _pullers.SetPcbDragging(false);
        }

        void LateUpdate()
        {
            if (!_stockDragging && _selected && _assembly != null && _assembly.CanInstallStructure(_kind) && _pullers.InputAllowed)
                for (int index = 0; index < _outlines.Count; index++)
                    _outlines[index].Show(_assembly.GetStructurePose(_kind, index));
        }

        /// <summary>ray의 첫 충돌이 편집 키보드 밖의 물체인지 반환한다. 드래그 PCB·보강판·상단부 충돌체는 제외하며 설치 상태는 변경하지 않는다.</summary>
        private bool IsObstructed(Ray ray)
        {
            return Physics.Raycast(ray, out RaycastHit hit, _pullers.InputCamera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(_assembly.transform);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !_selected || _returning
                || _dragging
                || _assembly == null || !_assembly.CanInstallStructure(_kind)
                || (_kind == AssemblyStructurePart.Pcb && _assembly.PcbLoose
                    || _kind == AssemblyStructurePart.Lower && _assembly.LowerLoose) || !_pullers.InputAllowed
                || _pullers.IsManipulatingPart || Mouse.current == null || !Mouse.current.leftButton.isPressed) return;
            eventData.eligibleForClick = false;
            _pullers.CancelDrag();
            _pullers.SetPcbDragging(true);
            _dragging = true;
            foreach (Transform part in _parts) part.gameObject.SetActive(true);
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Transform part in _parts)
            foreach (MeshRenderer renderer in part.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            _centerOffset = _parts[0].position - bounds.center;
            _partOffsets = new Vector3[_parts.Count];
            for (int index = 0; index < _parts.Count; index++) _partOffsets[index] = _parts[index].position - _parts[0].position;
            _dragPlane = new Plane(_pullers.InputCamera.transform.forward, bounds.center);
            _colliders.Clear();
            foreach (Transform part in _parts)
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                _colliders.Add((collider, collider.enabled));
                collider.enabled = false;
            }
            _preview.Show(_parts);
            MoveParts(InGameCursor.Position);
        }

        /// <summary>material을 선택하고 origin 재질 카드에서 기존 실물의 드래그를 시작한다. 설치 불가·중복 입력은 거부하며 반환 아이콘 목적지만 해당 카드로 바꾼다.</summary>
        public bool BeginMaterialDrag(PointerEventData eventData, Material material, RectTransform origin)
        {
            if (!_selected || _dragging || _returning || _assembly == null || !_assembly.CanInstallStructure(_kind)
                || !_pullers.InputAllowed || _pullers.IsManipulatingPart || Mouse.current == null
                || !Mouse.current.leftButton.isPressed || eventData.button != PointerEventData.InputButton.Left) return false;
            if (!_assembly.SetStructureMaterial(_kind, material)) return false;
            _returnTarget = origin;
            BuildIcon();
            OnBeginDrag(eventData);
            return _dragging;
        }

        /// <summary>point의 공통 커서 좌표를 카메라 평행 평면에 투영해 기존 실물을 함께 이동한다. 부품 사이 오프셋과 크기·회전 및 계층은 유지한다.</summary>
        private void MoveParts(Vector2 point)
        {
            Ray ray = _pullers.InputCamera.ScreenPointToRay(point);
            if (!_dragPlane.Raycast(ray, out float distance)) return;
            Vector3 position = ray.GetPoint(distance) + _centerOffset;
            IReadOnlyList<Transform> parts = _stockDragging ? _stockParts : _parts;
            for (int index = 0; index < parts.Count; index++) parts[index].position = position + _partOffsets[index];
        }

        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }

        /// <summary>공통 커서의 point에서 실패한 드래그를 반환한다. PCB·보강판·상단부 상태와 원래 자세를 복원하고 기존 아이콘 비행 방식으로 카드에 돌려보낸다.</summary>
        private void StartReturn(Vector2 point)
        {
            FinishDrag();
            _returning = true;
            StartCoroutine(ReturnIcon(point));
        }

        /// <summary>point에서 카드 중심까지 반환 아이콘을 이동한다. deltaTime으로 진행하며 완료 후 표시와 반환 중 상태를 해제한다.</summary>
        private IEnumerator ReturnIcon(Vector2 point)
        {
            _returnIcon.gameObject.SetActive(true);
            RectTransform canvas = (RectTransform)_returnIcon.transform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, point, null, out Vector2 start);
            float elapsed = 0f;
            while (elapsed < .25f)
            {
                elapsed += Time.deltaTime;
                Vector2 destination = RectTransformUtility.WorldToScreenPoint(null, _returnTarget.TransformPoint(_returnTarget.rect.center));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, destination, null, out Vector2 end);
                _returnIcon.rectTransform.localPosition = Vector2.Lerp(start, end, Mathf.Clamp01(elapsed / .25f));
                yield return null;
            }
            _returnIcon.gameObject.SetActive(false);
            _returning = false;
        }

        /// <summary>현재 PCB·보강판·상단부 드래그의 렌더 레이어와 충돌체 및 원래 자세를 복원한다. 배치 플래그는 변경하지 않고 입력 점유를 해제한다.</summary>
        private void FinishDrag()
        {
            _preview.Hide();
            foreach (var entry in _colliders) entry.Collider.enabled = entry.Enabled;
            _colliders.Clear();
            _assembly.RestoreStructurePose(_kind);
            _dragging = false;
            _pullers.SetPcbDragging(false);
        }

        /// <summary>입력 중단이나 탭 전환 시 PCB·보강판·상단부를 원래 미설치 위치로 반환한다. 진행 중인 반환 표시도 정리하며 설치 상태는 유지한다.</summary>
        private void CancelDrag()
        {
            if (_stockDragging) { CancelStockDrag(); return; }
            if (_worldDragging)
            {
                RecordStructureEvent("StructureDragCancelled", false, "WorldDrag", "Cancelled");
                FinishWorldDrag();
                if (_wasInstalled)
                {
                    if (_kind == AssemblyStructurePart.Pcb) _assembly.RestoreRemovedPcb();
                    else _assembly.RestoreRemovedStructure(_kind);
                }
                else for (int index = 0; index < _parts.Count; index++)
                {
                    _parts[index].SetPositionAndRotation(_worldStartPositions[index], _worldStartRotations[index]);
                    Rigidbody body = _parts[index].GetComponent<Rigidbody>();
                    if (body != null) { body.isKinematic = false; body.useGravity = true; }
                }
                return;
            }
            if (_dragging)
            {
                RecordStructureEvent("StructureDragCancelled", false, "InventoryDrag", "Cancelled");
                FinishDrag();
            }
            StopAllCoroutines();
            _returning = false;
            if (_returnIcon != null) _returnIcon.gameObject.SetActive(false);
        }

        /// <summary>확정된 kind의 조립 결과를 제작 로그에 기록한다. type과 성공 여부 및 실패 이유를 사용하며 조립 상태는 변경하지 않는다.</summary>
        private void RecordStructureEvent(string type, bool success, string source, string reason = "", string details = "")
        {
            CustomizationTelemetry.Emit(_assembly.GetComponent<KeyboardCustomization>(), type, _kind.ToString(),
                _assembly.GetStructureMaterial(_kind)?.name ?? "", success: success,
                changed: success && type != "StructureDragCancelled", details: details,
                materialId: _assembly.GetStructureMaterial(_kind)?.name ?? "",
                targetId: _kind.ToString(), actionSource: source, outcomeReason: reason);
        }

        /// <summary>blocked와 조립 선행 조건을 확인해 배치 실패 코드를 반환한다. 조립 상태는 변경하지 않는다.</summary>
        private string GetPlacementFailureReason(bool blocked)
        {
            return blocked ? "Blocked" : _assembly.CanInstallStructure(_kind) ? "PlacementRejected" : "PrerequisiteMissing";
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= CancelDrag;
            CancelDrag();
        }

        void OnDestroy()
        {
            CancelDrag();
            ClearUpperSurfaceColliders();
            foreach (var outline in _outlines) outline.Dispose();
            if (_texture != null) Destroy(_texture);
            if (_returnIcon != null) Destroy(_returnIcon.gameObject);
        }
    }
}
