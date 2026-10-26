using System;
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

        [Header("Failed Placement")]
        [SerializeField, Min(0f)] private float _failedDropLiftSpeed = 0.6f;
        [SerializeField, Min(0f)] private float _failedDropPushSpeed = 0.35f;
        [SerializeField, Min(0f)] private float _failedDropSpinSpeed = 4f;

        [Header("Workshop")]
        [SerializeField] private PlayerInteractionController _interaction;
        [SerializeField] private WorkshopKeyboardPullerController _pullers;

        [Header("Keycap Inventory")]
        [SerializeField] private AssemblyCatalogEntry[] _keycapEntries = new AssemblyCatalogEntry[0];

        [Header("Switch Inventory")]
        [SerializeField] private AssemblyCatalogEntry[] _switchEntries = new AssemblyCatalogEntry[0];

        [Header("Placement Mode Style")]
        [SerializeField] private Color _modeSelectedColor = new Color32(92, 58, 33, 255);
        [SerializeField] private Color _modeNormalColor = new Color32(214, 186, 154, 255);
        [SerializeField] private Color _modeSelectedOutlineColor = new Color32(255, 222, 173, 255);
        [SerializeField] private Color _modeNormalOutlineColor = new Color32(148, 115, 82, 255);
        [SerializeField] private Color _modeSelectedTextColor = new Color32(255, 250, 240, 255);
        [SerializeField] private Color _modeNormalTextColor = new Color32(105, 75, 48, 255);
        private readonly Dictionary<AssemblyCatalogEntry, Texture> _entryTextures = new Dictionary<AssemblyCatalogEntry, Texture>();
        private readonly Dictionary<SwitchType, Texture2D> _switchPreviewTextures = new Dictionary<SwitchType, Texture2D>();
        private WorkshopKeycapDrawerTransition _keycapTray;
        private WorkshopKeycapCardDrag _inventoryScrollOwner;
        private RectTransform _trayPlacementModes;
        private readonly List<AssemblyDraggablePart> _ownedParts = new List<AssemblyDraggablePart>();
        private readonly List<AssemblyDraggablePart> _looseParts = new List<AssemblyDraggablePart>();
        private AssemblyDraggablePart _activePart;
        private Transform _dragOriginPart;
        private AssemblyCatalogEntry _entry;
        private bool _fromInventory;
        private Plane _dragPlane;
        private Plane _switchClearancePlane;
        private RectTransform _returnArea;
        private RectTransform _switchReturnArea;
        private RectTransform _canvasRect;
        private RawImage _dragIcon;
        private WorkshopDragPreviewRenderer _previewRenderer;
        private WorkshopVerticalCatalogLayout _drawerLayout;
        private readonly List<KeyboardEditButton> _singlePlacementButtons = new List<KeyboardEditButton>();
        private readonly List<KeyboardEditButton> _bulkPlacementButtons = new List<KeyboardEditButton>();
        private bool _bulkPlacement;
        private bool _isBulkDragging;
        private KeyboardCustomization _bulkCustomization;
        private string _bulkInteractionId;
        private int _bulkAttemptCount;
        private int _bulkSuccessCount;
        private bool _hasPreviousBulkPosition;
        private Vector2 _previousBulkPosition;
        private readonly HashSet<int> _bulkVisited = new HashSet<int>();
        private readonly List<KeyValuePair<float, int>> _bulkCandidates = new List<KeyValuePair<float, int>>();
        private int _lastBulkFrame = -1;
        private int _lastRemovalFrame = -1;
        private readonly Dictionary<AssemblyDraggablePart, int> _extractedReturns = new Dictionary<AssemblyDraggablePart, int>();
        private KeyboardInputController EditingKeyboard => _interaction != null
            ? _interaction.EditingKeyboard : _pullers.EditingKeyboard;
        private Camera InputCamera => _pullers.InputCamera;
        public bool IsDragging => _entry != null;
        public bool IsInventoryScrolling => _inventoryScrollOwner != null;
        public WorkshopKeyboardPullerController Pullers => _pullers;
        public bool IsEditing => _interaction != null ? _interaction.IsKeyboardEditing : _pullers.IsEditing;
        public Camera ReturnCamera => InputCamera;
        public IReadOnlyList<AssemblyDraggablePart> LooseParts => _looseParts;

        /// <summary>현재 집은 외부 실물과 상위 category를 반환한다. 목록 아이콘·일괄 표시·유효한 자동 회수 예약은 제외하며 집기와 반환 상태는 변경하지 않는다.</summary>
        public bool TryGetHeldReturnPart(out Transform part, out WorkshopDrawerCategory category)
        {
            part = null;
            category = default;
            if (!isActiveAndEnabled || !IsDragging || _isBulkDragging || IsInventoryScrolling
                || _activePart == null || !_activePart.gameObject.activeInHierarchy
                || !_looseParts.Contains(_activePart) || _entry.IsStructure
                || (_extractedReturns.TryGetValue(_activePart, out int version) && version == _activePart.InteractionVersion)) return false;
            part = _activePart.transform;
            category = _entry.IsSwitch ? WorkshopDrawerCategory.Switches : WorkshopDrawerCategory.Keycaps;
            return true;
        }

        void Awake()
        {
            _drawerLayout = GetComponent<WorkshopVerticalCatalogLayout>();
            foreach (AssemblyCatalogEntry entry in _keycapEntries) entry.InitializeInventory();
            foreach (AssemblyCatalogEntry entry in _switchEntries) entry.InitializeInventory();
        }

        void Start()
        {
            _canvasRect = (RectTransform)GetComponentInParent<Canvas>().transform;
            _dragIcon = new GameObject("WorkshopKeycapDragIcon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _dragIcon.transform.SetParent(_canvasRect, false);
            _dragIcon.rectTransform.sizeDelta = new Vector2(80f, 80f);
            _dragIcon.raycastTarget = false;
            _dragIcon.gameObject.SetActive(false);
            _previewRenderer = gameObject.AddComponent<WorkshopDragPreviewRenderer>();
            _previewRenderer.Initialize(InputCamera);
            GetComponent<KeyboardEditOverlayUI>().ConnectKeycapPlacement(this);
        }

        void OnEnable()
        {
            InGameCursor.PointerCanceled += CancelDrag;
        }

        void Update()
        {
            RefreshPlacementModes();
            if (!_pullers.InputAllowed || Mouse.current == null)
            {
                if (_pullers.EditingPointerAllowed && Mouse.current != null && !IsDragging && IsInventoryScrolling) return;
                CancelDrag();
                return;
            }
            if (_pullers.IsPcbDragging || _pullers.IsScrewPointerCaptured || IsInventoryScrolling) return;
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

        /// <summary>눌린 entry의 키캡·스위치 카드 선택 표시만 갱신한다. 조작 가능 상태를 검사하며 실물 생성과 재고 예약 및 설치는 수행하지 않는다.</summary>
        public void SelectInventoryCard(AssemblyCatalogEntry entry)
        {
            if (entry.IsStructure || !_pullers.InputAllowed || _pullers.IsManipulatingPart) return;
            GetComponent<KeyboardEditOverlayUI>().SelectInventoryCard(entry);
        }

        /// <summary>
        /// material과 definition에 대응하는 원본 항목을 찾아 카드와 출고 입력을 연결한다.
        /// viewport를 반환 영역으로 저장하며 tray의 목록 드래그에서 선택한 배치 모드로 출고한다. 초기 수량은 유지한다.
        /// </summary>
        public void BindCard(KeyboardEditButton button, Material material, KeycapDefinition definition, RectTransform viewport, WorkshopKeycapDrawerTransition tray = null)
        {
            _keycapTray = tray;
            AssemblyCatalogEntry match = FindEntry(material, definition);
            if (_returnArea == null && tray == null) CreatePlacementModes(viewport);
            _returnArea = viewport;
            if (match == null) { button.interactable = false; return; }
            if (tray != null) button.enabled = false;
            button.gameObject.AddComponent<WorkshopKeycapCardDrag>().Initialize(this, match, tray);
            _entryTextures[match] = button.GetComponentInChildren<RawImage>().texture;
        }

        /// <summary>
        /// type에 대응하는 원본 축 항목과 출고 입력을 button에 연결한다.
        /// viewport를 반환 영역으로 저장하고 재고 카드 드래그와 배치 모드를 연결한다. 반환값은 없다.
        /// </summary>
        public void BindSwitchCard(KeyboardEditButton button, SwitchType type, RectTransform viewport, WorkshopKeycapDrawerTransition tray = null)
        {
            if (_switchReturnArea == null && tray == null) CreatePlacementModes(viewport);
            _switchReturnArea = viewport;
            foreach (AssemblyCatalogEntry entry in _switchEntries)
            {
                if (!entry.IsSwitch || entry.SwitchData.Type != type) continue;
                _entryTextures[entry] = GetSwitchPreview(type);
                if (tray != null) button.enabled = false;
                button.gameObject.AddComponent<WorkshopKeycapCardDrag>().Initialize(this, entry, tray);
                return;
            }
            button.interactable = false;
        }

        /// <summary>type의 출고 프리팹과 축 재질을 격리된 모델에 적용해 썸네일을 반환한다. 생성한 이미지는 재사용하며 원본 모델·재질·재고는 변경하지 않는다.</summary>
        public Texture2D GetSwitchPreview(SwitchType type)
        {
            if (_switchPreviewTextures.TryGetValue(type, out Texture2D texture)) return texture;
            foreach (AssemblyCatalogEntry entry in _switchEntries)
            {
                if (!entry.IsSwitch || entry.SwitchData.Type != type) continue;
                GameObject stage = new GameObject("SwitchCardPreview");
                stage.hideFlags = HideFlags.HideAndDontSave;
                stage.SetActive(false);
                try
                {
                    GameObject model = Instantiate(entry.Prefab.gameObject, stage.transform, false);
                    foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Material material = renderer.name == "Switch_TopHousing" ? entry.SwitchData.TopHousingMaterial
                            : renderer.name == "Switch_BottomHousing" ? entry.SwitchData.BottomHousingMaterial
                            : renderer.name.StartsWith("MX_Stem") ? entry.SwitchData.StemMaterial : null;
                        if (material != null) renderer.sharedMaterial = material;
                    }
                    texture = AssemblyCatalogPreview.RenderParts(new[] { model.transform });
                    _switchPreviewTextures[type] = texture;
                    return texture;
                }
                finally
                {
                    Destroy(stage);
                }
            }
            return null;
        }

        /// <summary>구조 부품 entry의 카드에 기존 아이콘·스크롤 출고 입력을 연결한다. structureDrag만 실물 설치 경로를 담당하며 기존 키캡·스위치 재고는 변경하지 않는다.</summary>
        public void BindStructureCard(KeyboardEditButton button, AssemblyCatalogEntry entry, Texture texture,
            WorkshopKeycapDrawerTransition tray, WorkshopPcbCardDrag structureDrag)
        {
            button.enabled = false;
            button.gameObject.AddComponent<WorkshopKeycapCardDrag>().Initialize(this, entry, tray, structureDrag);
            _entryTextures[entry] = texture;
        }

        /// <summary>entry의 기존 출고 아이콘만 texture로 갱신한다. 카드 재생성이나 수량·예약 초기화는 하지 않는다.</summary>
        public void SetInventoryTexture(AssemblyCatalogEntry entry, Texture texture)
        {
            _entryTextures[entry] = texture;
        }

        /// <summary>
        /// viewport 상단에 font로 기존 한 개 배치와 일괄 배치 버튼을 만들고 행을 반환한다.
        /// C# 클릭 이벤트를 연결하며 현재 배치 모드는 변경하지 않는다.
        /// </summary>
        private RectTransform CreatePlacementModes(RectTransform viewport, Font font = null)
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
            _singlePlacementButtons.Add(CreatePlacementButton(row, "한 개 배치", font));
            _bulkPlacementButtons.Add(CreatePlacementButton(row, "연속 배치", font));
            RefreshPlacementModes();
            return row;
        }

        /// <summary>
        /// parent의 예약된 상단 공간에 기존 배치 모드 버튼을 font로 한 번 연결한다.
        /// 트레이의 카드와 스크롤은 이동시키지 않으며 현재 배치 모드는 유지한다.
        /// </summary>
        public void BindTrayPlacementModes(RectTransform parent, Font font)
        {
            if (_trayPlacementModes != null) return;
            _trayPlacementModes = CreatePlacementModes(parent, font);
            RefreshPlacementModes();
        }

        /// <summary>
        /// owner의 목록 드래그 시작과 종료를 active로 기록한다.
        /// 해당 카드만 점유를 해제할 수 있으며 재고 예약과 실물 생성은 수행하지 않는다.
        /// </summary>
        public void SetInventoryScroll(WorkshopKeycapCardDrag owner, bool active)
        {
            if (active) _inventoryScrollOwner = owner;
            else if (_inventoryScrollOwner == owner) _inventoryScrollOwner = null;
        }

        /// <summary>
        /// parent에 text와 font를 사용하는 배치 모드 버튼을 생성하고 클릭 이벤트를 연결한다.
        /// 생성된 KeyboardEditButton을 반환하며 현재 모드는 변경하지 않는다.
        /// </summary>
        private KeyboardEditButton CreatePlacementButton(RectTransform parent, string text, Font font)
        {
            KeyboardEditButton button = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(KeyboardEditButton))
                .GetComponent<KeyboardEditButton>();
            button.transform.SetParent(parent, false);
            button.targetGraphic = button.GetComponent<Image>();
            button.Clicked += HandlePlacementModeClicked;
            Outline outline = button.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            Text label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(button.transform, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            if (!CanChangePlacementMode(button)) return;
            _bulkPlacement = _bulkPlacementButtons.Contains(button);
            RefreshPlacementModes();
        }

        /// <summary>
        /// 현재 배치 모드에 따라 두 버튼의 색상과 스타일을 갱신한다.
        /// _bulkPlacement 상태를 사용하며 각 버튼의 배경, 테두리, 텍스트 스타일을 갱신한다.
        /// </summary>
        private void RefreshPlacementModes()
        {
            if (_trayPlacementModes != null)
                _trayPlacementModes.gameObject.SetActive(_keycapTray.Category == WorkshopDrawerCategory.Keycaps || _keycapTray.Category == WorkshopDrawerCategory.Switches);
            foreach (KeyboardEditButton button in _singlePlacementButtons)
            {
                button.interactable = CanChangePlacementMode(button);
                ApplyPlacementModeButtonStyle(button, !_bulkPlacement);
            }
            foreach (KeyboardEditButton button in _bulkPlacementButtons)
            {
                button.interactable = CanChangePlacementMode(button);
                ApplyPlacementModeButtonStyle(button, _bulkPlacement);
            }
        }

        /// <summary>
        /// 배치 모드 버튼에 선택 상태에 따른 배경색, 외곽선, 텍스트 스타일을 적용한다.
        /// button과 selected 여부를 사용하며, 버튼의 Image, Outline, Text 컴포넌트 상태를 변경한다.
        /// </summary>
        public void ApplyPlacementModeButtonStyle(KeyboardEditButton button, bool selected)
        {
            Image background = button.GetComponent<Image>();
            if (background != null)
            {
                background.color = selected ? _modeSelectedColor : _modeNormalColor;
            }

            Outline outline = button.GetComponent<Outline>();
            if (outline == null)
            {
                outline = button.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            outline.effectColor = selected ? _modeSelectedOutlineColor : _modeNormalOutlineColor;

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? _modeSelectedTextColor : _modeNormalTextColor;
                label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                label.fontSize = 18;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 15;
                label.resizeTextMaxSize = 18;
                label.lineSpacing = 1f;
            }
        }

        /// <summary>
        /// button이 속한 트레이의 열림과 기존 집중·드래그·도구 상태로 모드 전환 허용 여부를 반환한다.
        /// 다른 서랍의 배치 버튼에는 키캡 트레이 열림 조건을 적용하지 않는다.
        /// </summary>
        private bool CanChangePlacementMode(KeyboardEditButton button)
        {
            return _pullers.InputAllowed && !_pullers.IsManipulatingPart && !IsInventoryScrolling
                && (_drawerLayout == null || !_drawerLayout.IsTransitioning)
                && (_trayPlacementModes == null || button.transform.parent != _trayPlacementModes || _keycapTray.CanInteractWithTray);
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

        /// <summary>customization의 PCB에 실제 장착된 축·키캡의 재고 연결을 모두 검사한다. 누락 시 상태를 보존하고 이유를 기록하며 false를 반환한다.</summary>
        public bool TryReturnPcbParts(KeyboardCustomization customization)
        {
            var parts = new List<(Transform Part, AssemblyCatalogEntry Entry)>();
            for (int index = 0; index < customization.KeycapCount; index++)
            {
                if (customization.IsPcbKeycapInstalled(index))
                {
                    AssemblyCatalogEntry keycap = FindEntry(customization.GetKeycapMaterial(index), customization.GetKeycapDefinition(index));
                    if (keycap == null) { Debug.LogError($"PCB 반환 실패: 키캡 {index} 재고 항목 없음", customization); return false; }
                    parts.Add((customization.GetKeycapSlotTransform(index), keycap));
                }
                if (customization.IsPcbSwitchInstalled(index))
                {
                    Transform part = customization.GetSwitchSlotTransform(index);
                    SwitchType type = customization.GetInstalledSwitchType(part.GetComponent<KeyboardSwitch>());
                    AssemblyCatalogEntry found = null;
                    foreach (AssemblyCatalogEntry entry in _switchEntries)
                        if (entry.IsSwitch && entry.SwitchData.Type == type) { found = entry; break; }
                    if (found == null) { Debug.LogError($"PCB 반환 실패: 축 {index} 재고 항목 없음", customization); return false; }
                    parts.Add((part, found));
                }
            }
            var prepared = new List<AssemblyDraggablePart>();
            try
            {
                foreach (var item in parts)
                {
                    Rigidbody visual = _pullers.CreateDetachedVisual(item.Part, Vector3.zero, Vector3.zero, false);
                    AssemblyDraggablePart part = visual.gameObject.AddComponent<AssemblyDraggablePart>();
                    prepared.Add(part);
                    part.InitializeLoosePart(item.Entry, true);
                    part.gameObject.SetActive(false);
                }
            }
            catch (System.Exception exception)
            {
                foreach (AssemblyDraggablePart part in prepared) Destroy(part.gameObject);
                Debug.LogError("PCB 반환 준비 실패: 원본 묶음을 유지합니다. " + exception, customization);
                return false;
            }
            for (int index = 0; index < parts.Count; index++)
            {
                parts[index].Part.gameObject.SetActive(false);
                _ownedParts.Add(prepared[index]);
                parts[index].Entry.StorePart(prepared[index], false);
            }
            return true;
        }

        /// <summary>customization의 장착 부품 반환을 검증하고 준비된 완료 동작을 즉시 한 번 실행한다. 실패 시 실물을 보존하며 수량은 변경하지 않고 성공 여부를 반환한다.</summary>
        public bool TryReturnKeyboardInventory(KeyboardCustomization customization)
        {
            if (!TryPrepareKeyboardInventoryReturn(customization, out Action completeReturn)) return false;
            completeReturn();
            return true;
        }

        /// <summary>
        /// customization의 현재 장착 부품을 기록해 나중에 한 번만 반환할 동작을 준비한다.
        /// 키캡 파손 전의 설치 상태와 카탈로그 항목을 사용하며 실패 시 재고를 변경하지 않고 false를 반환한다.
        /// </summary>
        public bool TryPrepareKeyboardInventoryReturn(KeyboardCustomization customization, out Action completeReturn)
        {
            completeReturn = null;
            if (customization == null || IsDragging || _pullers.IsPcbDragging || _pullers.IsScrewPointerCaptured) return false;
            for (int index = 0; index < customization.KeycapCount; index++)
            {
                Transform keycapSlot = customization.GetKeycapSlotTransform(index);
                KeycapHealth health = keycapSlot != null ? keycapSlot.GetComponent<KeycapHealth>() : null;
                bool brokenInPlace = keycapSlot != null && keycapSlot.gameObject.activeSelf
                    && health != null && health.CurrentHP == 0;
                if (customization.IsPcbKeycapInstalled(index) || brokenInPlace)
                {
                    AssemblyCatalogEntry keycap = FindEntry(customization.GetKeycapMaterial(index), customization.GetKeycapDefinition(index));
                    if (keycap == null) return false;
                }
                if (!customization.IsPcbSwitchInstalled(index)) continue;
                Transform switchPart = customization.GetSwitchSlotTransform(index);
                SwitchType type = customization.GetInstalledSwitchType(switchPart.GetComponent<KeyboardSwitch>());
                AssemblyCatalogEntry found = null;
                foreach (AssemblyCatalogEntry entry in _switchEntries)
                    if (entry.IsSwitch && entry.SwitchData.Type == type) { found = entry; break; }
                if (found == null) return false;
            }

            KeyboardAssemblyState assembly = customization.GetComponent<KeyboardAssemblyState>();
            AssemblyCatalogEntry lowerEntry = null;
            var detachedStructures = new List<Transform>();
            if (assembly != null && assembly.Initialized)
            {
                for (int index = 0; index < assembly.FixedScrews.Count; index++)
                    if (assembly.IsScrewRunning(index)) return false;
                KeyboardEditOverlayUI overlay = FindFirstObjectByType<KeyboardEditOverlayUI>();
                AssemblyDraggablePart rootPart = customization.GetComponent<AssemblyDraggablePart>();
                lowerEntry = rootPart != null && rootPart.CatalogEntry != null
                    && rootPart.CatalogEntry.IsStructure
                    && rootPart.CatalogEntry.StructureKind == AssemblyStructurePart.Lower
                    && rootPart.CatalogEntry.StructureMaterial == assembly.GetStructureMaterial(AssemblyStructurePart.Lower)
                    ? rootPart.CatalogEntry : GetStructureEntry(assembly, overlay, AssemblyStructurePart.Lower);
                if (lowerEntry == null) return false;
                if (!AddStructureReturn(assembly, overlay, AssemblyStructurePart.Upper,
                    assembly.UpperInstalled, detachedStructures)) return false;
                if (!AddStructureReturn(assembly, overlay, AssemblyStructurePart.Pcb,
                    assembly.PcbPlaced || assembly.PcbLoose, detachedStructures)) return false;
                if (!AddStructureReturn(assembly, overlay, AssemblyStructurePart.Plate,
                    assembly.PlateInstalled, detachedStructures)) return false;
            }
            else return false;

            bool returned = false;
            completeReturn = () =>
            {
                if (returned) return;
                returned = true;
                foreach (Transform part in detachedStructures)
                {
                    if (part == null) continue;
                    part.gameObject.SetActive(false);
                    Destroy(part.gameObject);
                }
            };
            return true;
        }

        /// <summary>assembly의 kind별 재질과 기존 출고 기록으로 정확한 카탈로그 항목을 반환한다. 원본 기록을 우선 사용하고 없으면 overlay 카탈로그를 조회한다.</summary>
        private static AssemblyCatalogEntry GetStructureEntry(KeyboardAssemblyState assembly, KeyboardEditOverlayUI overlay,
            AssemblyStructurePart kind)
        {
            AssemblyCatalogEntry entry = assembly.GetStructureInventoryEntry(kind);
            if (entry != null) return entry;
            return overlay != null ? overlay.GetStructureEntry(kind, assembly.GetStructureMaterial(kind)) : null;
        }

        /// <summary>kind의 실제 활성 부품에 대응하는 카탈로그 항목을 검사한다. 외부에 분리된 부품은 반환 확정 후 제거 목록에 둔다.</summary>
        private static bool AddStructureReturn(KeyboardAssemblyState assembly, KeyboardEditOverlayUI overlay,
            AssemblyStructurePart kind, bool installed, List<Transform> detached)
        {
            IReadOnlyList<Transform> parts = assembly.GetStructureParts(kind);
            bool hasPhysicalPart = installed;
            foreach (Transform part in parts)
                if (part != null && part.gameObject.activeSelf) hasPhysicalPart = true;
            if (!hasPhysicalPart) return true;
            AssemblyCatalogEntry entry = GetStructureEntry(assembly, overlay, kind);
            if (entry == null) return kind == AssemblyStructurePart.Plate;
            Transform root = assembly.transform.root;
            foreach (Transform part in parts)
                if (part != null && part.gameObject.activeSelf && !part.IsChildOf(root)) detached.Add(part);
            return true;
        }

        /// <summary>
        /// entry의 등록된 키캡 또는 축을 수량 제한 없이 point에서 드래그한다.
        /// 일괄 배치는 표시용 실물만 준비하며 실물 추적과 입력 점유를 유지한다. 반환값은 없다.
        /// </summary>
        public void BeginInventoryDrag(AssemblyCatalogEntry entry, Vector2 point)
        {
            if (!CanBeginInventoryDrag(entry) || !Mouse.current.leftButton.isPressed) return;
            bool bulk = _bulkPlacement;
            _pullers.CancelDrag();
            _entry = entry;
            _fromInventory = true;
            _isBulkDragging = bulk;
            _bulkInteractionId = _isBulkDragging ? System.Guid.NewGuid().ToString("N") : null;
            _bulkAttemptCount = 0;
            _bulkSuccessCount = 0;
            _bulkVisited.Clear();
            _hasPreviousBulkPosition = false;
            _lastBulkFrame = -1;
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            _bulkCustomization = bulk ? customization : null;
            _activePart = CreatePart(entry, entry.IsSwitch ? customization.GetSwitchSlotTransform(0) : customization.GetKeycapSlotTransform(0));
            if (_isBulkDragging)
                Game.Logging.CustomizationTelemetry.Emit(customization, "BulkPlacementStarted",
                    entry.IsSwitch ? "Switch" : "Keycap", GetEntryLogId(entry),
                    interactionId: _bulkInteractionId, actionSource: "BulkPlacement");
            BeginWorldDrag(_activePart, point);
            Game.Logging.CustomizationTelemetry.Emit(customization, "InventoryPartPicked",
                entry.IsSwitch ? "Switch" : "Keycap", GetEntryLogId(entry),
                keycapKind: entry.IsSwitch ? "" : entry.IsArtisan ? "Artisan" : "Regular");
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
            PrepareSwitchClearance(part);
            foreach (Collider collider in part.GetComponentsInChildren<Collider>()) collider.enabled = false;
            _previewRenderer.Show(part.transform);
            MoveDrag(point);
        }

        /// <summary>
        /// part의 메시 크기와 편집 키보드의 현재 표면으로 키캡과 스위치 이동의 최소 높이를 저장한다.
        /// 키보드의 위쪽 방향을 사용하며 슬롯 위치와 부품 크기는 변경하지 않는다.
        /// </summary>
        private void PrepareSwitchClearance(AssemblyDraggablePart part)
        {
            Vector3 normal = EditingKeyboard.transform.up;
            Vector3 absoluteNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            float surface = Vector3.Dot(normal, EditingKeyboard.transform.position);
            foreach (MeshRenderer renderer in EditingKeyboard.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                Bounds bounds = renderer.bounds;
                surface = Mathf.Max(surface, Vector3.Dot(normal, bounds.center)
                    + Vector3.Dot(absoluteNormal, bounds.extents));
            }
            float bottomOffset = 0f;
            foreach (MeshRenderer renderer in part.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                Bounds bounds = renderer.bounds;
                bottomOffset = Mathf.Max(bottomOffset, Vector3.Dot(normal, part.transform.position - bounds.center)
                    + Vector3.Dot(absoluteNormal, bounds.extents));
            }
            _switchClearancePlane = new Plane(normal, -surface - bottomOffset - 0.02f);
        }

        /// <summary>
        /// 클릭으로 제거한 원본 스위치를 복원하고 이동용 실물을 재고 증가 없이 회수한다.
        /// 저장된 원본 참조와 현재 축 타입을 사용하며 설치 위치와 키 바인딩을 유지한다.
        /// </summary>
        private void RestoreRemovedSwitch()
        {
            _dragOriginPart.gameObject.SetActive(true);
            KeyboardInputController keyboard = _dragOriginPart.GetComponentInParent<KeyboardInputController>();
            KeyboardCustomization customization = keyboard.GetComponent<KeyboardCustomization>();
            keyboard.SetSwitchSound(customization.GetInstalledSwitchType(_dragOriginPart.GetComponent<KeyboardSwitch>()));
            _looseParts.Remove(_activePart);
            _entry.StorePart(_activePart, false);
            _dragOriginPart = null;
        }

        /// <summary>
        /// entry의 기존 썸네일을 point에 표시하며 null이면 아이콘을 숨긴다. 재고 예약이나 실물 생성은 하지 않는다.
        /// </summary>
        public void ShowInventoryIcon(AssemblyCatalogEntry entry, Vector2 point)
        {
            _dragIcon.gameObject.SetActive(entry != null);
            if (entry == null) return;
            _entryTextures.TryGetValue(entry, out Texture texture);
            _dragIcon.texture = texture != null ? texture : Texture2D.whiteTexture;
            _dragIcon.color = Color.white;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, point, null, out Vector2 local);
            _dragIcon.rectTransform.localPosition = local;
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
            ShowInventoryIcon(overUI ? _entry : null, point);
            Ray ray = InputCamera.ScreenPointToRay(point);
            if (_dragPlane.Raycast(ray, out float distance))
            {
                Vector3 position = ray.GetPoint(distance);
                if (_switchClearancePlane.GetDistanceToPoint(position) < 0f
                    && _switchClearancePlane.Raycast(ray, out float clearanceDistance))
                    position = ray.GetPoint(clearanceDistance);
                _activePart.transform.position = position;
            }
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
        /// 방문한 슬롯과 UI 뒤는 제외하며 재고 개수와 관계없이 각 슬롯의 기존 설치 조건을 검사한다.
        /// </summary>
        private void InstallAlongBulkPath(Vector2 start, Vector2 end)
        {
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
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
            foreach (KeyValuePair<float, int> candidate in _bulkCandidates)
            {
                Vector2 point = start + direction * candidate.Key;
                if (_pullers.IsPointerOverUI(point)) continue;
                Ray ray = InputCamera.ScreenPointToRay(point);
                if (Physics.Raycast(ray, out RaycastHit obstruction, InputCamera.farClipPlane,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !obstruction.transform.IsChildOf(customization.transform)) continue;
                int index = candidate.Value;
                if (!CanInstallEntry(index)) continue;
                _bulkAttemptCount++;
                bool installed = InstallEntry(customization, index);
                if (installed)
                {
                    _bulkVisited.Add(index);
                    _bulkSuccessCount++;
                    RecordInstallAttempt(customization, index, true, "BulkPlacement");
                }
            }
        }

        /// <summary>
        /// customization의 index 슬롯에 현재 재고 파츠 종류에 맞는 설치 함수를 호출한다.
        /// 축 타입 또는 키캡 재질과 모델을 전달하고 설치 성공 여부를 반환한다. 재고 수량은 변경하지 않는다.
        /// </summary>
        private bool InstallEntry(KeyboardCustomization customization, int index)
        {
            if (!CanInstallEntry(index)) return false;
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
        /// 일괄 배치의 종료 이유와 취소 여부 및 설치 집계를 기록하고 드래그 참조를 비운다.
        /// cancelled와 endReason을 사용하며 이미 설치된 수량은 유지한다. 반환값은 없다.
        /// </summary>
        private void EndBulkPlacement(bool cancelled = false, string endReason = "Released")
        {
            if (_bulkCustomization != null) Game.Logging.CustomizationTelemetry.Emit(_bulkCustomization,
                "BulkPlacementEnded", _entry.IsSwitch ? "Switch" : "Keycap", GetEntryLogId(_entry),
                success: !cancelled, changed: _bulkSuccessCount > 0, details: endReason,
                interactionId: _bulkInteractionId, actionSource: "BulkPlacement",
                outcomeReason: cancelled ? "Cancelled" : "", attemptCount: _bulkAttemptCount,
                successCount: _bulkSuccessCount);
            _previewRenderer.Hide();
            _looseParts.Remove(_activePart);
            _entry.StorePart(_activePart, false);
            _entry = null;
            _activePart = null;
            _fromInventory = false;
            _isBulkDragging = false;
            _bulkCustomization = null;
            _bulkInteractionId = null;
            _bulkAttemptCount = 0;
            _bulkSuccessCount = 0;
            _hasPreviousBulkPosition = false;
            _bulkVisited.Clear();
            _dragIcon.gameObject.SetActive(false);
        }

        /// <summary>
        /// point의 반환 영역과 슬롯 조건에 따라 재고 반환, 설치 또는 물리 낙하를 확정한다.
        /// 왼쪽 버튼이 실제 해제된 경우에만 확정하며 출고 실물의 보관·설치·낙하를 처리한다. 반환값은 없다.
        /// </summary>
        public void EndDrag(Vector2 point)
        {
            if (!IsDragging) return;
            if (!_pullers.InputAllowed) { CancelDrag(); return; }
            if (Mouse.current == null || Mouse.current.leftButton.isPressed) return;
            if (_isBulkDragging) { EndBulkPlacement(); return; }
            Transform cabinetReturn = _keycapTray != null && _activePart.CatalogEntry == _entry
                && _keycapTray.CanReturnHeldPart(_activePart.transform) ? _activePart.transform : null;
            _previewRenderer.Hide();
            if (cabinetReturn != null || IsInReturnArea(point))
            {
                ReturnLoosePart(_activePart);
                _dragOriginPart = null;
            }
            else if (_dragOriginPart != null && !_entry.IsSwitch)
            {
                DropActivePart();
                _dragOriginPart = null;
            }
            else if (_pullers.IsPointerOverUI(point)) { CancelDrag(); return; }
            else
            {
                KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
                int index = FindInstallSlot(point);
                bool installed = InstallEntry(customization, index);
                if (index >= 0) RecordInstallAttempt(customization, index, installed, "InventoryDrag");
                if (installed)
                {
                    _looseParts.Remove(_activePart);
                    _entry.StorePart(_activePart, false);
                    _dragOriginPart = null;
                }
                else
                {
                    RejectPlacement(customization);
                    _dragOriginPart = null;
                }
            }
            _entry = null;
            _activePart = null;
            _fromInventory = false;
            _dragIcon.gameObject.SetActive(false);
            if (cabinetReturn != null) _keycapTray.CompletePartReturn(cabinetReturn);
        }

        /// <summary>
        /// customization 위에서 설치에 실패한 실물을 위쪽과 바깥쪽으로 튕겨 낙하시킨다.
        /// Inspector 속도를 적용하고 조립 인스턴스의 PCB와 보강판 충돌을 활성화하며 재고는 변경하지 않는다.
        /// </summary>
        private void RejectPlacement(KeyboardCustomization customization)
        {
            _activePart.gameObject.SetActive(true);
            foreach (Collider collider in _activePart.GetComponentsInChildren<Collider>()) collider.enabled = true;
            _activePart.DropWithPhysics();
            PrepareDetachedPartCollisions(_activePart, customization);
            Rigidbody body = _activePart.GetComponent<Rigidbody>();
            Vector3 up = customization.transform.up;
            Vector3 outward = Vector3.ProjectOnPlane(_activePart.transform.position - customization.transform.position, up);
            if (outward.sqrMagnitude < 0.000001f) outward = customization.transform.forward;
            outward.Normalize();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = up * _failedDropLiftSpeed + outward * _failedDropPushSpeed;
            body.angularVelocity = Vector3.Cross(up, outward) * _failedDropSpinSpeed;
        }

        /// <summary>
        /// part가 customization의 실제 부품들과 다시 충돌하도록 준비한다.
        /// PCB·보강판·상단부 충돌체를 준비하고 스위치·키캡을 포함한 충돌 무시를 해제하며 본체 집기용 충돌체는 제외한다.
        /// </summary>
        private void PrepareDetachedPartCollisions(AssemblyDraggablePart part, KeyboardCustomization customization)
        {
            Rigidbody body = part.GetComponent<Rigidbody>();
            if (!body.isKinematic) body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (!customization.TryGetComponent<KeyboardAssemblyState>(out var assembly) || !assembly.Initialized) return;
            Transform pcb = assembly.GetStructureParts(AssemblyStructurePart.Pcb)[0];
            EnableFailedDropCollision(pcb.Find("PCB_Board").GetComponent<MeshRenderer>(), part);
            EnableFailedDropCollision(customization.PlateRenderer, part);
            foreach (MeshRenderer upper in assembly.UpperRenderers)
                EnableFailedDropCollision(upper, part, true);
            foreach (Collider detached in part.GetComponentsInChildren<Collider>())
            foreach (Collider installed in customization.GetComponentsInChildren<Collider>())
            {
                if (!installed.enabled || installed.isTrigger || installed.transform == customization.transform
                    || installed.transform.IsChildOf(part.transform)) continue;
                Physics.IgnoreCollision(detached, installed, false);
            }
        }

        /// <summary>
        /// surface에 런타임 충돌체를 준비하고 part와의 충돌 무시를 해제한다.
        /// matchMesh가 켜진 상단부는 구멍을 유지하는 메시와 독립된 고정 Rigidbody를 사용한다.
        /// </summary>
        private void EnableFailedDropCollision(MeshRenderer surface, AssemblyDraggablePart part, bool matchMesh = false)
        {
            Collider target = surface.GetComponent<Collider>();
            if (target == null)
            {
                Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
                if (matchMesh)
                {
                    Rigidbody supportBody = surface.GetComponent<Rigidbody>();
                    if (supportBody == null) supportBody = surface.gameObject.AddComponent<Rigidbody>();
                    supportBody.isKinematic = true;
                    supportBody.useGravity = false;
                    MeshCollider shape = surface.gameObject.AddComponent<MeshCollider>();
                    shape.sharedMesh = mesh;
                    target = shape;
                }
                else
                {
                    BoxCollider box = surface.gameObject.AddComponent<BoxCollider>();
                    box.center = mesh.bounds.center;
                    box.size = mesh.bounds.size;
                    target = box;
                }
            }
            foreach (Collider collider in part.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(collider, target, false);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 실제 설치를 확정한 슬롯을 Customization 행동 이벤트로 전달한다.
        /// customization, index, 성공 여부와 설치 경로를 사용하며 일괄 실패는 호출하지 않는다.
        /// </summary>
        private void RecordInstallAttempt(KeyboardCustomization customization, int index, bool success, string actionSource)
        {
            string part = _entry.IsSwitch ? "Switch" : "Keycap";
            string elementId = GetEntryLogId(_entry);
            Game.Logging.CustomizationTelemetry.Emit(customization, "PartInstallAttempted", part, elementId,
                success: success, changed: success, targetId: customization.GetKeycapSlotId(index),
                actionSource: actionSource, outcomeReason: success ? "" : GetInstallFailureReason(customization, index),
                interactionId: actionSource == "BulkPlacement" ? _bulkInteractionId : "");
        }

        /// <summary>entry의 축 타입 또는 키캡 정의에서 로그용 부품 ID를 반환한다. 재고 상태는 변경하지 않는다.</summary>
        private static string GetEntryLogId(AssemblyCatalogEntry entry)
        {
            return entry.IsSwitch ? entry.SwitchData.Type.ToString()
                : entry.KeycapDefinition != null ? entry.KeycapDefinition.Id : "Keycap";
        }

        /// <summary>
        /// 실패한 재고 설치의 고정 사유 코드를 반환한다.
        /// customization의 슬롯 점유와 선행 스위치 상태를 읽으며 게임 상태는 변경하지 않는다.
        /// </summary>
        private string GetInstallFailureReason(KeyboardCustomization customization, int index)
        {
            if (_entry.IsSwitch)
                return customization.HasSwitchForKeycap(index) || customization.GetKeycapSlotTransform(index).gameObject.activeSelf
                    ? "SlotOccupied" : "PlacementRejected";
            if (customization.GetKeycapSlotTransform(index).gameObject.activeSelf) return "SlotOccupied";
            return customization.HasSwitchForKeycap(index) ? "PlacementRejected" : "PrerequisiteMissing";
        }

        /// <summary>
        /// 현재 UI 출고 실물을 보관하거나 월드 부품의 집기 전 상태를 복원한다.
        /// 확정 설치와 실물 소유는 유지하며 드래그 참조를 비운다. 반환값은 없다.
        /// </summary>
        public void CancelDrag()
        {
            if (_inventoryScrollOwner != null) _inventoryScrollOwner.CancelInventoryScroll();
            if (_dragIcon != null) _dragIcon.gameObject.SetActive(false);
            if (!IsDragging) return;
            if (_isBulkDragging) { EndBulkPlacement(true, "Cancelled"); return; }
            _previewRenderer.Hide();
            if (_fromInventory)
            {
                _looseParts.Remove(_activePart);
                _entry.StorePart(_activePart, false);
            }
            else if (_dragOriginPart != null)
            {
                if (_entry.IsSwitch) RestoreRemovedSwitch();
                else DropActivePart();
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
        }

        /// <summary>point의 설치 레이와 가림 검사를 수행해 슬롯 인덱스를 반환한다. 설치 확정과 안내가 같은 대상을 조회한다.</summary>
        private int FindInstallSlot(Vector2 point)
        {
            Physics.SyncTransforms();
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            Ray ray = InputCamera.ScreenPointToRay(point);
            if (Physics.Raycast(ray, out RaycastHit obstruction, InputCamera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !obstruction.transform.IsChildOf(customization.transform)) return -1;
            return customization.FindKeycapSlot(ray, _entry.IsSwitch);
        }

        /// <summary>현재 entry와 index의 점유·축 선행·규격 및 명시된 InstalledPart 점유로 설치 가능 여부를 반환한다. 수량과 부품 상태는 변경하지 않는다.</summary>
        private bool CanInstallEntry(int index)
        {
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
            if (index < 0 || index >= customization.KeycapCount) return false;
            Transform target = _entry.IsSwitch ? customization.GetSwitchSlotTransform(index) : customization.GetKeycapSlotTransform(index);
            AssemblyDraggablePart original = target.GetComponent<AssemblyDraggablePart>();
            if (original != null && original.Slot != null && original.Slot.InstalledPart != null) return false;
            if (_entry.IsSwitch) return customization.CanInstallSwitchFromInventory(index, _entry.SwitchData.Type);
            Material material = _entry.KeycapMaterial != null ? _entry.KeycapMaterial : _entry.KeycapDefinition.Material;
            return customization.CanInstallKeycapFromInventory(index, material, _entry.KeycapDefinition);
        }

        /// <summary>현재 카드의 entry가 출고 가능한지 반환한다. 등록된 원본과 기존 입력 및 드래그 조건만 읽고 수량은 검사하지 않는다.</summary>
        public bool CanBeginInventoryDrag(AssemblyCatalogEntry entry)
        {
            return _pullers.InputAllowed && !_pullers.IsPcbDragging && !_pullers.IsScrewPointerCaptured && !IsInventoryScrolling
                && (_keycapTray == null || (_keycapTray.CanInteractWithTray && _keycapTray.IsCategoryOpen(entry.IsSwitch ? WorkshopDrawerCategory.Switches : WorkshopDrawerCategory.Keycaps)))
                && Mouse.current != null && !IsDragging && entry != null && entry.Prefab != null
                && (entry.IsSwitch ? System.Array.IndexOf(_switchEntries, entry) : System.Array.IndexOf(_keycapEntries, entry)) >= 0
                && (_drawerLayout == null || !_drawerLayout.IsTransitioning);
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
            state.Title = _isBulkDragging ? "연속 배치" : _entry.IsSwitch ? "스위치 드래그" : "키캡 드래그";
            state.Add("마우스 이동", "부품 이동");
            if (_isBulkDragging)
            {
                if (!_pullers.IsPointerOverUI(point) && CanInstallEntry(FindInstallSlot(point)))
                    state.Add("좌클릭 유지", "키캡·스위치 연속 설치");
                state.Add("좌클릭 놓기", "연속 배치 종료");
            }
            else if (IsInReturnArea(point)) state.Add("좌클릭 놓기", "보관함에 넣기");
            else if (_dragOriginPart != null) state.Add("좌클릭 놓기", "부품 내려놓기");
            else if (_pullers.IsPointerOverUI(point)) state.Add("좌클릭 놓기", "취소");
            else state.Add("좌클릭 놓기", CanInstallEntry(FindInstallSlot(point)) ? "슬롯에 설치" : "부품 내려놓기");
            return true;
        }

        /// <summary>
        /// point가 현재 표시된 재료 목록 반환 영역 안인지 반환한다.
        /// 카메라 오버레이 좌표를 사용하며 재고와 설치 상태는 변경하지 않는다.
        /// </summary>
        private bool IsInReturnArea(Vector2 point)
        {
            return (_returnArea != null && (_keycapTray == null || _keycapTray.IsTrayVisible) && _returnArea.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_returnArea, point, null))
                || (_switchReturnArea != null && (_keycapTray == null || _keycapTray.IsTrayVisible) && _switchReturnArea.gameObject.activeInHierarchy
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
            KeyboardCustomization customization = EditingKeyboard.GetComponent<KeyboardCustomization>();
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
            SwitchType type = EditingKeyboard.GetComponent<KeyboardCustomization>().GetInstalledSwitchType(keyboardSwitch);
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
            PrepareDetachedPartCollisions(part, EditingKeyboard.GetComponent<KeyboardCustomization>());
            _lastRemovalFrame = Time.frameCount;
            if (!returnToInventory)
            {
                _dragOriginPart = removed;
                BeginWorldDrag(part, InGameCursor.Position);
                return;
            }
            part.DropWithPhysics();
            PrepareDetachedPartCollisions(part, EditingKeyboard.GetComponent<KeyboardCustomization>());
            PrepareSwitchClearance(part);
            float clearance = _switchClearancePlane.GetDistanceToPoint(part.transform.position);
            if (clearance < 0f) part.transform.position -= _switchClearancePlane.normal * clearance;
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
        /// deltaTime으로 시간을 누적하고 유효한 회수 예약의 실물을 보관 상태로 변경한다.
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
            PrepareDetachedPartCollisions(_activePart, EditingKeyboard.GetComponent<KeyboardCustomization>());
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
        /// 실물을 비활성 보관하고 회수 예약을 정리하며 수량은 변경하지 않는다.
        /// </summary>
        public void ReturnLoosePart(AssemblyDraggablePart part)
        {
            if (!_looseParts.Remove(part)) return;
            _extractedReturns.Remove(part);
            part.CatalogEntry.StorePart(part, false);
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
            icon.color = Color.white;
            icon.gameObject.SetActive(true);
            return icon;
        }

        /// <summary>
        /// 연출이 허용된 entry의 반환 목적지를 계산하며 새 키캡 트레이를 기존 카탈로그보다 우선한다.
        /// 화면 좌표를 반환하고 스위치와 기존 카테고리의 목적지 계산은 유지한다.
        /// </summary>
        public Vector2 GetReturnDestination(AssemblyCatalogEntry entry)
        {
            if (_keycapTray != null)
            {
                _keycapTray.TryGetReturnDestination(out Vector2 destination);
                return destination;
            }
            if (_drawerLayout != null) return _drawerLayout.GetReturnDestination();
            RectTransform area = entry.IsSwitch ? _switchReturnArea : _returnArea;
            return area != null && area.gameObject.activeInHierarchy
                ? RectTransformUtility.WorldToScreenPoint(null, area.TransformPoint(area.rect.center))
                : new Vector2(0f, Screen.height * 0.5f);
        }

        /// <summary>
        /// entry의 반환 연출이 현재 집중 상태와 목적지 표시 상태에서 가능한지 반환한다.
        /// 새 키캡 트레이가 닫혔거나 전환 중이면 false이며 재고와 반환 예약은 변경하지 않는다.
        /// </summary>
        public bool CanAnimateReturn(AssemblyCatalogEntry entry)
        {
            return IsEditing && (_keycapTray == null || (_keycapTray.IsCategoryOpen(entry.IsSwitch ? WorkshopDrawerCategory.Switches : WorkshopDrawerCategory.Keycaps) && _keycapTray.TryGetReturnDestination(out _)));
        }

        void OnDisable()
        {
            InGameCursor.PointerCanceled -= CancelDrag;
            CancelDrag();
        }
        void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
        void OnDestroy()
        {
            foreach (Texture2D texture in _switchPreviewTextures.Values) if (texture != null) Destroy(texture);
            foreach (KeyboardEditButton button in _singlePlacementButtons) button.Clicked -= HandlePlacementModeClicked;
            foreach (KeyboardEditButton button in _bulkPlacementButtons) button.Clicked -= HandlePlacementModeClicked;
            if (_dragIcon != null) Destroy(_dragIcon.gameObject);
            foreach (AssemblyDraggablePart part in _ownedParts) if (part != null) Destroy(part.gameObject);
        }
    }
}
