using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

using Game.Player;
using Game.Tournament;

namespace KeyboardModeling.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyCatalogUI : MonoBehaviour
    {
        private static readonly Color _panelColor = new Color32(37, 52, 68, 255);
        private static readonly Color _cardColor = new Color32(48, 66, 84, 255);
        private static readonly Color _selectedColor = new Color32(39, 80, 100, 255);
        private static readonly Color _accentColor = new Color32(87, 214, 243, 255);

        [Header("WorkShop2 Keycap Choices")]
        [SerializeField] private GameObject _previewKeycapPrefab;
        [SerializeField] private Material[] _keycapMaterials;
        [SerializeField] private KeycapDefinition[] _keycapDefinitions;

        [Header("WorkShop2 Keyboard Data")]
        [SerializeField] private GameObject _referenceKeyboardPrefab;
        [SerializeField] private SwitchData[] _switchData;
        [SerializeField] private Material[] _caseMaterials;
        [SerializeField] private PlateData[] _plateData;

        [Header("Assembly Material Targets")]
        [SerializeField] private MeshRenderer[] _caseRenderers = new MeshRenderer[0];
        [SerializeField] private MeshRenderer _plateRenderer;
        private readonly Dictionary<AssemblyCatalogButton, Material> _surfaceMaterials = new Dictionary<AssemblyCatalogButton, Material>();
        private int _caseCategory = -1;
        private int _plateCategory = -1;

        [Header("LED Customization")]
        [SerializeField] private AssemblyLEDSettings _ledSettings;
        private KeyboardLEDPanelUI _ledUI;
        private int _ledCategory = -1;

        [Header("Image Customization")]
        [SerializeField] private AssemblyImageCustomization _imageCustomization;
        [SerializeField] private AssemblyCustomizationStorage _customizationStorage;
        private int _imageCategory = -1;
        private KeyboardCustomizationTestUI _imageUI;

        [Header("Appearance")]
        [SerializeField] private Sprite _sprite;
        [SerializeField] private bool _startCollapsed;

        [Header("Catalog Drag")]
        [SerializeField] private AssemblyDragInput _dragInput;
        [SerializeField] private AssemblyCatalogEntry[] _spawnEntries = new AssemblyCatalogEntry[0];
        private readonly Dictionary<AssemblyCatalogButton, AssemblyCatalogEntry> _cardEntries = new Dictionary<AssemblyCatalogButton, AssemblyCatalogEntry>();
        private readonly Dictionary<AssemblyCatalogEntry, Texture2D> _entryTextures = new Dictionary<AssemblyCatalogEntry, Texture2D>();
        private readonly Dictionary<AssemblyCatalogEntry, Text> _quantityLabels = new Dictionary<AssemblyCatalogEntry, Text>();

        [Header("Original Esc Inventory Mapping")]
        [SerializeField] private AssemblyDraggablePart _originalKeycapInventoryPrefab;
        [SerializeField] private AssemblyDraggablePart _originalSwitchInventoryPrefab;

        [Header("Runtime UI")]
        private Font _font;
        private RectTransform _canvasRect;
        private RectTransform _returnArea;
        private RawImage _dragIcon;
        private int _currentCategory;

        private ScrollRect _scroll;
        private Text _heading;
        private GameObject _panel;
        private RectTransform _toggleRect;
        private AssemblyCatalogButton _toggleButton;
        private Text _toggleLabel;
        private AssemblyCatalogButton _singlePlacementButton;
        private AssemblyCatalogButton _bulkPlacementButton;
        private bool _bulkPlacement;
        private readonly List<RectTransform> _contents = new List<RectTransform>();
        private readonly List<AssemblyCatalogButton> _tabs = new List<AssemblyCatalogButton>();
        private readonly List<AssemblyCatalogButton> _cards = new List<AssemblyCatalogButton>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly Dictionary<AssemblyCatalogButton, int> _cardCategories = new Dictionary<AssemblyCatalogButton, int>();
        private readonly Dictionary<int, AssemblyCatalogButton> _selections = new Dictionary<int, AssemblyCatalogButton>();
        private readonly List<string> _categoryNames = new List<string>();

        public RectTransform ReturnArea => _returnArea;
        public int CurrentCategory => _currentCategory;
        public IReadOnlyList<AssemblyCatalogEntry> Entries => _spawnEntries;
        public IReadOnlyList<Material> Materials => _keycapMaterials;
        public IReadOnlyList<MeshRenderer> CaseRenderers => _caseRenderers;
        public MeshRenderer PlateRenderer => _plateRenderer;
        public AssemblyLEDSettings LEDSettings => _ledSettings;
        public event Action<int> CategorySelected;
        public event Action<AssemblyCatalogEntry> PartSelected;
        public event Action PanelOpened;
        public event Action BulkPlacementSelected;

        void Awake()
        {
            foreach (AssemblyCatalogEntry entry in _spawnEntries)
                entry.InitializeInventory();
            _dragInput.ConnectCatalog(this);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildLayout();
            BuildChoices();
            ShowCategory(0);
            RefreshInventory();
            if (_startCollapsed)
            {
                _panel.SetActive(false);
                _toggleLabel.text = "UI 펼치기";
                _toggleRect.anchoredPosition = new Vector2(20f, -20f);
            }
        }

        void Start()
        {
            foreach (AssemblyPartSlot slot in _dragInput.Slots)
                slot.OriginalPart.SetCatalogEntry(FindPrefabEntry(slot.InventoryPrefab));
        }

        void Update()
        {
            bool canChangeMode = !_dragInput.IsDragging;
            _singlePlacementButton.interactable = canChangeMode;
            _bulkPlacementButton.interactable = canChangeMode;

        }

        void OnDisable()
        {
            _dragInput.CancelDrag();
            if (_ledUI != null) _ledUI.Close();
            if (_imageUI != null) _imageUI.HideEmbedded();
        }

        void OnDestroy()
        {
            _toggleButton.Clicked -= HandleToggleClicked;
            _singlePlacementButton.Clicked -= HandlePlacementModeClicked;
            _bulkPlacementButton.Clicked -= HandlePlacementModeClicked;
            foreach (AssemblyCatalogButton tab in _tabs)
                tab.Clicked -= HandleTabClicked;
            foreach (AssemblyCatalogButton card in _cards)
            {
                card.Clicked -= HandleCardClicked;
                card.Pressed -= HandleCardPressed;
            }
            foreach (Texture2D texture in _textures)
                Destroy(texture);
        }

        /// <summary>
        /// 화면 크기에 맞춰 왼쪽 패널과 오른쪽 외부 탭을 담는 Canvas를 생성한다.
        /// 기존 EventSystem을 재사용하고 목록의 수직 스크롤과 스크롤바를 연결한다.
        /// </summary>
        private void BuildLayout()
        {
            GameObject canvasObject = new GameObject("AssemblyCatalogCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvasRect = (RectTransform)canvasObject.transform;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (EventSystem.current == null)
            {
                GameObject events = new GameObject("AssemblyCatalogEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasObject.transform, false);
                InGameCursor.ConfigureUI(events.GetComponent<InputSystemUIInputModule>());
            }

            RectTransform panel = Rect("CustomizationPanel", canvasObject.transform);
            _panel = panel.gameObject;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.offsetMin = new Vector2(20f, 20f);
            panel.offsetMax = new Vector2(340f, -20f);
            Background(panel, _panelColor);

            _toggleRect = Rect("CatalogToggle", canvasObject.transform);
            Place(_toggleRect, 348f, 20f, 140f, 44f);
            _toggleButton = CreateButton(_toggleRect);
            _toggleLabel = Label(_toggleRect, "UI 접기", 15);
            Stretch(_toggleLabel.rectTransform, new Vector2(6f, 4f), new Vector2(-6f, -4f));
            _toggleButton.Clicked += HandleToggleClicked;

            Text title = Label(panel, "커스터마이징", 23);
            Place(title.rectTransform, 18f, 18f, 284f, 34f);
            _heading = Label(panel, "", 14);
            _heading.color = _accentColor;
            Place(_heading.rectTransform, 18f, 62f, 284f, 26f);

            RectTransform singleMode = Rect("SinglePlacement", panel);
            Place(singleMode, 18f, 98f, 136f, 34f);
            _singlePlacementButton = CreateButton(singleMode);
            Text singleLabel = Label(singleMode, "한 개 배치", 14);
            Stretch(singleLabel.rectTransform, Vector2.zero, Vector2.zero);
            _singlePlacementButton.Clicked += HandlePlacementModeClicked;
            RectTransform bulkMode = Rect("BulkPlacement", panel);
            Place(bulkMode, 166f, 98f, 136f, 34f);
            _bulkPlacementButton = CreateButton(bulkMode);
            Text bulkLabel = Label(bulkMode, "연속 배치", 14);
            Stretch(bulkLabel.rectTransform, Vector2.zero, Vector2.zero);
            _bulkPlacementButton.Clicked += HandlePlacementModeClicked;
            SetSelected(_singlePlacementButton, true);
            SetSelected(_bulkPlacementButton, false);

            RectTransform scrollArea = Rect("PartList", panel);
            Stretch(scrollArea, new Vector2(14f, 16f), new Vector2(-14f, -146f));
            _scroll = scrollArea.gameObject.AddComponent<AssemblyCatalogScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 34f;
            RectTransform viewport = Rect("Viewport", scrollArea);
            Stretch(viewport, Vector2.zero, new Vector2(-17f, 0f));
            Background(viewport, _panelColor);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll.viewport = viewport;
            _returnArea = viewport;

            RectTransform track = Rect("VerticalScrollbar", scrollArea);
            track.anchorMin = new Vector2(1f, 0f);
            track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(8f, 0f);
            Background(track, new Color32(28, 40, 53, 255));
            Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
            RectTransform handle = Rect("Handle", track);
            Stretch(handle, Vector2.zero, Vector2.zero);
            scrollbar.targetGraphic = Background(handle, new Color32(75, 129, 151, 255));
            scrollbar.handleRect = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            _dragIcon = Rect("InventoryDragIcon", canvasObject.transform).gameObject.AddComponent<RawImage>();
            _dragIcon.raycastTarget = false;
            _dragIcon.rectTransform.sizeDelta = new Vector2(105f, 105f);
            _dragIcon.gameObject.SetActive(false);
        }

        /// <summary>
        /// button의 클릭으로 커스터마이징 패널과 하위 목록 및 탭의 활성 상태를 반전한다.
        /// 접기 버튼은 유지하고 위치와 문구를 갱신하며 기존 선택 및 스크롤 상태를 보존한다.
        /// </summary>
        private void HandleToggleClicked(AssemblyCatalogButton button)
        {
            if (!_dragInput.InputAllowed)
                return;
            _dragInput.CancelDrag();
            bool isExpanded = !_panel.activeSelf;
            _panel.SetActive(isExpanded);
            _toggleLabel.text = isExpanded ? "UI 접기" : "UI 펼치기";
            _toggleRect.anchoredPosition = new Vector2(isExpanded ? 348f : 20f, -20f);
            if (_ledUI != null)
            {
                if (isExpanded && _currentCategory == _ledCategory) _ledUI.OpenAssembly(_ledSettings);
                else _ledUI.Close();
            }
            if (isExpanded) PanelOpened?.Invoke();
            if (_imageUI != null)
            {
                if (isExpanded && _currentCategory == _imageCategory) _imageUI.BeginCustomization();
                else _imageUI.HideEmbedded();
            }
        }

        /// <summary>
        /// WorkShop2에서 복사한 에셋 참조로 키캡, 스위치, 케이스와 보강판 목록을 만든다.
        /// 정의가 있는 이름을 우선 사용하며 실제 에셋의 메시와 재질로 아이콘을 생성한다.
        /// </summary>
        private void BuildChoices()
        {
            Mesh shell = FindMesh(_previewKeycapPrefab, "PBT_SculptedShell");
            int category = AddCategory("키캡");
            foreach (Material material in _keycapMaterials)
            {
                KeycapDefinition definition = null;
                foreach (KeycapDefinition candidate in _keycapDefinitions)
                    if (candidate.ShellMesh == null && candidate.Material == material)
                        definition = candidate;
                AddCard(category, definition != null ? definition.DisplayName : material.name, shell,
                    new[] { material }, FindKeycapEntry(material, definition));
            }
            foreach (KeycapDefinition definition in _keycapDefinitions)
                if (definition.ShellMesh != null)
                    AddCard(category, definition.DisplayName, definition.ShellMesh, definition.ModelMaterials,
                        FindKeycapEntry(definition.Material, definition));

            category = AddCategory("스위치");
            Mesh switchMesh = FindMesh(_referenceKeyboardPrefab, "Switch_TopHousing");
            foreach (SwitchData data in _switchData)
                AddCard(category, data.Type.ToString(), switchMesh, new[] { data.TopHousingMaterial },
                    FindSwitchEntry(data));

            KeyboardCustomization keyboard = _referenceKeyboardPrefab.GetComponent<KeyboardCustomization>();
            if (_keycapMaterials.Length > 0)
            {
                category = AddCategory("케이스");
                _caseCategory = category;
                Mesh caseMesh = keyboard.CaseRenderers[0].GetComponent<MeshFilter>().sharedMesh;
                foreach (Material material in _keycapMaterials)
                    AddCard(category, material.name, caseMesh, new[] { material });
            }
            if (_keycapMaterials.Length > 0)
            {
                category = AddCategory("보강판");
                _plateCategory = category;
                Mesh plateMesh = keyboard.PlateRenderer.GetComponent<MeshFilter>().sharedMesh;
                foreach (Material material in _keycapMaterials)
                    AddCard(category, material.name, plateMesh, new[] { material });
            }
            if (_ledSettings != null)
            {
                _ledCategory = AddCategory("LED");
                RectTransform content = _contents[_ledCategory];
                content.GetComponent<GridLayoutGroup>().enabled = false;
                content.GetComponent<ContentSizeFitter>().enabled = false;
                RectTransform ledPanel = Rect("LEDCustomizationPanel", content);
                ledPanel.gameObject.SetActive(false);
                _ledUI = ledPanel.gameObject.AddComponent<KeyboardLEDPanelUI>();
                float height = _ledUI.InitializeEmbedded(content, 272f);
                content.sizeDelta = new Vector2(0f, height);
            }
            if (_imageCustomization != null) BuildImagePanel();
        }

        /// <summary>기존 사진 편집 UI를 왼쪽 목록의 이미지 탭에 구성한다. 입력값 없이 편집 대상과 닫기 이벤트를 연결한다.</summary>
        private void BuildImagePanel()
        {
            _imageCategory = AddCategory("이미지");
            RectTransform content = _contents[_imageCategory];
            content.GetComponent<GridLayoutGroup>().enabled = false;
            content.GetComponent<ContentSizeFitter>().enabled = false;
            RectTransform host = Rect("ImageCustomization", content);
            Place(host, 0f, 0f, 272f, 802f);
            _imageUI = host.gameObject.AddComponent<KeyboardCustomizationTestUI>();
            _customizationStorage.Initialize();
            float height = _imageUI.InitializeAssemblyEmbedded(_imageCustomization, host, 272f, _customizationStorage.Storage);
            host.sizeDelta = new Vector2(272f, height);
            content.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>
        /// label의 카테고리 탭과 2열 그리드 콘텐츠를 생성한다.
        /// 생성된 순서의 카테고리 인덱스를 반환하고 UI 참조를 저장한다.
        /// </summary>
        private int AddCategory(string label)
        {
            int index = _contents.Count;
            RectTransform content = Rect(label + "Content", _scroll.viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(131f, 148f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.gameObject.SetActive(false);
            _contents.Add(content);
            _categoryNames.Add(label);

            RectTransform panel = (RectTransform)_scroll.transform.parent;
            RectTransform tab = Rect(label + "Tab", panel);
            Place(tab, 328f, 104f + index * 72f, 88f, 60f);
            AssemblyCatalogButton button = CreateButton(tab);
            Text text = Label(tab, label, 15);
            Stretch(text.rectTransform, new Vector2(6f, 4f), new Vector2(-6f, -4f));
            button.Clicked += HandleTabClicked;
            _tabs.Add(button);
            return index;
        }

        /// <summary>
        /// category의 목록에 label과 mesh, materials를 표시하는 카드를 생성한다.
        /// entry가 있으면 재고 예약용 Down 이벤트를 연결하고 미리보기 및 선택 이벤트를 저장한다.
        /// </summary>
        private void AddCard(int category, string label, Mesh mesh, Material[] materials,
            AssemblyCatalogEntry entry = null)
        {
            RectTransform card = Rect(label, _contents[category]);
            AssemblyCatalogButton button = CreateButton(card);
            if (category == _caseCategory || category == _plateCategory)
                _surfaceMaterials.Add(button, materials[0]);
            Texture2D texture = AssemblyCatalogPreview.Render(mesh, materials);
            _textures.Add(texture);
            RawImage icon = Rect("PartIcon", card).gameObject.AddComponent<RawImage>();
            icon.texture = texture;
            icon.raycastTarget = false;
            Place(icon.rectTransform, 13f, 6f, 105f, 105f);
            Text text = Label(card, label, 13);
            Place(text.rectTransform, 6f, 112f, 119f, 32f);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 13;
            button.Clicked += HandleCardClicked;
            if (entry != null)
            {
                _cardEntries.Add(button, entry);
                _entryTextures.Add(entry, texture);
                Text quantity = Label(card, "", 14);
                Place(quantity.rectTransform, 87f, 7f, 38f, 24f);
                quantity.alignment = TextAnchor.MiddleRight;
                _quantityLabels.Add(entry, quantity);
                button.Pressed += HandleCardPressed;
            }
            _cards.Add(button);
            _cardCategories.Add(button, category);
        }

        /// <summary>
        /// material과 definition의 에셋 참조에 연결된 생성 항목을 반환한다.
        /// 연결되지 않은 키캡이면 null을 반환하며 표시 이름으로 검색하지 않는다.
        /// </summary>
        private AssemblyCatalogEntry FindKeycapEntry(Material material, KeycapDefinition definition)
        {
            foreach (AssemblyCatalogEntry entry in _spawnEntries)
                if (entry.MatchesKeycap(material, definition))
                    return entry;
            return null;
        }

        /// <summary>
        /// data의 에셋 참조에 연결된 스위치 생성 항목을 반환한다.
        /// 연결되지 않은 데이터는 null을 반환한다.
        /// </summary>
        private AssemblyCatalogEntry FindSwitchEntry(SwitchData data)
        {
            foreach (AssemblyCatalogEntry entry in _spawnEntries)
                if (entry.MatchesSwitch(data))
                    return entry;
            return null;
        }

        /// <summary>
        /// button의 entry 한 개를 예약하고 screenPosition에서 UI 아이콘 드래그를 시작한다.
        /// 시작이 거절되면 예약을 취소하며 월드 인스턴스 존재 여부로 수량을 제한하지 않는다.
        /// </summary>
        private void HandleCardPressed(AssemblyCatalogButton button, Vector2 screenPosition)
        {
            if (!_dragInput.InputAllowed)
                return;
            AssemblyCatalogEntry entry = _cardEntries[button];
            if (_bulkPlacement)
            {
                if (_dragInput.TryBeginBulkPlacement(entry, screenPosition))
                    HandleCardClicked(button);
                return;
            }
            if (_dragInput.IsDragging || !entry.TryReserve())
                return;
            if (!_dragInput.TryBeginInventoryDrag(entry, screenPosition))
                entry.FinishReservation(false);
            else
                HandleCardClicked(button);
            RefreshInventory();
        }

        /// <summary>
        /// button에 대응하는 한 개 또는 일괄 배치 모드를 선택한다.
        /// 드래그 중에는 변경을 거부하고 두 버튼의 선택 표시를 갱신한다.
        /// </summary>
        private void HandlePlacementModeClicked(AssemblyCatalogButton button)
        {
            if (!_dragInput.InputAllowed)
                return;
            if (_dragInput.IsDragging)
                return;
            _bulkPlacement = button == _bulkPlacementButton;
            SetSelected(_singlePlacementButton, !_bulkPlacement);
            SetSelected(_bulkPlacementButton, _bulkPlacement);
            if (_bulkPlacement)
                BulkPlacementSelected?.Invoke();
        }

        /// <summary>
        /// prefab의 직접 참조에 대응하는 보유 항목을 반환한다.
        /// 기존 실물은 슬롯의 Inspector 연결로만 식별하며 잘못된 연결은 설정 오류로 알린다.
        /// </summary>
        private AssemblyCatalogEntry FindPrefabEntry(AssemblyDraggablePart prefab)
        {
            foreach (AssemblyCatalogEntry entry in _spawnEntries)
                if (prefab != null && entry.Prefab == prefab)
                    return entry;
            throw new System.InvalidOperationException("슬롯의 인벤토리 Prefab 연결을 확인하십시오.");
        }

        /// <summary>
        /// visible에 따라 생성된 인벤토리 Canvas와 UI 레이캐스트만 전환한다.
        /// 재고, 선택, 접힘과 스크롤 상태 및 자식 EventSystem은 그대로 유지한다.
        /// </summary>
        public void SetInventoryVisible(bool visible)
        {
            if (_canvasRect == null)
                return;
            _canvasRect.GetComponent<Canvas>().enabled = visible;
            _canvasRect.GetComponent<GraphicRaycaster>().enabled = visible;
            EventSystem events = EventSystem.current;
            if (!visible && events != null && events.currentSelectedGameObject != null
                && events.currentSelectedGameObject.transform.IsChildOf(_canvasRect))
                events.SetSelectedGameObject(null);
        }

        /// <summary>
        /// screenPosition이 현재 열린 명시적 목록 Viewport 안인지 반환한다.
        /// 카드 수나 현재 카테고리와 무관하게 빈 목록도 반환 영역으로 취급한다.
        /// </summary>
        public bool IsInReturnArea(Vector2 screenPosition)
        {
            return _dragInput.InputAllowed && isActiveAndEnabled && _panel.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_returnArea, screenPosition, null);
        }

        /// <summary>
        /// entry의 기존 프리뷰를 screenPosition에 포인터를 막지 않는 UI 아이콘으로 표시한다.
        /// 화면 좌표를 Canvas 좌표로 변환하고 다른 UI 위에 아이콘을 유지한다.
        /// </summary>
        public void ShowDragIcon(AssemblyCatalogEntry entry, Vector2 screenPosition)
        {
            _dragIcon.texture = _entryTextures[entry];
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition, null, out Vector2 local);
            _dragIcon.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
            _dragIcon.gameObject.SetActive(true);
        }

        /// <summary>
        /// 드래그 종료나 월드 전환 시 임시 UI 아이콘을 숨긴다.
        /// 별도 입력 없이 기존 프리뷰 텍스처와 재고는 유지한다.
        /// </summary>
        public void HideDragIcon()
        {
            if (_dragIcon != null)
                _dragIcon.gameObject.SetActive(false);
        }

        /// <summary>entry의 기존 카탈로그 텍스처로 독립적인 반환 아이콘을 만든다. 반환값은 입력을 가로막지 않는 Canvas 자식이다.</summary>
        public RawImage CreateReturnIcon(AssemblyCatalogEntry entry)
        {
            RawImage icon = Rect("BoundaryReturnIcon", _canvasRect).gameObject.AddComponent<RawImage>();
            icon.texture = _entryTextures[entry];
            icon.raycastTarget = false;
            icon.rectTransform.sizeDelta = _dragIcon.rectTransform.sizeDelta;
            return icon;
        }

        /// <summary>worldPosition을 camera로 투영하고 icon 전체가 화면 안에 들어오는 Canvas 좌표를 반환한다. 뒤쪽 위치는 화면 중심 반대쪽 가장자리로 제한한다.</summary>
        public Vector2 GetReturnIconStart(RawImage icon, Camera camera, Vector3 worldPosition)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f)
            {
                Vector2 direction = new Vector2(Screen.width * 0.5f - screen.x, Screen.height * 0.5f - screen.y);
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector2.down;
                screen = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
                    + (Vector3)(direction.normalized * Mathf.Max(Screen.width, Screen.height));
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
            return ClampReturnIcon(icon, local);
        }

        /// <summary>현재 패널 상태에 따라 반환 목적지를 계산한다. 펼쳤으면 패널 중심, 접혔으면 화면 왼쪽 중앙의 Canvas 좌표를 반환한다.</summary>
        public Vector2 GetReturnIconDestination(RawImage icon)
        {
            Vector2 screen = _panel.activeInHierarchy
                ? RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)_panel.transform).TransformPoint(((RectTransform)_panel.transform).rect.center))
                : new Vector2(0f, Screen.height * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local);
            return ClampReturnIcon(icon, local);
        }

        /// <summary>icon의 크기와 화면 양끝을 Canvas 좌표로 계산한다. local을 여백 안으로 제한하여 아이콘 전체가 보이는 위치를 반환한다.</summary>
        public Vector2 ClampReturnIcon(RawImage icon, Vector2 local)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Vector2.zero, null, out Vector2 min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, new Vector2(Screen.width, Screen.height), null, out Vector2 max);
            Vector2 half = icon.rectTransform.rect.size * 0.5f + Vector2.one * 8f;
            return new Vector2(Mathf.Clamp(local.x, min.x + half.x, Mathf.Max(min.x + half.x, max.x - half.x)),
                Mathf.Clamp(local.y, min.y + half.y, Mathf.Max(min.y + half.y, max.y - half.y)));
        }

        /// <summary>
        /// entry의 한 개 예약을 consumed에 따라 확정하거나 취소한다.
        /// 초기 수량은 변경하지 않고 목록의 남은 수량 표시만 갱신한다.
        /// </summary>
        public void FinishReservation(AssemblyCatalogEntry entry, bool consumed)
        {
            entry.FinishReservation(consumed);
            RefreshInventory();
        }

        /// <summary>
        /// 각 항목의 예약을 제외한 재고 수량으로 카드 활성 상태와 수량 문구를 갱신한다.
        /// 기존 카드 순서와 카테고리를 유지하고 현재 스크롤의 픽셀 위치를 유효 범위 안에서 보존한다.
        /// </summary>
        public void RefreshInventory()
        {
            Vector2 position = _scroll.content.anchoredPosition;
            foreach (KeyValuePair<AssemblyCatalogButton, AssemblyCatalogEntry> pair in _cardEntries)
            {
                int quantity = pair.Value.AvailableQuantity;
                pair.Key.gameObject.SetActive(quantity > 0);
                _quantityLabels[pair.Value].text = "×" + quantity;
            }
            UpdateHeading();
            Canvas.ForceUpdateCanvases();
            float maximum = Mathf.Max(0f, _scroll.content.rect.height - _scroll.viewport.rect.height);
            _scroll.content.anchoredPosition = new Vector2(position.x, Mathf.Clamp(position.y, 0f, maximum));
        }

        /// <summary>
        /// 현재 카테고리에서 표시 중인 카드 수로 제목을 갱신한다.
        /// 비활성 재고 항목은 제외하며 카테고리와 스크롤은 변경하지 않는다.
        /// </summary>
        private void UpdateHeading()
        {
            int count = 0;
            foreach (Transform card in _contents[_currentCategory])
                if (card.gameObject.activeSelf)
                    count++;
            _heading.text = _categoryNames[_currentCategory] + "  ·  " + count;
        }

        /// <summary>
        /// button의 카테고리를 찾아 해당 목록과 탭 강조를 표시한다.
        /// 선택한 카테고리로 수직 스크롤을 초기화한다.
        /// </summary>
        private void HandleTabClicked(AssemblyCatalogButton button)
        {
            if (!_dragInput.InputAllowed)
                return;
            int index = _tabs.IndexOf(button);
            ShowCategory(index);
            CategorySelected?.Invoke(index);
        }

        /// <summary>
        /// button이 속한 카테고리의 이전 선택을 해제하고 새 카드를 강조한다.
        /// 케이스와 보강판 카드는 연결된 씬 렌더러에 재질을 적용하고 키캡과 축은 기존 선택 이벤트를 유지한다.
        /// </summary>
        private void HandleCardClicked(AssemblyCatalogButton button)
        {
            if (!_dragInput.InputAllowed)
                return;
            int category = _cardCategories[button];
            if (_selections.TryGetValue(category, out AssemblyCatalogButton previous))
                SetSelected(previous, false);
            _selections[category] = button;
            SetSelected(button, true);
            if (_surfaceMaterials.TryGetValue(button, out Material material))
            {
                if (category == _caseCategory)
                {
                    foreach (MeshRenderer renderer in _caseRenderers)
                        renderer.sharedMaterial = material;
                }
                else
                    _plateRenderer.sharedMaterial = material;
            }
            if (_cardEntries.TryGetValue(button, out AssemblyCatalogEntry entry)) PartSelected?.Invoke(entry);
        }

        /// <summary>
        /// index의 카테고리를 활성화하고 제목과 탭 색을 동기화한다.
        /// 다른 카테고리는 숨기고 새 목록의 스크롤을 맨 위로 이동한다.
        /// </summary>
        private void ShowCategory(int index)
        {
            for (int i = 0; i < _contents.Count; i++)
            {
                _contents[i].gameObject.SetActive(i == index);
                SetSelected(_tabs[i], i == index);
            }
            _scroll.content = _contents[index];
            _currentCategory = index;
            bool isLED = index == _ledCategory;
            bool isImage = index == _imageCategory;
            if (_imageUI != null)
            {
                if (isImage) _imageUI.BeginCustomization();
                else _imageUI.HideEmbedded();
            }
            _scroll.gameObject.SetActive(true);
            _singlePlacementButton.gameObject.SetActive(!isLED && !isImage);
            _bulkPlacementButton.gameObject.SetActive(!isLED && !isImage);
            if (_ledUI != null)
            {
                if (isLED) _ledUI.OpenAssembly(_ledSettings);
                else _ledUI.Close();
            }
            UpdateHeading();
            Canvas.ForceUpdateCanvases();
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// button에 selected 상태의 배경과 테두리 색을 적용한다.
        /// 포인터 강조 색과 선택 테두리를 갱신하여 UI 선택을 표시한다.
        /// </summary>
        private void SetSelected(AssemblyCatalogButton button, bool selected)
        {
            button.GetComponent<Image>().color = selected ? _selectedColor : _cardColor;
            button.GetComponent<Outline>().effectColor = selected ? _accentColor : new Color32(80, 103, 122, 255);
        }

        /// <summary>
        /// rect에 카탈로그 버튼 배경과 테두리를 추가하고 버튼 참조를 반환한다.
        /// 키보드 포커스가 UI 탐색으로 이동하지 않도록 탐색을 비활성화한다.
        /// </summary>
        private AssemblyCatalogButton CreateButton(RectTransform rect)
        {
            Image image = Background(rect, _cardColor);
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1f, -1f);
            outline.effectColor = new Color32(80, 103, 122, 255);
            AssemblyCatalogButton button = rect.gameObject.AddComponent<AssemblyCatalogButton>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
            button.colors = colors;
            return button;
        }

        /// <summary>
        /// parent 아래 name의 RectTransform을 생성하여 반환한다.
        /// 로컬 배치를 유지하고 UI 레이어를 적용한다.
        /// </summary>
        private RectTransform Rect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// rect에 color와 기존 UI 스프라이트를 적용한 Image를 생성하여 반환한다.
        /// 배경 영역은 UI 입력을 받아 뒤쪽 부품 클릭을 차단한다.
        /// </summary>
        private Image Background(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = _sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        /// <summary>
        /// parent 아래 text와 size를 사용하는 기존 폰트 라벨을 생성하여 반환한다.
        /// 라벨의 입력 판정은 해제하고 가운데 정렬을 적용한다.
        /// </summary>
        private Text Label(Transform parent, string text, int size)
        {
            Text label = Rect("Label", parent).gameObject.AddComponent<Text>();
            label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// rect를 부모의 왼쪽 위에서 x, y만큼 떨어진 width, height 영역에 배치한다.
        /// 앵커, 피벗과 크기를 변경한다.
        /// </summary>
        private void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// rect를 부모 영역에 늘리고 min, max 여백을 적용한다.
        /// 화면 크기에 따라 해당 UI 영역의 크기를 갱신한다.
        /// </summary>
        private void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        /// <summary>
        /// prefab 안에서 name과 일치하는 MeshFilter의 공유 메시를 반환한다.
        /// 일치하는 메시가 없으면 설정 오류로 작업을 중단한다.
        /// </summary>
        private Mesh FindMesh(GameObject prefab, string name)
        {
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == name)
                    return filter.sharedMesh;
            throw new System.InvalidOperationException(prefab.name + "에 " + name + " 메시가 없습니다.");
        }

        /// <summary>
        /// type에 대응하는 WorkShop2 보강판 표시 이름을 반환한다.
        /// 별도의 새 부품 이름이나 데이터를 생성하지 않는다.
        /// </summary>
        private string PlateName(PlateType type)
        {
            switch (type)
            {
                case PlateType.Nomal: return "기본";
                case PlateType.Golden: return "골드";
                case PlateType.UnderWater: return "아쿠아";
                case PlateType.RainBow: return "레인보우";
                case PlateType.Steel: return "스틸";
                default: return type.ToString();
            }
        }
    }
}
