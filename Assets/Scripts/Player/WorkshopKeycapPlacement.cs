using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Tournament;
using KeyboardModeling;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    [DisallowMultipleComponent]
    public sealed class WorkshopKeycapPlacement : MonoBehaviour
    {
        private const float RETURN_DELAY = 2f;

        [Header("Workshop")]
        [SerializeField] private PlayerInteractionController _interaction;
        [SerializeField] private WorkshopKeyboardPullerController _pullers;

        [Header("Keycap Inventory")]
        [SerializeField] private AssemblyCatalogEntry[] _keycapEntries = new AssemblyCatalogEntry[0];

        [Header("Switch Inventory")]
        [SerializeField] private AssemblyCatalogEntry[] _switchEntries = new AssemblyCatalogEntry[0];
        private readonly Dictionary<AssemblyCatalogEntry, Text> _quantityLabels = new Dictionary<AssemblyCatalogEntry, Text>();
        private readonly Dictionary<AssemblyCatalogEntry, Texture> _entryTextures = new Dictionary<AssemblyCatalogEntry, Texture>();
        private readonly List<AssemblyDraggablePart> _ownedParts = new List<AssemblyDraggablePart>();
        private readonly List<AssemblyDraggablePart> _looseParts = new List<AssemblyDraggablePart>();
        private AssemblyDraggablePart _activePart;
        private Transform _dragOriginPart;
        private AssemblyCatalogEntry _entry;
        private bool _fromInventory;
        private Plane _dragPlane;
        private RectTransform _returnArea;
        private RectTransform _switchReturnArea;
        private RectTransform _canvasRect;
        private RawImage _dragIcon;
        private WorkshopDragPreviewRenderer _previewRenderer;
        private readonly List<KeyboardEditButton> _singlePlacementButtons = new List<KeyboardEditButton>();
        private readonly List<KeyboardEditButton> _bulkPlacementButtons = new List<KeyboardEditButton>();
        private bool _bulkPlacement;
        private bool _isBulkDragging;
        private bool _hasPreviousBulkPosition;
        private Vector2 _previousBulkPosition;
        private readonly HashSet<int> _bulkVisited = new HashSet<int>();
        private readonly List<KeyValuePair<float, int>> _bulkCandidates = new List<KeyValuePair<float, int>>();
        private int _lastBulkFrame = -1;
        private int _lastRemovalFrame = -1;
        private readonly Dictionary<AssemblyDraggablePart, int> _extractedReturns = new Dictionary<AssemblyDraggablePart, int>();
        private Camera InputCamera => _pullers.InputCamera;
        public bool IsDragging => _entry != null;
        public bool IsEditing => _interaction.IsKeyboardEditing;
        public Camera ReturnCamera => InputCamera;
        public IReadOnlyList<AssemblyDraggablePart> LooseParts => _looseParts;

        void Awake()
        {
            foreach (AssemblyCatalogEntry entry in _keycapEntries) entry.InitializeInventory();
            foreach (AssemblyCatalogEntry entry in _switchEntries) entry.InitializeInventory();
        }

        void Start()
        {
            GetComponent<KeyboardEditOverlayUI>().ConnectKeycapPlacement(this);
            _canvasRect = (RectTransform)GetComponentInParent<Canvas>().transform;
            _dragIcon = new GameObject("WorkshopKeycapDragIcon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _dragIcon.transform.SetParent(_canvasRect, false);
            _dragIcon.rectTransform.sizeDelta = new Vector2(80f, 80f);
            _dragIcon.raycastTarget = false;
            _dragIcon.gameObject.SetActive(false);
            _previewRenderer = gameObject.AddComponent<WorkshopDragPreviewRenderer>();
            _previewRenderer.Initialize(InputCamera);
        }

        void OnEnable()
        {
            InGameCursor.PointerCanceled += CancelDrag;
        }

        void Update()
        {
            foreach (KeyboardEditButton button in _singlePlacementButtons) button.interactable = !IsDragging;
            foreach (KeyboardEditButton button in _bulkPlacementButtons) button.interactable = !IsDragging;
            if (!_pullers.InputAllowed || Mouse.current == null)
            {
                CancelDrag();
                return;
            }
            Vector2 point = InGameCursor.Position;
            if (!IsDragging && _lastRemovalFrame != Time.frameCount
                && Mouse.current.leftButton.wasPressedThisFrame && !_pullers.IsPointerOverUI(point))
            {
                AssemblyDraggablePart part = FindLoosePart(InputCamera.ScreenPointToRay(point));
                if (part != null) BeginWorldDrag(part, point);
            }
            if (!IsDragging) return;
            MoveDrag(point);
            if (!Mouse.current.leftButton.isPressed)
            {
                if (Mouse.current.leftButton.wasReleasedThisFrame) EndDrag(point);
                else CancelDrag();
            }
        }

        /// <summary>
        /// material과 definition에 대응하는 원본 재고 항목을 찾아 카드와 수량 표시를 연결한다.
        /// viewport를 반환 영역으로 저장하며 초기 수량은 변경하지 않는다. 반환값은 없다.
        /// </summary>
        public void BindCard(KeyboardEditButton button, Material material, KeycapDefinition definition, RectTransform viewport)
        {
            AssemblyCatalogEntry match = FindEntry(material, definition);
            if (_returnArea == null) CreatePlacementModes(viewport);
            _returnArea = viewport;
            if (match == null) { button.interactable = false; return; }
            button.gameObject.AddComponent<WorkshopKeycapCardDrag>().Initialize(this, match);
            _entryTextures[match] = button.GetComponentInChildren<RawImage>().texture;
            BindQuantityLabel(button, match);
        }

        /// <summary>
        /// type에 대응하는 원본 축 재고와 수량 표시를 button에 연결한다.
        /// viewport를 반환 영역으로 저장하고 재고 카드 드래그와 배치 모드를 연결한다. 반환값은 없다.
        /// </summary>
        public void BindSwitchCard(KeyboardEditButton button, SwitchType type, RectTransform viewport)
        {
            if (_switchReturnArea == null) CreatePlacementModes(viewport);
            _switchReturnArea = viewport;
            foreach (AssemblyCatalogEntry entry in _switchEntries)
            {
                if (!entry.IsSwitch || entry.SwitchData.Type != type) continue;
                button.gameObject.AddComponent<WorkshopKeycapCardDrag>().Initialize(this, entry);
                BindQuantityLabel(button, entry);
                return;
            }
            button.interactable = false;
        }

        /// <summary>
        /// button 오른쪽 위에 entry의 사용 가능 수량을 표시하는 Text를 생성한다.
        /// 재고를 변경하지 않고 갱신 대상에 등록한다. 반환값은 없다.
        /// </summary>
        private void BindQuantityLabel(KeyboardEditButton button, AssemblyCatalogEntry entry)
        {
            Text quantity = new GameObject("Quantity", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            quantity.transform.SetParent(button.transform, false);
            quantity.rectTransform.anchorMin = quantity.rectTransform.anchorMax = quantity.rectTransform.pivot = Vector2.one;
            quantity.rectTransform.anchoredPosition = new Vector2(-8f, -8f);
            quantity.rectTransform.sizeDelta = new Vector2(55f, 22f);
            quantity.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            quantity.fontSize = 14;
            quantity.alignment = TextAnchor.MiddleRight;
            quantity.raycastTarget = false;
            _quantityLabels[entry] = quantity;
            RefreshInventory();
        }

        /// <summary>
        /// viewport 위쪽에 원본 조립 UI의 한 개 배치와 일괄 배치 선택 버튼을 만든다.
        /// C# 클릭 이벤트로 모드를 전환하고 기본값은 한 개 배치로 유지한다. 반환값은 없다.
        /// </summary>
        private void CreatePlacementModes(RectTransform viewport)
        {
            RectTransform row = new GameObject("KeycapPlacementModes", typeof(RectTransform), typeof(HorizontalLayoutGroup))
                .GetComponent<RectTransform>();
            row.SetParent(viewport, false);
            row.anchorMin = Vector2.up;
            row.anchorMax = Vector2.one;
            row.pivot = Vector2.up;
            row.anchoredPosition = new Vector2(12f, -12f);
            row.sizeDelta = new Vector2(-36f, 36f);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            _singlePlacementButtons.Add(CreatePlacementButton(row, "한 개 배치"));
            _bulkPlacementButtons.Add(CreatePlacementButton(row, "일괄 배치"));
            RefreshPlacementModes();
        }

        /// <summary>
        /// parent에 text를 표시하는 배치 모드 버튼을 생성하고 클릭 이벤트를 연결한다.
        /// 생성된 KeyboardEditButton을 반환하며 현재 모드는 변경하지 않는다.
        /// </summary>
        private KeyboardEditButton CreatePlacementButton(RectTransform parent, string text)
        {
            KeyboardEditButton button = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(KeyboardEditButton))
                .GetComponent<KeyboardEditButton>();
            button.transform.SetParent(parent, false);
            button.targetGraphic = button.GetComponent<Image>();
            button.Clicked += HandlePlacementModeClicked;
            Text label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(button.transform, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = text;
            return button;
        }

        /// <summary>
        /// button이 선택한 배치 모드를 저장하고 선택 강조를 갱신한다.
        /// 드래그 중에는 모드를 변경하지 않으며 반환값은 없다.
        /// </summary>
        private void HandlePlacementModeClicked(KeyboardEditButton button)
        {
            if (IsDragging) return;
            _bulkPlacement = _bulkPlacementButtons.Contains(button);
            RefreshPlacementModes();
        }

        /// <summary>
        /// 현재 배치 모드에 따라 두 버튼의 색상을 갱신한다.
        /// 재고와 설치 상태는 변경하지 않으며 반환값은 없다.
        /// </summary>
        private void RefreshPlacementModes()
        {
            foreach (KeyboardEditButton button in _singlePlacementButtons)
                button.GetComponent<Image>().color = !_bulkPlacement
                    ? new Color32(39, 80, 100, 255) : new Color32(48, 66, 84, 255);
            foreach (KeyboardEditButton button in _bulkPlacementButtons)
                button.GetComponent<Image>().color = _bulkPlacement
                    ? new Color32(39, 80, 100, 255) : new Color32(48, 66, 84, 255);
        }

        /// <summary>
        /// material과 definition의 에셋 참조로 키캡 재고 항목을 반환한다.
        /// 축 항목은 제외하며 대응 항목이 없으면 null을 반환한다. 재고는 변경하지 않는다.
        /// </summary>
        private AssemblyCatalogEntry FindEntry(Material material, KeycapDefinition definition)
        {
            foreach (AssemblyCatalogEntry entry in _keycapEntries)
                if (!entry.IsSwitch && (entry.MatchesKeycap(material, definition)
                    || (definition != null && entry.KeycapDefinition == definition)
                    || (definition == null && !entry.IsArtisan && entry.KeycapMaterial == material))) return entry;
            return null;
        }

        /// <summary>
        /// entry의 키캡 또는 축을 point에서 드래그하며 한 개 배치에서는 재고 한 개를 예약한다.
        /// 일괄 배치에서는 표시용 실물만 준비하고 재고가 없으면 시작하지 않는다. 반환값은 없다.
        /// </summary>
        public void BeginInventoryDrag(AssemblyCatalogEntry entry, Vector2 point)
        {
            if (!CanBeginInventoryDrag(entry) || !Mouse.current.leftButton.isPressed) return;
            if (!_bulkPlacement && !entry.TryReserve()) return;
            _pullers.CancelDrag();
            _entry = entry;
            _fromInventory = true;
            _isBulkDragging = _bulkPlacement;
            _bulkVisited.Clear();
            _hasPreviousBulkPosition = false;
            _lastBulkFrame = -1;
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            _activePart = CreatePart(entry, entry.IsSwitch ? customization.GetSwitchSlotTransform(0) : customization.GetKeycapSlotTransform(0));
            BeginWorldDrag(_activePart, point);
            RefreshInventory();
        }

        /// <summary>
        /// entry의 보관 부품이나 프리팹을 꺼내 shape의 크기와 회전에 맞춘 실물을 반환한다.
        /// preserveAppearance가 true이면 제거 직전의 메시와 재질 및 크기를 그대로 사용하며 재고 수량은 변경하지 않는다.
        /// </summary>
        private AssemblyDraggablePart CreatePart(AssemblyCatalogEntry entry, Transform shape, bool preserveAppearance = false)
        {
            AssemblyDraggablePart part = entry.TakeStoredPart();
            if (preserveAppearance)
            {
                if (part != null) { _ownedParts.Remove(part); Destroy(part.gameObject); }
                Rigidbody body = _pullers.CreateDetachedVisual(shape, Vector3.zero, Vector3.zero, false);
                part = body.gameObject.AddComponent<AssemblyDraggablePart>();
                part.InitializeLoosePart(entry, true);
                _ownedParts.Add(part);
                _looseParts.Add(part);
                return part;
            }
            if (part == null) { part = Instantiate(entry.Prefab); _ownedParts.Add(part); }
            part.InitializeLoosePart(entry);
            part.gameObject.SetActive(true);
            part.transform.SetPositionAndRotation(shape.position, shape.rotation);
            if (entry.IsSwitch)
            {
                part.transform.localScale = shape.lossyScale;
                if (!_looseParts.Contains(part)) _looseParts.Add(part);
                return part;
            }
            Bounds bounds = new Bounds();
            bool found = false;
            foreach (MeshRenderer renderer in part.GetComponentsInChildren<MeshRenderer>())
                if (renderer.enabled)
                {
                    if (found) bounds.Encapsulate(renderer.bounds);
                    else { bounds = renderer.bounds; found = true; }
                }
            MeshRenderer shell = null;
            foreach (MeshRenderer renderer in shape.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "PBT_SculptedShell") { shell = renderer; break; }
            if (shell != null && bounds.size.magnitude > 0f)
                part.transform.localScale *= shell.bounds.size.magnitude / bounds.size.magnitude;
            if (!_looseParts.Contains(part)) _looseParts.Add(part);
            return part;
        }

        /// <summary>
        /// tool이 있으면 파츠와 같은 전용 카메라로 풀러를 앞에 표시하고 null이면 표시를 복원한다.
        /// 공통 렌더러를 재사용하며 도구 위치와 제거 판정 및 재고는 변경하지 않는다.
        /// </summary>
        public void ShowToolPreview(Transform tool)
        {
            if (tool != null) _previewRenderer.Show(tool);
            else _previewRenderer.Hide();
        }

        /// <summary>
        /// part의 현재 자세를 저장하고 point를 통과하는 카메라 평행 평면에서 집기를 시작한다.
        /// 슬롯과 재고를 유지하며 부품 충돌체를 임시 비활성화한다. 반환값은 없다.
        /// </summary>
        private void BeginWorldDrag(AssemblyDraggablePart part, Vector2 point)
        {
            _activePart = part;
            _entry = part.CatalogEntry;
            part.BeginDrag();
            _dragPlane = new Plane(InputCamera.transform.forward, part.transform.position);
            foreach (Collider collider in part.GetComponentsInChildren<Collider>()) collider.enabled = false;
            _previewRenderer.Show(part.transform);
            MoveDrag(point);
        }

        /// <summary>
        /// point의 포인터 위치에 집은 키캡을 이동시키고 UI 위에서는 실물을 숨긴다.
        /// 재고와 설치 상태는 변경하지 않으며 반환값은 없다.
        /// </summary>
        public void MoveDrag(Vector2 point)
        {
            if (!IsDragging) return;
            if (!_pullers.InputAllowed) { CancelDrag(); return; }
            if (_isBulkDragging && _lastBulkFrame == Time.frameCount) return;
            if (_isBulkDragging) _lastBulkFrame = Time.frameCount;
            bool overUI = _pullers.IsPointerOverUI(point);
            _activePart.gameObject.SetActive(!overUI);
            _dragIcon.gameObject.SetActive(overUI);
            if (overUI)
            {
                _entryTextures.TryGetValue(_entry, out Texture texture);
                _dragIcon.texture = texture != null ? texture : Texture2D.whiteTexture;
                _dragIcon.color = _entry.IsSwitch && _entry.SwitchData.StemMaterial != null
                    && _entry.SwitchData.StemMaterial.HasProperty("_BaseColor")
                    ? _entry.SwitchData.StemMaterial.GetColor("_BaseColor") : Color.white;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, point, null, out Vector2 local);
                _dragIcon.rectTransform.localPosition = local;
            }
            if (_dragPlane.Raycast(InputCamera.ScreenPointToRay(point), out float distance))
                _activePart.transform.position = InputCamera.ScreenPointToRay(point).GetPoint(distance);
            if (!_isBulkDragging) return;
            if (overUI) { _hasPreviousBulkPosition = false; return; }
            if (_hasPreviousBulkPosition && (point - _previousBulkPosition).sqrMagnitude < 0.25f) return;
            InstallAlongBulkPath(_hasPreviousBulkPosition ? _previousBulkPosition : point, point);
            if (!IsDragging) return;
            _previousBulkPosition = point;
            _hasPreviousBulkPosition = true;
        }

        /// <summary>
        /// start와 end 사이 경로의 빈 호환 슬롯에 원본 일괄 배치 방식으로 키캡을 설치한다.
        /// 방문한 슬롯과 UI 뒤는 제외하고 성공할 때만 재고를 소모하며 재고 소진 시 드래그를 종료한다.
        /// </summary>
        private void InstallAlongBulkPath(Vector2 start, Vector2 end)
        {
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            Vector2 direction = end - start;
            float lengthSquared = direction.sqrMagnitude;
            _bulkCandidates.Clear();
            for (int index = 0; index < customization.KeycapCount; index++)
            {
                if (_bulkVisited.Contains(index)) continue;
                Bounds bounds = _entry.IsSwitch ? customization.GetSwitchSlotBounds(index) : customization.GetKeycapSlotBounds(index);
                Vector3 projected = InputCamera.WorldToScreenPoint(bounds.center);
                if (projected.z <= 0f) continue;
                float progress = lengthSquared > 0f
                    ? Mathf.Clamp01(Vector2.Dot((Vector2)projected - start, direction) / lengthSquared) : 0f;
                Vector2 closest = start + direction * progress;
                if (bounds.IntersectRay(InputCamera.ScreenPointToRay(closest)))
                    _bulkCandidates.Add(new KeyValuePair<float, int>(progress, index));
            }
            _bulkCandidates.Sort(CompareBulkCandidates);
            bool changed = false;
            foreach (KeyValuePair<float, int> candidate in _bulkCandidates)
            {
                Vector2 point = start + direction * candidate.Key;
                if (_pullers.IsPointerOverUI(point)) continue;
                Ray ray = InputCamera.ScreenPointToRay(point);
                if (Physics.Raycast(ray, out RaycastHit obstruction, InputCamera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !obstruction.transform.IsChildOf(customization.transform)) continue;
                int index = candidate.Value;
                if (!_entry.TryReserve()) break;
                bool installed = InstallEntry(customization, index);
                _entry.FinishReservation(installed);
                if (installed) { _bulkVisited.Add(index); changed = true; }
                if (_entry.AvailableQuantity <= 0) break;
            }
            if (changed) RefreshInventory();
            if (_entry.AvailableQuantity <= 0) EndBulkPlacement();
        }

        /// <summary>
        /// customization의 index 슬롯에 현재 재고 파츠 종류에 맞는 설치 함수를 호출한다.
        /// 축 타입 또는 키캡 재질과 모델을 전달하고 설치 성공 여부를 반환한다. 재고 수량은 변경하지 않는다.
        /// </summary>
        private bool InstallEntry(KeyboardCustomization customization, int index)
        {
            if (_entry.IsSwitch) return customization.InstallSwitchFromInventory(index, _entry.SwitchData.Type);
            Material material = _entry.KeycapMaterial != null ? _entry.KeycapMaterial : _entry.KeycapDefinition.Material;
            return customization.InstallKeycapFromInventory(index, material, _entry.KeycapDefinition);
        }

        /// <summary>
        /// left와 right의 경로 진행률을 비교해 슬롯의 설치 순서를 반환한다.
        /// 후보 목록과 재고는 변경하지 않는다.
        /// </summary>
        private static int CompareBulkCandidates(KeyValuePair<float, int> left, KeyValuePair<float, int> right)
        {
            return left.Key.CompareTo(right.Key);
        }

        /// <summary>
        /// 일괄 배치의 표시용 키캡을 보관하고 방문 경로와 드래그 참조를 비운다.
        /// 미리보기는 재고를 예약하지 않으므로 설치된 수량만 소비된 상태로 유지한다. 반환값은 없다.
        /// </summary>
        private void EndBulkPlacement()
        {
            _previewRenderer.Hide();
            _looseParts.Remove(_activePart);
            _entry.StorePart(_activePart, false);
            _entry = null;
            _activePart = null;
            _fromInventory = false;
            _isBulkDragging = false;
            _hasPreviousBulkPosition = false;
            _bulkVisited.Clear();
            _dragIcon.gameObject.SetActive(false);
            RefreshInventory();
        }

        /// <summary>
        /// point의 반환 영역과 슬롯 조건에 따라 재고 반환, 설치 또는 물리 낙하를 확정한다.
        /// 왼쪽 버튼이 실제 해제된 경우에만 확정하며 UI 출고 예약은 월드 출고 시 소모한다. 반환값은 없다.
        /// </summary>
        public void EndDrag(Vector2 point)
        {
            if (!IsDragging) return;
            if (!_pullers.InputAllowed) { CancelDrag(); return; }
            if (Mouse.current == null || Mouse.current.leftButton.isPressed) return;
            if (_isBulkDragging) { EndBulkPlacement(); return; }
            _previewRenderer.Hide();
            if (IsInReturnArea(point))
            {
                _looseParts.Remove(_activePart);
                _entry.StorePart(_activePart, !_fromInventory);
                if (_fromInventory) _entry.FinishReservation(false);
                _dragOriginPart = null;
            }
            else if (_dragOriginPart != null)
            {
                DropActivePart();
                _dragOriginPart = null;
            }
            else if (_pullers.IsPointerOverUI(point)) { CancelDrag(); return; }
            else
            {
                KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
                int index = FindInstallSlot(point);
                bool installed = InstallEntry(customization, index);
                if (_fromInventory) _entry.FinishReservation(true);
                if (installed)
                {
                    _looseParts.Remove(_activePart);
                    _entry.StorePart(_activePart, false);
                }
                else
                {
                    _activePart.gameObject.SetActive(true);
                    foreach (Collider collider in _activePart.GetComponentsInChildren<Collider>()) collider.enabled = true;
                    _activePart.DropWithPhysics();
                }
            }
            _entry = null;
            _activePart = null;
            _fromInventory = false;
            _dragIcon.gameObject.SetActive(false);
            RefreshInventory();
        }

        /// <summary>
        /// 현재 UI 출고 예약을 취소하거나 월드 부품의 집기 전 상태를 복원한다.
        /// 확정 설치와 소모 재고는 유지하며 드래그 참조를 비운다. 반환값은 없다.
        /// </summary>
        public void CancelDrag()
        {
            if (!IsDragging) return;
            if (_isBulkDragging) { EndBulkPlacement(); return; }
            _previewRenderer.Hide();
            if (_fromInventory)
            {
                _looseParts.Remove(_activePart);
                _entry.StorePart(_activePart, false);
                _entry.FinishReservation(false);
            }
            else if (_dragOriginPart != null)
            {
                DropActivePart();
                _dragOriginPart = null;
            }
            else
            {
                _activePart.gameObject.SetActive(true);
                foreach (Collider collider in _activePart.GetComponentsInChildren<Collider>()) collider.enabled = true;
                _activePart.RestoreDragOrigin();
            }
            _entry = null;
            _activePart = null;
            _fromInventory = false;
            _dragIcon.gameObject.SetActive(false);
            RefreshInventory();
        }

        /// <summary>point의 설치 레이와 가림 검사를 수행해 슬롯 인덱스를 반환한다. 설치 확정과 안내가 같은 대상을 조회한다.</summary>
        private int FindInstallSlot(Vector2 point)
        {
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            Ray ray = InputCamera.ScreenPointToRay(point);
            if (Physics.Raycast(ray, out RaycastHit obstruction, InputCamera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !obstruction.transform.IsChildOf(customization.transform)) return -1;
            return customization.FindKeycapSlot(ray, _entry.IsSwitch);
        }

        /// <summary>현재 entry와 index의 실제 설치 조건을 조회한다. 수량 예약과 효과음 및 부품 상태는 변경하지 않는다.</summary>
        private bool CanInstallEntry(int index)
        {
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (_entry.IsSwitch) return customization.CanInstallSwitchFromInventory(index, _entry.SwitchData.Type);
            Material material = _entry.KeycapMaterial != null ? _entry.KeycapMaterial : _entry.KeycapDefinition.Material;
            return customization.CanInstallKeycapFromInventory(index, material, _entry.KeycapDefinition);
        }

        /// <summary>현재 카드의 entry가 출고 가능한지 반환한다. 배치 입력과 같은 수량 및 드래그 조건만 읽는다.</summary>
        public bool CanBeginInventoryDrag(AssemblyCatalogEntry entry)
        {
            return _pullers.InputAllowed && Mouse.current != null && !IsDragging && entry.AvailableQuantity > 0;
        }

        /// <summary>state에 현재 부품 드래그나 바닥 부품 집기의 실행 가능한 조작을 추가한다. 포인터는 공통 커서를 사용한다.</summary>
        public bool CollectControlHints(WorkshopControlHintState state)
        {
            if (!_pullers.InputAllowed) return false;
            Vector2 point = InGameCursor.Position;
            if (!IsDragging)
            {
                if (_pullers.IsPullerInteraction(point) || _pullers.IsPointerOverUI(point)
                    || !IsPointerOverLoosePart(InputCamera.ScreenPointToRay(point))) return false;
                state.Title = "바닥 부품";
                state.Add("좌클릭 드래그", "부품 집기");
                return true;
            }
            state.Title = _isBulkDragging ? "일괄 배치" : _entry.IsSwitch ? "스위치 드래그" : "키캡 드래그";
            state.Add("마우스 이동", "부품 이동");
            if (_isBulkDragging)
            {
                if (!_pullers.IsPointerOverUI(point) && _entry.AvailableQuantity > 0 && CanInstallEntry(FindInstallSlot(point)))
                    state.Add("좌클릭 유지", "키캡·스위치 연속 설치");
                state.Add("좌클릭 놓기", "일괄 배치 종료");
            }
            else if (IsInReturnArea(point)) state.Add("좌클릭 놓기", "목록에 반환");
            else if (_dragOriginPart != null) state.Add("좌클릭 놓기", "부품 내려놓기");
            else if (_pullers.IsPointerOverUI(point)) state.Add("좌클릭 놓기", "드래그 취소·복원");
            else state.Add("좌클릭 놓기", CanInstallEntry(FindInstallSlot(point)) ? "슬롯에 설치" : "부품 내려놓기");
            return true;
        }

        /// <summary>
        /// point가 현재 표시된 재료 목록 반환 영역 안인지 반환한다.
        /// 카메라 오버레이 좌표를 사용하며 재고와 설치 상태는 변경하지 않는다.
        /// </summary>
        private bool IsInReturnArea(Vector2 point)
        {
            return (_returnArea != null && _returnArea.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_returnArea, point, null))
                || (_switchReturnArea != null && _switchReturnArea.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_switchReturnArea, point, null));
        }

        /// <summary>
        /// ray가 가장 먼저 가리키는 등록된 월드 키캡을 반환한다.
        /// UI 출고 중인 부품과 가려진 물체는 제외하며 찾지 못하면 null을 반환한다.
        /// </summary>
        private AssemblyDraggablePart FindLoosePart(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, InputCamera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return null;
            AssemblyDraggablePart part = hit.collider.GetComponentInParent<AssemblyDraggablePart>();
            return part != null && _looseParts.Contains(part) ? part : null;
        }

        /// <summary>
        /// ray가 집을 수 있는 월드 키캡 위에 있는지 반환한다. 입력과 재고는 변경하지 않는다.
        /// </summary>
        public bool IsPointerOverLoosePart(Ray ray) => FindLoosePart(ray) != null;

        /// <summary>
        /// removed 키캡을 해당 재고의 실물로 출고하고 velocity와 angularVelocity로 튀긴다.
        /// returnToInventory가 true이면 풀러 지연 반환을 예약하고 일반 클릭은 놓을 때 낙하하는 집기로 전환한다. 성공 여부를 반환한다.
        /// </summary>
        public bool TakeRemovedKeycap(Transform removed, bool returnToInventory, Vector3 velocity, Vector3 angularVelocity)
        {
            KeyboardCustomization customization = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>();
            int index = -1;
            for (int i = 0; i < customization.KeycapCount; i++)
                if (customization.GetKeycapSlotTransform(i) == removed) { index = i; break; }
            if (index < 0) return false;
            AssemblyCatalogEntry entry = FindEntry(customization.GetKeycapMaterial(index), customization.GetKeycapDefinition(index));
            if (entry == null) return false;
            TakeRemovedPart(entry, removed, returnToInventory, velocity, angularVelocity);
            return true;
        }

        /// <summary>
        /// removed의 축 타입에 대응하는 원본 재고를 찾아 실물 분리를 연결한다.
        /// returnToInventory와 속도값으로 풀러 지연 반환 또는 놓을 때 낙하하는 집기를 시작한다. 연결 성공 여부를 반환한다.
        /// </summary>
        public bool TakeRemovedSwitch(Transform removed, bool returnToInventory, Vector3 velocity, Vector3 angularVelocity)
        {
            KeyboardSwitch keyboardSwitch = removed.GetComponent<KeyboardSwitch>();
            SwitchType type = _interaction.EditingKeyboard.GetComponent<KeyboardCustomization>().GetInstalledSwitchType(keyboardSwitch);
            foreach (AssemblyCatalogEntry entry in _switchEntries)
            {
                if (!entry.IsSwitch || entry.SwitchData.Type != type) continue;
                TakeRemovedPart(entry, removed, returnToInventory, velocity, angularVelocity);
                return true;
            }
            return false;
        }

        /// <summary>
        /// entry와 removed로 실물 파츠를 준비하고 일반 클릭이면 원본을 저장한 뒤 드래그를 시작한다.
        /// returnToInventory가 true이면 속도값을 적용하고 2초 뒤 재고 반환을 예약한다. 반환값은 없다.
        /// </summary>
        private void TakeRemovedPart(AssemblyCatalogEntry entry, Transform removed, bool returnToInventory,
            Vector3 velocity, Vector3 angularVelocity)
        {
            AssemblyDraggablePart part = CreatePart(entry, removed, true);
            _lastRemovalFrame = Time.frameCount;
            if (!returnToInventory)
            {
                _dragOriginPart = removed;
                BeginWorldDrag(part, InGameCursor.Position);
                return;
            }
            part.DropWithPhysics();
            Rigidbody body = part.GetComponent<Rigidbody>();
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            if (returnToInventory)
            {
                _extractedReturns[part] = part.InteractionVersion;
                StartCoroutine(ReturnExtractedPart(part, part.InteractionVersion));
            }
        }

        /// <summary>
        /// part가 version 이후 조작되지 않았으면 원본과 동일하게 2초 뒤 재고로 반환한다.
        /// deltaTime으로 시간을 누적하고 보관 상태와 수량 표시를 갱신한다.
        /// </summary>
        private IEnumerator ReturnExtractedPart(AssemblyDraggablePart part, int version)
        {
            float elapsed = 0f;
            while (elapsed < RETURN_DELAY)
            {
                yield return null;
                if (part == null || part.InteractionVersion != version) yield break;
                elapsed += Time.deltaTime;
            }
            _extractedReturns.Remove(part);
            ReturnLoosePart(part);
        }

        /// <summary>
        /// 현재 집은 파츠의 충돌체와 중력을 활성화하여 포인터 위치에서 떨어뜨린다.
        /// 입력값과 반환값은 없으며 원본 키보드의 제거 상태는 유지한다.
        /// </summary>
        private void DropActivePart()
        {
            _activePart.gameObject.SetActive(true);
            foreach (Collider collider in _activePart.GetComponentsInChildren<Collider>()) collider.enabled = true;
            _activePart.DropWithPhysics();
        }

        /// <summary>
        /// part가 현재 집기나 유효한 풀러 회수 예약 대상인지 반환한다.
        /// 조작 버전을 비교하며 파츠와 재고 상태는 변경하지 않는다.
        /// </summary>
        public bool IsReturnBlocked(AssemblyDraggablePart part)
        {
            return _activePart == part || (_extractedReturns.TryGetValue(part, out int version)
                && version == part.InteractionVersion);
        }

        /// <summary>
        /// 등록된 자유 파츠 part를 한 번만 재고로 반환한다.
        /// 실물을 보관하고 수량 표시를 갱신하며 반환값은 없다.
        /// </summary>
        public void ReturnLoosePart(AssemblyDraggablePart part)
        {
            if (!_looseParts.Remove(part)) return;
            _extractedReturns.Remove(part);
            part.CatalogEntry.StorePart(part, true);
            RefreshInventory();
        }

        /// <summary>
        /// entry의 미리보기로 독립적인 반환 아이콘을 생성해 반환한다.
        /// 기존 드래그 아이콘과 입력 및 재고 상태는 유지한다.
        /// </summary>
        public RawImage CreateReturnIcon(AssemblyCatalogEntry entry)
        {
            RawImage icon = Instantiate(_dragIcon, _canvasRect);
            icon.name = "BoundaryReturnIcon";
            _entryTextures.TryGetValue(entry, out Texture texture);
            icon.texture = texture != null ? texture : Texture2D.whiteTexture;
            icon.color = entry.IsSwitch && entry.SwitchData.StemMaterial != null
                && entry.SwitchData.StemMaterial.HasProperty("_BaseColor")
                ? entry.SwitchData.StemMaterial.GetColor("_BaseColor") : Color.white;
            icon.gameObject.SetActive(true);
            return icon;
        }

        /// <summary>
        /// entry 종류에 맞는 재료 목록 또는 접힌 UI의 화면 왼쪽을 반환 목적지로 계산한다.
        /// screen 좌표를 반환하며 패널과 재고는 변경하지 않는다.
        /// </summary>
        public Vector2 GetReturnDestination(AssemblyCatalogEntry entry)
        {
            RectTransform area = entry.IsSwitch ? _switchReturnArea : _returnArea;
            return area != null && area.gameObject.activeInHierarchy
                ? RectTransformUtility.WorldToScreenPoint(null, area.TransformPoint(area.rect.center))
                : new Vector2(0f, Screen.height * 0.5f);
        }

        /// <summary>
        /// 각 원본 재고 항목의 사용 가능한 수량을 카드에 표시한다.
        /// 예약과 소모 수량을 읽기만 하며 반환값은 없다.
        /// </summary>
        private void RefreshInventory()
        {
            foreach (var label in _quantityLabels) label.Value.text = "×" + label.Key.AvailableQuantity;
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= CancelDrag;
            CancelDrag();
        }
        void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
        void OnDestroy()
        {
            foreach (KeyboardEditButton button in _singlePlacementButtons) button.Clicked -= HandlePlacementModeClicked;
            foreach (KeyboardEditButton button in _bulkPlacementButtons) button.Clicked -= HandlePlacementModeClicked;
            if (_dragIcon != null) Destroy(_dragIcon.gameObject);
            foreach (AssemblyDraggablePart part in _ownedParts) if (part != null) Destroy(part.gameObject);
        }
    }
}
