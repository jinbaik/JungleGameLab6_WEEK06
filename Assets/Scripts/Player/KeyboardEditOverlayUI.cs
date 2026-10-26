using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

using Game.Tournament;
using KeyboardModeling.Assembly;

namespace Game.Player
{
    public sealed partial class KeyboardEditOverlayUI : MonoBehaviour, IPointerDownHandler
    {
        private const int PREVIEW_LAYER = 31;
        private const int PREVIEW_TEXTURE_SIZE = 128;
        private const float WHEEL_SCROLL_INPUT_SCALE = 20f;
        private const float PICKER_PANEL_HEIGHT = 164f;
        private const float PICKER_PANEL_PADDING = 12f;
        private const float PICKER_LEFT_ITEM_PADDING = 24f;
        private const float PICKER_ITEM_HEIGHT = 128f;
        private const float PICKER_PREVIEW_WIDTH = 80f;
        private const float PICKER_PREVIEW_HEIGHT = 72f;
        private const float PICKER_SCROLLBAR_HEIGHT = 6f;
        private const float PLATE_PICKER_TAB_TOP_PADDING = 7f;
        private const float PLATE_PICKER_TAB_HEIGHT = 28f;
        private const float PLATE_PICKER_ITEM_HEIGHT = 108f;
        private const float PLATE_PICKER_PREVIEW_WIDTH = 68f;
        private const float PLATE_PICKER_PREVIEW_HEIGHT = 60f;

        private Color _pickerPanelColor = new Color32(37, 52, 68, 255);
        private Color _pickerPanelBorderColor = new Color32(69, 91, 112, 255);
        private Color _pickerBorderColor = new Color32(80, 103, 122, 255);
        private Color _pickerHoverColor = new Color32(115, 220, 247, 255);
        private Color _pickerFocusColor = new Color32(128, 234, 255, 255);
        private Color _pickerSelectedColor = new Color32(87, 214, 243, 255);
        private Color _pickerCardColor = new Color32(48, 66, 84, 255);
        private Color _pickerSelectedCardColor = new Color32(39, 80, 100, 255);
        private Color _pickerScrollbarColor = new Color32(75, 129, 151, 255);
        private static readonly SwitchType[] _switchSoundTypes = (SwitchType[])Enum.GetValues(typeof(SwitchType));

        [Header("Edit Buttons")]
        [SerializeField] private KeyboardEditButton _keycapButton;
        [SerializeField] private KeyboardEditButton _switchAxisButton;
        [SerializeField] private KeyboardEditButton _ledButton;
        [SerializeField] private KeyboardEditButton _caseButton;
        [SerializeField] private KeyboardEditButton _plateButton;
        [SerializeField] private KeyboardEditButton _customButton;
        [SerializeField] private bool _showKeycapsOnOpen;
        [SerializeField] private WorkshopKeyboardPullerController _pullerController;

        [Header("Mode Buttons")]
        [SerializeField] private KeyboardEditButton _generalModeButton;
        [SerializeField] private KeyboardEditButton _attackModeButton;
        [SerializeField] private Text _modeStatus;
        private PickerMode _pickerMode;

        [Header("Keycap Material Picker")]
        [SerializeField] private GameObject _previewKeycapPrefab;
        [SerializeField] private Material[] _keycapMaterials = new Material[0];
        [SerializeField] private Material[] _pcbMaterials = new Material[0];
        [SerializeField] private KeycapDefinition[] _keycapDefinitions = new KeycapDefinition[0];
        [SerializeField, Min(1f)] private float _viewportWidth = 860f;
        [SerializeField, Min(1f)] private float _itemWidth = 116f;
        [SerializeField, Min(0f)] private float _itemSpacing = 11f;
        [SerializeField, Min(0f)] private float _wheelScrollSpeed = 0.55f;

        private Canvas _canvas;
        private Sprite _pickerSprite;
        private Font _pickerFont;
        private RectTransform _keyboardEditPanel;
        private RectTransform _viewport;
        private RectTransform _content;
        private readonly List<KeyboardEditButton> _previewButtons = new List<KeyboardEditButton>();
        private readonly List<Material> _previewMaterials = new List<Material>();
        private readonly List<Image> _previewCardBackgrounds = new List<Image>();
        private readonly List<Text> _selectionIndicators = new List<Text>();
        private readonly List<Texture2D> _previewTextures = new List<Texture2D>();
        private readonly List<KeycapDefinition> _previewDefinitions = new List<KeycapDefinition>();
        private Material _selectedMaterial;
        private KeycapDefinition _selectedKeycapDefinition;

        [Header("Switch Sound Picker")]
        private RectTransform _switchSoundViewport;
        private readonly List<KeyboardEditButton> _switchSoundButtons = new List<KeyboardEditButton>();
        private readonly List<Image> _switchSoundCardBackgrounds = new List<Image>();
        private readonly List<Text> _switchSoundSelectionIndicators = new List<Text>();
        private SwitchType? _selectedSwitchSound;

        [Header("Case Material Picker")]
        private KeyboardCustomization _caseCustomization;
        private RectTransform _caseMaterialViewport;
        private readonly List<KeyboardEditButton> _casePreviewButtons = new List<KeyboardEditButton>();
        private readonly List<Image> _caseCardBackgrounds = new List<Image>();
        private readonly List<int> _caseMaterialIndices = new List<int>();
        private readonly List<Text> _caseSelectionIndicators = new List<Text>();
        private readonly List<Texture2D> _casePreviewTextures = new List<Texture2D>();

        [Header("Plate Picker")]
        private KeyboardCustomization _plateCustomization;
        private RectTransform _plateViewport;
        private RectTransform _plateMaterialContent;
        private RectTransform _plateEffectContent;
        private KeyboardEditButton _plateMaterialTabButton;
        private KeyboardEditButton _plateEffectTabButton;
        private PlatePickerSection _platePickerSection;
        private readonly List<KeyboardEditButton> _plateMaterialButtons = new List<KeyboardEditButton>();
        private readonly List<Image> _plateMaterialCardBackgrounds = new List<Image>();
        private readonly List<Material> _plateMaterials = new List<Material>();
        private readonly List<Material> _plateMaterialButtonMaterials = new List<Material>();
        private readonly List<string> _plateMaterialLabels = new List<string>();
        private readonly List<Text> _plateMaterialSelectionIndicators = new List<Text>();
        private readonly List<KeyboardEditButton> _plateEffectButtons = new List<KeyboardEditButton>();
        private readonly List<Image> _plateEffectCardBackgrounds = new List<Image>();
        private readonly List<PlateEffect> _plateEffects = new List<PlateEffect>();
        private readonly List<Text> _plateEffectSelectionIndicators = new List<Text>();
        private readonly List<Texture2D> _platePreviewTextures = new List<Texture2D>();

        [Header("Photo Customization")]
        [SerializeField] private KeyboardCustomizationStorage _photoStorage;
        [SerializeField, Range(0.01f, 0.2f)]
        [Tooltip("휠 한 칸당 확대·축소 비율입니다. 0.03은 한 번에 약 3% 변하며 플레이 중 변경도 적용됩니다.")]
        private float _photoWheelZoomSensitivity = 0.03f;
        private KeyboardCustomizationTestUI _photoUI;
        private KeyboardKeycapPanelUI _keycapUI;
        private WorkshopKeycapPlacement _keycapPlacement;
        private WorkshopVerticalCatalogLayout _catalogLayout;
        private bool _hasPendingKeycapMaterial;
        private bool _hasActiveKeycapMaterialPreview;
        private float _appliedPhotoWheelZoomSensitivity;

        [Header("LED Customization")]
        private KeyboardCustomization _ledCustomization;
        private KeyboardLEDPanelUI _ledUI;

        public event Action<SwitchType> SwitchSoundSelected;
        public event Action<Material> CaseMaterialSelected;
        public event Action<Material> PlateMaterialSelected;
        public event Action<PlateEffect> PlateEffectSelected;
        public event Action<bool> AttackModeSelected;

        private enum PlatePickerSection
        {
            Material,
            Effect
        }

        private enum PickerMode
        {
            None,
            Keycap,
            SwitchSound,
            CaseMaterial,
            Plate,
            Photo,
            Led,
            Pcb,
            Upper
        }

        void Awake()
        {
            _keycapPlacement = GetComponent<WorkshopKeycapPlacement>();
            _catalogLayout = GetComponent<WorkshopVerticalCatalogLayout>();
            if (_catalogLayout != null)
            {
                _pickerPanelColor = new Color32(225, 204, 176, 255);
                _pickerPanelBorderColor = new Color32(148, 111, 77, 255);
                _pickerBorderColor = new Color32(157, 122, 87, 255);
                _pickerCardColor = new Color32(193, 160, 124, 255);
                _pickerSelectedCardColor = new Color32(146, 107, 69, 255);
                _pickerSelectedColor = new Color32(255, 231, 188, 255);
                _pickerScrollbarColor = new Color32(130, 92, 59, 255);
            }
            _canvas = GetComponentInParent<Canvas>();
            _pickerSprite = (_keycapButton.targetGraphic as Image).sprite;
            _pickerFont = _switchAxisButton.GetComponentInChildren<Text>().font;
            _keyboardEditPanel = transform.Find("KeyboardEditPanel") as RectTransform;
            CreateMaterialPicker();
            CreateSwitchSoundPicker();
            _photoUI = gameObject.AddComponent<KeyboardCustomizationTestUI>();
            _photoUI.InitializeEmbedded(_photoStorage, _photoWheelZoomSensitivity);
            _appliedPhotoWheelZoomSensitivity = Mathf.Clamp(_photoWheelZoomSensitivity, 0.01f, 0.2f);
            _photoUI.CloseRequested += HandlePhotoCloseRequested;
            _photoUI.CustomizationLoaded += HandlePhotoCustomizationLoaded;
            GameObject keycapPanel = new GameObject("KeycapCustomizationPanel", typeof(RectTransform));
            keycapPanel.transform.SetParent(transform, false);
            keycapPanel.SetActive(false);
            _keycapUI = keycapPanel.AddComponent<KeyboardKeycapPanelUI>();
            _keycapUI.Initialize();
            _keycapUI.TargetChanged += HandleKeycapTargetChanged;
            _keycapUI.KeyInputCaptured += HandleKeycapKeyInputCaptured;
            _keycapUI.UndoRequested += HandleKeycapUndoRequested;
            _keycapUI.ApplyRequested += HandleKeycapApplyRequested;
            GameObject ledPanel = new GameObject("LEDCustomizationPanel", typeof(RectTransform));
            ledPanel.transform.SetParent(transform, false);
            ledPanel.SetActive(false);
            _ledUI = ledPanel.AddComponent<KeyboardLEDPanelUI>();
            _ledUI.Initialize();
            if (_assemblyCatalog) BuildAssemblyControls();
        }

        void LateUpdate()
        {
            if (_keycapTrayGrid != null) UpdateKeycapTrayWidth();
            if (_assemblyCatalog) RefreshAssemblyControls();
            if (_photoUI == null)
                return;

            float sensitivity = Mathf.Clamp(_photoWheelZoomSensitivity, 0.01f, 0.2f);
            if (Mathf.Approximately(sensitivity, _appliedPhotoWheelZoomSensitivity))
                return;

            _photoUI.SetWheelZoomSensitivity(sensitivity);
            _appliedPhotoWheelZoomSensitivity = sensitivity;
        }

        void OnEnable()
        {
            SetEditorTrayBindings(true);
            CreateCaseMaterialPicker();
            CreatePlatePicker();
            _keycapButton.Clicked += HandleCategoryClicked;
            _switchAxisButton.Clicked += HandleCategoryClicked;
            _ledButton.Clicked += HandleCategoryClicked;
            _caseButton.Clicked += HandleCategoryClicked;
            _plateButton.Clicked += HandleCategoryClicked;
            _customButton.Clicked += HandleCategoryClicked;
            _generalModeButton.Clicked += HandleGeneralModeClicked;
            _attackModeButton.Clicked += HandleAttackModeClicked;
            foreach (KeyboardEditButton button in _previewButtons)
            {
                button.Clicked += HandlePreviewClicked;
            }

            foreach (KeyboardEditButton button in _switchSoundButtons)
            {
                button.Clicked += HandleSwitchSoundClicked;
            }

            foreach (KeyboardEditButton button in _casePreviewButtons)
            {
                button.Clicked += HandleCasePreviewClicked;
            }

            SetPlatePickerEventBindings(true);

            SetPickerMode(_showKeycapsOnOpen ? (_assemblyCatalog ? PickerMode.CaseMaterial : PickerMode.Keycap) : PickerMode.None);
        }

        void OnDisable()
        {
            SetEditorTrayBindings(false);
            Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "CustomizationEnd", success: false, details: "OverlayDisabled");
            _keycapButton.Clicked -= HandleCategoryClicked;
            _switchAxisButton.Clicked -= HandleCategoryClicked;
            _ledButton.Clicked -= HandleCategoryClicked;
            _caseButton.Clicked -= HandleCategoryClicked;
            _plateButton.Clicked -= HandleCategoryClicked;
            _customButton.Clicked -= HandleCategoryClicked;
            _generalModeButton.Clicked -= HandleGeneralModeClicked;
            _attackModeButton.Clicked -= HandleAttackModeClicked;
            foreach (KeyboardEditButton button in _previewButtons)
            {
                button.Clicked -= HandlePreviewClicked;
            }

            foreach (KeyboardEditButton button in _switchSoundButtons)
            {
                button.Clicked -= HandleSwitchSoundClicked;
            }

            foreach (KeyboardEditButton button in _casePreviewButtons)
            {
                button.Clicked -= HandleCasePreviewClicked;
            }

            SetPlatePickerEventBindings(false);

            if (_switchSoundViewport != null && _photoUI != null && _ledUI != null && _keycapUI != null)
                SetPickerMode(PickerMode.None);
        }

        void OnDestroy()
        {
            if (_assemblyCatalog) ReleaseAssemblyControls();
            _photoUI.CloseRequested -= HandlePhotoCloseRequested;
            _photoUI.CustomizationLoaded -= HandlePhotoCustomizationLoaded;
            _keycapUI.TargetChanged -= HandleKeycapTargetChanged;
            _keycapUI.KeyInputCaptured -= HandleKeycapKeyInputCaptured;
            _keycapUI.UndoRequested -= HandleKeycapUndoRequested;
            _keycapUI.ApplyRequested -= HandleKeycapApplyRequested;
            DestroyPreviewTextures(_previewTextures);
            DestroyPreviewTextures(_casePreviewTextures);
            DestroyPreviewTextures(_platePreviewTextures);
            DestroyPreviewTextures(_structurePreviewTextures);
            DestroyPreviewTextures(_toolPreviewTextures);
        }

        /// <summary>
        /// 접기 버튼을 사용하는 세로 목록은 빈 영역 클릭에도 현재 표시 상태를 유지한다.
        /// 기존 가로 UI는 eventData 위치로 외부 클릭을 검사해 선택 목록을 닫는다. 반환값은 없다.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_catalogLayout != null) return;
            if (_pullerController != null && _pullerController.IsPullerInteraction(eventData.position)) return;
            if (eventData.button == PointerEventData.InputButton.Left
                && !ContainsPointer(_viewport, eventData.position)
                && !ContainsPointer(_switchSoundViewport, eventData.position)
                && !ContainsPointer(_caseMaterialViewport, eventData.position)
                && !ContainsPointer(_plateViewport, eventData.position)
                && !_photoUI.IsPointerOverUI(eventData.position)
                && !_keycapUI.IsPointerOverUI(eventData.position)
                && !_ledUI.IsPointerOverUI(eventData.position))
            {
                SetPickerMode(PickerMode.None);
            }
        }

        /// <summary>
        /// 선택한 재질을 저장해 미리보기의 선택 표시를 갱신한다.
        /// material은 현재 키보드 재질이며 렌더링된 항목과 같으면 표시를 켠다.
        /// </summary>
        public void SetSelectedMaterial(Material material)
        {
            _selectedMaterial = material;
            _selectedKeycapDefinition = null;
            RefreshSelectionIndicators();
        }

        /// <summary>
        /// 키보드에 적용된 축 사운드를 저장하고 목록의 선택 색상을 갱신한다.
        /// type을 사용하며 null이면 모든 사운드 항목을 선택되지 않은 상태로 표시한다.
        /// </summary>
        public void SetSelectedSwitchSound(SwitchType? type)
        {
            _selectedSwitchSound = type;
            RefreshSwitchSoundSelection();
        }

        /// <summary>entry에 대응하는 키캡 또는 스위치 카드의 선택 강조를 갱신한다. 출고 항목의 재질·정의·축 타입만 읽으며 실제 설치와 사운드 및 재고는 변경하지 않는다.</summary>
        public void SelectInventoryCard(AssemblyCatalogEntry entry)
        {
            if (entry.IsSwitch)
            {
                SetSelectedSwitchSound(entry.SwitchData.Type);
                return;
            }
            _selectedMaterial = entry.KeycapMaterial;
            _selectedKeycapDefinition = entry.KeycapDefinition;
            RefreshSelectionIndicators();
        }

        /// <summary>
        /// 편집할 키보드의 케이스 재질과 미리보기 원본을 지정한다.
        /// customization을 저장하며 다음 활성화 시 해당 키보드의 목록을 만든다.
        /// </summary>
        public void SetCaseCustomization(KeyboardCustomization customization)
        {
            if (_caseCustomization == customization)
                return;
            if (UsesScreenTrays)
            {
                _caseCustomization = customization;
                return;
            }
            if (isActiveAndEnabled)
                foreach (KeyboardEditButton button in _casePreviewButtons)
                    button.Clicked -= HandleCasePreviewClicked;
            _caseCustomization = customization;
            if (isActiveAndEnabled)
            {
                CreateCaseMaterialPicker();
                foreach (KeyboardEditButton button in _casePreviewButtons)
                    button.Clicked += HandleCasePreviewClicked;
            }
        }

        /// <summary>
        /// visible에 따라 기존 작업실 목록을 열거나 닫고 진행 중인 UI 미리보기를 정리한다.
        /// GameObject는 비활성화하지 않아 재고와 분리 부품 회수 코루틴을 유지한다.
        /// </summary>
        public void SetWorkshopVisible(bool visible)
        {
            if (_catalogLayout != null) _catalogLayout.ResetDrawers(visible);
            SetPickerMode(_catalogLayout == null && visible && _showKeycapsOnOpen ? PickerMode.Keycap : PickerMode.None);
            if (visible && _keycapTrayTransition != null && _keycapTrayTransition.IsTrayOpen)
                HandleEditorTrayStateChanged(_keycapTrayTransition.Category, true);
            if (visible)
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "WorkshopOpened");
        }

        /// <summary>
        /// 보강판 색상과 효과 선택 UI가 사용할 편집 키보드를 지정한다.
        /// customization의 재질과 효과 목록을 카드 구성과 독립 선택 표시에 사용한다.
        /// </summary>
        public void SetPlateCustomization(KeyboardCustomization customization)
        {
            if (_plateCustomization == customization)
                return;

            bool reusePicker = UsesScreenTrays && _plateViewport != null && _plateCustomization != null
                && customization != null && _plateCustomization.PlateRenderer != null && customization.PlateRenderer != null
                && _plateCustomization.PlateRenderer.GetComponent<MeshFilter>().sharedMesh
                    == customization.PlateRenderer.GetComponent<MeshFilter>().sharedMesh
                && _plateEffects.Count == customization.AvailablePlateEffects.Count;
            if (reusePicker)
                for (int index = 0; index < _plateEffects.Count; index++)
                    if (_plateEffects[index] != customization.AvailablePlateEffects[index]) { reusePicker = false; break; }
            _plateCustomization = customization;
            RefreshStructureTrayPreviews();
            if (!isActiveAndEnabled)
                return;
            if (reusePicker)
            {
                RefreshPlateSelection();
                return;
            }

            SetPlatePickerEventBindings(false);
            CreatePlatePicker();
            SetPlatePickerEventBindings(true);
        }

        /// <summary>
        /// customization을 기존 사진 편집 UI의 저장 및 복원 대상으로 연결한다.
        /// 작업대에서 편집하는 키보드만 변경되며 단일 저장 파일과 재질 카탈로그는 유지한다.
        /// </summary>
        public void SetPhotoCustomization(KeyboardCustomization customization)
        {
            if (_photoStorage.Customization != customization)
                CancelKeycapMaterialPreview();
            _photoStorage.SetTarget(customization);
        }

        /// <summary>
        /// customization을 LED 편집 대상으로 지정한다.
        /// 현재 작업대 키보드 참조만 저장하고 실제 설정 읽기와 입력 중지는 LED 패널을 열 때 수행한다.
        /// </summary>
        public void SetLEDCustomization(KeyboardCustomization customization)
        {
            _ledCustomization = customization;
        }

        /// <summary>
        /// 공격 모드 또는 일반 모드 버튼의 선택 강조와 상태 문구를 갱신한다.
        /// attackModeSelected를 사용하며 선택된 버튼의 배경과 아이콘 및 선택 점을 변경한다.
        /// </summary>
        public void SetSelectedAttackMode(bool attackModeSelected)
        {
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive)
            {
                _modeStatus.text = "키보드 커스터마이징";
                return;
            }
            _generalModeButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(!attackModeSelected);
            _attackModeButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(attackModeSelected);
            _modeStatus.text = attackModeSelected
                ? "현재 모드: 공격 모드"
                : "현재 모드: 일반 모드";
        }

        /// <summary>
        /// 사진 편집 완료 요청을 받아 오른쪽 패널과 상단 저장 슬롯을 닫는다.
        /// 별도 입력 없이 선택 모드를 해제하고 기존 키보드 입력 상태를 복원한다.
        /// </summary>
        private void HandlePhotoCloseRequested()
        {
            if (_keycapTrayContent != null && _keycapTrayTransition.IsCategoryOpen(WorkshopDrawerCategory.LedCustomImage))
                _keycapTrayTransition.RequestClose();
            else if (_catalogLayout != null) _catalogLayout.RequestDrawer(7);
            else SetPickerMode(PickerMode.None);
        }

        /// <summary>
        /// 사진 저장 상태를 복원한 키보드의 재질 선택 표시를 갱신한다.
        /// 현재 저장 대상의 키캡 및 케이스 선택을 사용하며 사진 편집 모드는 유지한다.
        /// </summary>
        private void HandlePhotoCustomizationLoaded()
        {
            RefreshKeycapMaterialSelection();
            RefreshCaseMaterialSelection();
            RefreshPlateSelection();
        }

        /// <summary>
        /// 키캡 적용 범위가 바뀌면 선택 재질의 미리보기를 새 범위에 맞춰 갱신한다.
        /// allKeys와 keycapIndex를 사용하며 적용 전 상태는 스냅샷에서 다시 구성한다.
        /// </summary>
        private void HandleKeycapTargetChanged(bool allKeys, int keycapIndex)
        {
            KeyboardCustomization customization = _photoStorage.Customization;
            if (_hasPendingKeycapMaterial)
            {
                bool targetIsValid = allKeys || keycapIndex >= 0;
                if (!targetIsValid)
                {
                    customization.CancelKeycapMaterialPreview();
                    _hasActiveKeycapMaterialPreview = false;
                }
                else
                {
                    _hasActiveKeycapMaterialPreview = customization.PreviewKeycapMaterial(
                        _selectedMaterial, allKeys, keycapIndex, _selectedKeycapDefinition);
                }
                _keycapUI.SetCanApply(_hasActiveKeycapMaterialPreview, _hasPendingKeycapMaterial);
                if (targetIsValid && !_hasActiveKeycapMaterialPreview)
                    _keycapUI.ShowPlacementFailure(customization.KeycapPlacementError);
                return;
            }

            if (!allKeys && keycapIndex < 0)
                return;

            _selectedMaterial = allKeys
                ? customization.KeycapMaterial
                : customization.GetKeycapMaterial(keycapIndex);
            _selectedKeycapDefinition = customization.GetKeycapDefinition(allKeys ? -1 : keycapIndex);
            RefreshSelectionIndicators();
        }

        /// <summary>
        /// 현재 키캡 재질 미리보기를 취소하고 적용 상태의 재질 표시로 복귀한다.
        /// 버튼 입력 없이 키보드 상태와 선택 표시를 되돌린다.
        /// </summary>
        private void HandleKeycapUndoRequested()
        {
            CancelKeycapMaterialPreview();
            RefreshKeycapMaterialSelection();
        }

        /// <summary>
        /// 유효한 미리보기를 확정하고 선택 재질과 범위를 관찰 이벤트로 전달한다.
        /// 현재 적용 범위 및 키 번호를 사용하며 새 적용 상태를 기준으로 삼는다.
        /// </summary>
        private void HandleKeycapApplyRequested()
        {
            if (!_keycapUI.TryGetTarget(out bool allKeys, out int keycapIndex))
                return;

            CommitPendingKeycapMaterial(allKeys, keycapIndex);
        }

        /// <summary>
        /// 단일 키 입력 직후 선택된 재질을 해당 키에 확정하고 다음 키 입력을 준비한다.
        /// keycapIndex를 사용하며 설치 연출 뒤 선택 재질을 유지한다.
        /// </summary>
        private void HandleKeycapKeyInputCaptured(int keycapIndex)
        {
            CommitPendingKeycapMaterial(false, keycapIndex, true);
        }

        /// <summary>
        /// 선택 재질의 유효한 미리보기를 전체 또는 단일 키에 적용한다.
        /// allKeys, keycapIndex와 retainMaterialSelection을 사용해 적용 및 다음 키 입력 상태를 갱신한다.
        /// </summary>
        private void CommitPendingKeycapMaterial(bool allKeys, int keycapIndex, bool retainMaterialSelection = false)
        {
            if (!_hasPendingKeycapMaterial || !_hasActiveKeycapMaterialPreview)
                return;

            KeyboardCustomization customization = _photoStorage.Customization;
            var before = new List<string[]>(customization.GetKeycapLogValues());
            bool success = customization.CommitKeycapMaterialPreview(_selectedMaterial, allKeys, keycapIndex, _selectedKeycapDefinition);
            bool changed = false;
            string selectedId = Game.Logging.CustomizationTelemetry.GetKeycapElementId(_selectedKeycapDefinition, _selectedMaterial);
            if (success)
            {
                var after = new List<string[]>(customization.GetKeycapLogValues());
                for (int index = 0; index < before.Count; index++)
                    if ((allKeys || index == keycapIndex) && (before[index][1] != after[index][1]
                        || before[index][4] != after[index][4] || before[index][5] != after[index][5])) changed = true;

                string previousSelection = allKeys
                    ? before.Count > 0 ? before[0][1] : ""
                    : keycapIndex >= 0 && keycapIndex < before.Count ? before[keycapIndex][1] : "";
                if (previousSelection != selectedId)
                    Game.Logging.CustomizationTelemetry.Emit(customization, "OptionSelected", "Keycap", selectedId,
                        previousSelection, changed: true, scope: allKeys ? "AllKeycaps" : "SingleKeycap",
                        key: allKeys ? "" : keycapIndex.ToString());

                for (int index = 0; index < before.Count; index++)
                {
                    bool slotChanged = before[index][1] != after[index][1]
                        || before[index][4] != after[index][4] || before[index][5] != after[index][5];
                    if (allKeys && slotChanged)
                    {
                        Game.Logging.CustomizationTelemetry.Emit(customization, "PartInstallAttempted", "Keycap", after[index][1],
                            before[index][1], true, true, "AllKeycaps", index.ToString(), targetId: customization.GetKeycapSlotId(index),
                            actionSource: "BulkPlacement", keycapKind: after[index][3], materialId: after[index][4]);
                    }
                }
            }
            string previous = !allKeys && keycapIndex >= 0 && keycapIndex < before.Count ? before[keycapIndex][1] : "";
            if (!allKeys && keycapIndex >= 0 && keycapIndex < before.Count)
                Game.Logging.CustomizationTelemetry.Emit(customization, "PartInstallAttempted", "Keycap", selectedId,
                    previous, success, changed, "SingleKeycap", keycapIndex.ToString(), targetId: customization.GetKeycapSlotId(keycapIndex),
                    actionSource: "MaterialPicker", outcomeReason: success ? "" : "PlacementRejected",
                    keycapKind: Game.Logging.CustomizationTelemetry.GetKeycapKind(_selectedKeycapDefinition, _selectedMaterial),
                    materialId: _selectedKeycapDefinition != null ? _selectedKeycapDefinition.MaterialId : _selectedMaterial != null ? _selectedMaterial.name : "");
            if (!success)
            {
                customization.CancelKeycapMaterialPreview();
                _hasActiveKeycapMaterialPreview = false;
                _keycapUI.SetCanApply(false, _hasPendingKeycapMaterial);
                _keycapUI.ShowPlacementFailure(customization.KeycapPlacementError);
                return;
            }

            _hasPendingKeycapMaterial = retainMaterialSelection;
            _hasActiveKeycapMaterialPreview = false;
            _keycapUI.SetCanApply(false, _hasPendingKeycapMaterial);
            if (retainMaterialSelection) RefreshSelectionIndicators();
            else RefreshKeycapMaterialSelection();
        }

        /// <summary>
        /// 미적용 키캡 재질 변경을 취소하고 패널 버튼 상태를 초기화한다.
        /// 현재 사진 저장 대상의 스냅샷을 사용하며 선택 표시를 유지한다.
        /// </summary>
        private void CancelKeycapMaterialPreview()
        {
            if (_photoStorage != null && _photoStorage.Customization != null)
                _photoStorage.Customization.CancelKeycapMaterialPreview();
            _hasPendingKeycapMaterial = false;
            _hasActiveKeycapMaterialPreview = false;
            if (_keycapUI != null)
                _keycapUI.SetCanApply(false, false);
        }

        /// <summary>
        /// 현재 키캡 범위에 적용된 재질을 찾아 선택 카드 강조를 갱신한다.
        /// 전체 범위는 공통 재질을, 단일 범위는 현재 키의 개별 재질을 읽는다.
        /// </summary>
        private void RefreshKeycapMaterialSelection()
        {
            if (_photoStorage == null || _photoStorage.Customization == null)
                return;

            if (_pickerMode == PickerMode.Keycap && _keycapUI.TryGetTarget(out bool allKeys, out int keycapIndex))
            {
                _selectedMaterial = allKeys
                    ? _photoStorage.Customization.KeycapMaterial
                    : _photoStorage.Customization.GetKeycapMaterial(keycapIndex);
                _selectedKeycapDefinition = _photoStorage.Customization.GetKeycapDefinition(allKeys ? -1 : keycapIndex);
            }
            else
            {
                _selectedMaterial = _photoStorage.Customization.KeycapMaterial;
                _selectedKeycapDefinition = _photoStorage.Customization.GetKeycapDefinition(-1);
            }

            _keycapUI.SetSingleKeyOnly(_selectedKeycapDefinition != null && _selectedKeycapDefinition.IsArtisan);
            RefreshSelectionIndicators();
        }

        /// <summary>
        /// 빠진 키캡이 있으면 축이 설치된 키 전체에 미리보기를 시작한다.
        /// 키캡이 모두 빠진 경우 첫 재질을 선택하고 재질 및 축 상태로 적용 가능 여부를 갱신한다.
        /// </summary>
        private void PreviewMissingKeycapsWithCurrentMaterial()
        {
            KeyboardCustomization customization = _photoStorage.Customization;
            if (!customization.HasMissingKeycaps() || !customization.HasAnySwitchForKeycaps())
                return;

            if (!customization.HasAnyInstalledKeycaps() && _previewMaterials.Count > 0)
            {
                _selectedMaterial = _previewMaterials[0];
                _selectedKeycapDefinition = _previewDefinitions[0];
                RefreshSelectionIndicators();
            }

            if (_selectedMaterial == null)
                return;

            _hasActiveKeycapMaterialPreview = customization.PreviewKeycapMaterial(
                _selectedMaterial,
                true,
                -1,
                _selectedKeycapDefinition);
            _hasPendingKeycapMaterial = _hasActiveKeycapMaterialPreview;
            _keycapUI.SetCanApply(_hasActiveKeycapMaterialPreview, _hasPendingKeycapMaterial);
        }

        /// <summary>
        /// 현재 케이스 재질과 일치하는 카드의 선택 표시를 갱신한다.
        /// 공통 재질 목록과 현재 재질을 비교하며 각 카드의 배경과 체크 표시를 변경한다.
        /// </summary>
        public void RefreshCaseMaterialSelection()
        {
            for (int index = 0; index < _caseSelectionIndicators.Count; index++)
            {
                SetPickerItemSelection(
                    _casePreviewButtons[index],
                    _caseCardBackgrounds[index],
                    _caseSelectionIndicators[index],
                    _keycapMaterials[_caseMaterialIndices[index]] == (_pickerMode == PickerMode.Upper && ActiveAssembly != null
                        ? ActiveAssembly.UpperMaterial : _caseCustomization.CaseMaterial));
            }
        }

        /// <summary>
        /// 현재 보강판 재질과 효과에 맞춰 두 선택 목록의 카드 상태를 갱신한다.
        /// 편집 대상의 PlateMaterial과 CurrentPlateEffect를 사용하며 각 선택 표시를 변경한다.
        /// </summary>
        public void RefreshPlateSelection()
        {
            if (_plateCustomization == null)
                return;

            for (int index = 0; index < _plateMaterialSelectionIndicators.Count; index++)
            {
                SetPickerItemSelection(
                    _plateMaterialButtons[index],
                    _plateMaterialCardBackgrounds[index],
                    _plateMaterialSelectionIndicators[index],
                    !UsesScreenTrays && _plateMaterialButtonMaterials[index] == _plateCustomization.PlateMaterial);
            }

            for (int index = 0; index < _plateEffectSelectionIndicators.Count; index++)
            {
                SetPickerItemSelection(
                    _plateEffectButtons[index],
                    _plateEffectCardBackgrounds[index],
                    _plateEffectSelectionIndicators[index],
                    _plateEffects[index] == _plateCustomization.CurrentPlateEffect);
            }
        }

        public Color CatalogCardColor => _pickerCardColor;

        public string ControlHintTitle => _pickerMode switch
        {
            PickerMode.Keycap => "키캡 카탈로그",
            PickerMode.SwitchSound => "스위치 카탈로그",
            PickerMode.CaseMaterial => _assemblyCatalog ? "하단부 재질" : "케이스 재질",
            PickerMode.Plate => "보강판 편집",
            PickerMode.Pcb => "PCB 조립",
            PickerMode.Upper => "상단부 재질",
            PickerMode.Photo => "이미지 편집",
            PickerMode.Led => "LED 편집",
            _ => "키보드 편집"
        };

        /// <summary>현재 사진 편집 입력이 점유 중이면 state에 크롭 조작을 추가하고 true를 반환한다. 다른 편집 상태는 변경하지 않는다.</summary>
        public bool CollectControlHints(WorkshopControlHintState state)
        {
            if (!isActiveAndEnabled) return false;
            _keycapUI.CollectControlHints(state);
            return _photoUI.CollectControlHints(state);
        }

        /// <summary>
        /// screenPosition이 편집 패널이나 열린 선택 목록 위에 있는지 확인한다.
        /// 입력 위치와 UI 사각형을 사용하며 해당 지점이 UI 영역이면 true를 반환한다.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            if (ContainsPointer(_keyboardEditPanel, screenPosition))
            {
                return true;
            }

            return ((_keycapTrayContent == null || _keycapTrayTransition.IsTrayVisible) && ContainsPointer(_viewport, screenPosition))
                || ((_keycapTrayContent == null || _keycapTrayTransition.IsTrayVisible) && ContainsPointer(_switchSoundViewport, screenPosition))
                || ContainsPointer(_caseMaterialViewport, screenPosition)
                || ContainsPointer(_plateViewport, screenPosition)
                || _photoUI.IsPointerOverUI(screenPosition)
                || _ledUI.IsPointerOverUI(screenPosition);
        }

        /// <summary>
        /// 클릭한 편집 항목에 따라 키캡, 축 사운드, 케이스, 보강판 목록 또는 사진 및 LED 편집 UI를 연다.
        /// button을 사용하며 같은 항목을 다시 누르면 해당 목록이나 패널을 닫는다.
        /// </summary>
        private void HandleCategoryClicked(KeyboardEditButton button)
        {
            PickerMode mode = PickerMode.None;
            if (button == _pcbButton)
                mode = PickerMode.Pcb;
            else if (button == _upperButton)
                mode = PickerMode.Upper;
            else if (button == _keycapButton)
                mode = PickerMode.Keycap;
            else if (button == _switchAxisButton)
                mode = PickerMode.SwitchSound;
            else if (button == _caseButton)
                mode = PickerMode.CaseMaterial;
            else if (button == _plateButton)
                mode = PickerMode.Plate;
            else if (button == _customButton)
                mode = PickerMode.Photo;
            else if (button == _ledButton)
                mode = PickerMode.Led;
            if (_keycapTrayContent != null && (mode == PickerMode.Photo || mode == PickerMode.Led))
            {
                _photoTraySelected = mode == PickerMode.Photo;
                _keycapTrayTransition.RequestDrawer(WorkshopDrawerCategory.LedCustomImage);
                return;
            }
            if (_keycapTrayContent != null && (mode == PickerMode.Plate || mode == PickerMode.Upper
                || mode == PickerMode.Pcb || mode == PickerMode.CaseMaterial))
            {
                _keycapTrayTransition.RequestDrawer(mode == PickerMode.Plate ? WorkshopDrawerCategory.Plate
                    : mode == PickerMode.Upper ? WorkshopDrawerCategory.UpperHousing
                    : mode == PickerMode.Pcb ? WorkshopDrawerCategory.Pcb : WorkshopDrawerCategory.LowerHousing);
                return;
            }
            Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "PartAccess", GetLogPart(mode),
                mode == _pickerMode ? "Close" : "Open");
            if (_catalogLayout != null)
            {
                int index = mode == PickerMode.Keycap ? 0 : mode == PickerMode.SwitchSound ? 1
                    : mode == PickerMode.Led ? 3 : mode == PickerMode.Plate ? 4 : mode == PickerMode.Photo ? 7 : 8;
                if (_assemblyCatalog)
                    index = mode switch
                    {
                        PickerMode.CaseMaterial => 0,
                        PickerMode.Pcb => 1,
                        PickerMode.Plate => 2,
                        PickerMode.SwitchSound => 3,
                        PickerMode.Upper => 4,
                        PickerMode.Keycap => 5,
                        PickerMode.Led => 6,
                        PickerMode.Photo => 7,
                        _ => -1
                    };
                _catalogLayout.RequestDrawer(index);
                return;
            }
            SetPickerMode(mode == _pickerMode ? PickerMode.None : mode);
        }

        /// <summary>
        /// index에 해당하는 기존 이미지 또는 케이스 버튼을 반환해 서랍 도구줄로 옮길 수 있게 한다.
        /// 원본 이벤트 연결과 선택 표시 컴포넌트는 유지한다.
        /// </summary>
        public KeyboardEditButton GetDrawerUtilityButton(int index)
        {
            return index == 7 ? _customButton : _caseButton;
        }

        /// <summary>
        /// parent 아래로 기존 일반 및 공격 모드 버튼을 옮겨 카테고리 목록 숨김과 분리한다.
        /// 두 버튼의 이벤트와 기능은 유지하며 반환한 버튼을 새 도구줄에서 배치한다.
        /// </summary>
        public KeyboardEditButton[] MoveDrawerModeButtons(Transform parent)
        {
            _generalModeButton.transform.SetParent(parent, false);
            _attackModeButton.transform.SetParent(parent, false);
            return new[] { _generalModeButton, _attackModeButton };
        }

        /// <summary>
        /// 서랍 index를 기존 편집 목록 모드로 연결하고 표시 전용 항목은 목록 없이 유지한다.
        /// 기존 재고와 선택 데이터는 변경하지 않으며 음수는 모든 목록을 닫는다.
        /// </summary>
        public void SetDrawerCategory(int index)
        {
            PickerMode mode = index switch
            {
                0 => PickerMode.Keycap,
                1 => PickerMode.SwitchSound,
                3 => PickerMode.Led,
                4 => PickerMode.Plate,
                7 => PickerMode.Photo,
                8 => PickerMode.CaseMaterial,
                _ => PickerMode.None
            };
            if (_assemblyCatalog)
                mode = index switch
                {
                    0 => PickerMode.CaseMaterial,
                    1 => PickerMode.Pcb,
                    2 => PickerMode.Plate,
                    3 => PickerMode.SwitchSound,
                    4 => PickerMode.Upper,
                    5 => PickerMode.Keycap,
                    6 => PickerMode.Led,
                    7 => PickerMode.Photo,
                    _ => PickerMode.None
                };
            SetPickerMode(mode);
        }

        /// <summary>
        /// 서랍의 펼침 완료 시 현재 부품 목록을 튜토리얼 관찰자에게 알린다.
        /// 선택된 편집 모드를 사용하며 목록이나 부품 상태는 변경하지 않는다.
        /// </summary>
        public void NotifyDrawerOpened()
        {
            Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "PartPanelOpened", GetLogPart(_pickerMode));
        }

        /// <summary>
        /// 일반 모드 선택을 PlayerInteractionController에 전달한다.
        /// button의 클릭 콜백에서 false를 전달해 공격 요청을 차단하도록 알린다.
        /// </summary>
        private void HandleGeneralModeClicked(KeyboardEditButton button)
        {
            AttackModeSelected?.Invoke(false);
        }

        /// <summary>
        /// 공격 모드 선택을 PlayerInteractionController에 전달한다.
        /// button의 클릭 콜백에서 true를 전달해 공격 요청을 허용하도록 알린다.
        /// </summary>
        private void HandleAttackModeClicked(KeyboardEditButton button)
        {
            AttackModeSelected?.Invoke(true);
        }

        /// <summary>
        /// 클릭한 미리보기 버튼에 해당하는 키캡 재질을 선택한다.
        /// button의 목록 인덱스를 사용하며 선택 이벤트를 전달하고 목록은 유지한다.
        /// </summary>
        private void HandlePreviewClicked(KeyboardEditButton button)
        {
            if (_keycapPlacement != null) return;
            int index = _previewButtons.IndexOf(button);
            if (index >= 0 && index < _previewDefinitions.Count)
                ObserveKeycap("ElementPicked", _previewDefinitions[index], _previewMaterials[index]);
            SelectMaterial(index);
        }

        /// <summary>
        /// 클릭한 축 사운드 버튼의 타입을 편집 대상에 전달한다.
        /// button의 목록 인덱스를 사용하며 실제 적용은 C# 이벤트 구독자에게 요청한다.
        /// </summary>
        private void HandleSwitchSoundClicked(KeyboardEditButton button)
        {
            if (_keycapPlacement != null) return;
            SwitchSoundSelected?.Invoke(_switchSoundTypes[_switchSoundButtons.IndexOf(button)]);
        }

        /// <summary>
        /// 클릭한 케이스 미리보기에 해당하는 공통 재질을 전달한다.
        /// button으로 재질을 찾아 C# 이벤트로 적용을 요청하며 목록은 유지한다.
        /// </summary>
        private void HandleCasePreviewClicked(KeyboardEditButton button)
        {
            if (_pickerMode == PickerMode.Upper)
            {
                if (ActiveAssembly != null && _pullerController.InputAllowed)
                    ActiveAssembly.SetUpperMaterial(_keycapMaterials[_caseMaterialIndices[_casePreviewButtons.IndexOf(button)]]);
                RefreshCaseMaterialSelection();
                return;
            }
            CaseMaterialSelected?.Invoke(_keycapMaterials[_caseMaterialIndices[_casePreviewButtons.IndexOf(button)]]);
        }

        /// <summary>
        /// 색상 탭을 선택하고 재질 목록을 표시한다.
        /// button은 탭 클릭 이벤트에서 전달되며 효과 설정은 변경하지 않는다.
        /// </summary>
        private void HandlePlateMaterialTabClicked(KeyboardEditButton button)
        {
            SetPlatePickerSection(PlatePickerSection.Material);
        }

        /// <summary>
        /// 효과 탭을 선택하고 효과 목록을 표시한다.
        /// button은 탭 클릭 이벤트에서 전달되며 재질 설정은 변경하지 않는다.
        /// </summary>
        private void HandlePlateEffectTabClicked(KeyboardEditButton button)
        {
            SetPlatePickerSection(PlatePickerSection.Effect);
        }

        /// <summary>
        /// 클릭한 보강판 색상 카드의 재질을 편집 대상으로 전달한다.
        /// button의 카드 목록 인덱스를 사용하며 재질 선택 이벤트를 발생시킨다.
        /// </summary>
        private void HandlePlateMaterialClicked(KeyboardEditButton button)
        {
            int index = _plateMaterialButtons.IndexOf(button);
            if (index < 0 || index >= _plateMaterialButtonMaterials.Count)
                return;

            PlateMaterialSelected?.Invoke(_plateMaterialButtonMaterials[index]);
        }

        /// <summary>
        /// 클릭한 보강판 효과 카드의 효과를 편집 대상으로 전달한다.
        /// button의 카드 목록 인덱스를 사용하며 효과 선택 이벤트를 발생시킨다.
        /// </summary>
        private void HandlePlateEffectClicked(KeyboardEditButton button)
        {
            int index = _plateEffectButtons.IndexOf(button);
            if (index < 0 || index >= _plateEffects.Count)
                return;

            PlateEffectSelected?.Invoke(_plateEffects[index]);
        }

        /// <summary>
        /// 편집 패널에 재질 미리보기와 이름이 있는 가로 선택 목록을 만든다.
        /// 프리팹 메시, 재질 배열과 등록된 키캡 SO를 사용하며 모델을 포함한 선택 목록과 미리보기 텍스처를 구성한다.
        /// </summary>
        private void CreateMaterialPicker()
        {
            if (_previewKeycapPrefab == null || _keycapMaterials.Length == 0)
            {
                return;
            }

            MeshRenderer shellRenderer = FindKeycapShellRenderer(_previewKeycapPrefab);
            MeshFilter shellMeshFilter = shellRenderer != null ? shellRenderer.GetComponent<MeshFilter>() : null;
            if (shellMeshFilter == null || shellMeshFilter.sharedMesh == null)
            {
                Debug.LogWarning("키캡 미리보기 프리팹에서 PBT_SculptedShell 메시를 찾지 못했습니다.", this);
                return;
            }

            var materials = new List<Material>();
            var definitions = new List<KeycapDefinition>();
            foreach (Material material in _keycapMaterials)
            {
                KeycapDefinition matched = null;
                foreach (KeycapDefinition definition in _keycapDefinitions)
                {
                    if (definition != null && definition.ShellMesh == null && definition.Material == material)
                    {
                        if (matched != null && matched != definition)
                            throw new InvalidOperationException("Material-only keycap choices must have a unique definition.");
                        matched = definition;
                    }
                }
                materials.Add(material);
                definitions.Add(matched);
            }
            foreach (KeycapDefinition definition in _keycapDefinitions)
            {
                if (definition == null || definition.ShellMesh == null || definitions.Contains(definition)) continue;
                materials.Add(definition.Material);
                definitions.Add(definition);
            }
            Texture2D[] previews = CreateMaterialPreviewTextures(shellMeshFilter.sharedMesh,
                materials, _previewTextures, "Keycap", definitions);
            CreateViewport(materials.Count);
            CreatePreviewItems(previews, materials, definitions);
            RefreshSelectionIndicators();
        }

        /// <summary>
        /// previewPrefab의 PBT_SculptedShell 렌더러를 찾아 반환한다.
        /// previewPrefab을 사용하며 해당 렌더러가 없으면 null을 반환한다.
        /// </summary>
        private MeshRenderer FindKeycapShellRenderer(GameObject previewPrefab)
        {
            MeshRenderer[] renderers = previewPrefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.name == "PBT_SculptedShell")
                {
                    return renderer;
                }
            }

            return null;
        }

        /// <summary>
        /// 미리보기 메시와 재질 목록을 오프스크린 렌더링해 텍스처 배열을 만든다.
        /// previewMesh, materials, previewName과 선택적 definitions의 모델을 사용하며 생성한 텍스처를 textures에 기록하고 반환한다.
        /// </summary>
        private Texture2D[] CreateMaterialPreviewTextures(
            Mesh previewMesh,
            IReadOnlyList<Material> materials,
            List<Texture2D> textures,
            string previewName,
            IReadOnlyList<KeycapDefinition> definitions = null)
        {
            Texture2D[] previews = new Texture2D[materials.Count];
            if (UsesScreenTrays && (previewName == "Plate" || previewName == "Case"))
            {
                for (int index = 0; index < materials.Count; index++)
                {
                    previews[index] = AssemblyCatalogPreview.Render(previewMesh, new[] { materials[index] }, widePreview: true);
                    textures.Add(previews[index]);
                }
                return previews;
            }
            GameObject previewStage = new GameObject(previewName + "MaterialPreviewStage");
            previewStage.hideFlags = HideFlags.HideAndDontSave;

            int previewLayerMask = 1 << PREVIEW_LAYER;
            GameObject modelObject = new GameObject("KeycapPreviewModel");
            modelObject.layer = PREVIEW_LAYER;
            modelObject.transform.SetParent(previewStage.transform, false);
            MeshFilter meshFilter = modelObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = previewMesh;
            MeshRenderer meshRenderer = modelObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            GameObject lightObject = new GameObject("KeycapPreviewLight");
            lightObject.layer = PREVIEW_LAYER;
            lightObject.transform.SetParent(previewStage.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light previewLight = lightObject.AddComponent<Light>();
            previewLight.type = LightType.Directional;
            previewLight.intensity = 1.35f;
            previewLight.shadows = LightShadows.None;
            previewLight.cullingMask = previewLayerMask;

            GameObject cameraObject = new GameObject("KeycapPreviewCamera");
            cameraObject.layer = PREVIEW_LAYER;
            cameraObject.transform.SetParent(previewStage.transform, false);
            Camera previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.cullingMask = previewLayerMask;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.orthographic = true;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 10f;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;

            Bounds bounds = meshRenderer.bounds;
            Vector3 viewDirection = new Vector3(0.65f, 0.82f, -1f).normalized;
            float radius = bounds.extents.magnitude;
            float cameraDistance = Mathf.Max(3f, radius * 3f);
            previewCamera.farClipPlane = Mathf.Max(10f, cameraDistance + radius * 2f);
            previewCamera.transform.position = bounds.center + viewDirection * cameraDistance;
            previewCamera.transform.LookAt(bounds.center, Vector3.up);
            previewCamera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.8f;

            RenderTexture renderTexture = new RenderTexture(
                PREVIEW_TEXTURE_SIZE,
                PREVIEW_TEXTURE_SIZE,
                16,
                RenderTextureFormat.ARGB32);
            renderTexture.Create();
            previewCamera.targetTexture = renderTexture;
            RenderTexture previousActiveTexture = RenderTexture.active;
            var previewMaterials = new List<Material>();

            try
            {
                for (int index = 0; index < materials.Count; index++)
                {
                    Material material = materials[index];
                    if (material == null)
                    {
                        continue;
                    }

                    KeycapDefinition definition = definitions != null ? definitions[index] : null;
                    meshFilter.sharedMesh = definition != null && definition.ShellMesh != null ? definition.ShellMesh : previewMesh;
                    Material[] sourceMaterials = definition != null && definition.ShellMesh != null
                        ? definition.ModelMaterials : new[] { material };
                    var modelMaterials = new Material[sourceMaterials.Length];
                    for (int slot = 0; slot < sourceMaterials.Length; slot++)
                        modelMaterials[slot] = CreatePreviewMaterial(sourceMaterials[slot], previewMaterials);
                    meshRenderer.sharedMaterials = modelMaterials;
                    bounds = meshRenderer.bounds;
                    radius = bounds.extents.magnitude;
                    cameraDistance = Mathf.Max(3f, radius * 3f);
                    previewCamera.farClipPlane = Mathf.Max(10f, cameraDistance + radius * 2f);
                    previewCamera.transform.position = bounds.center + viewDirection * cameraDistance;
                    previewCamera.transform.LookAt(bounds.center, Vector3.up);
                    previewCamera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.8f;
                    KeyboardToonSurface.ApplyBounds(meshRenderer);
                    previewCamera.Render();
                    RenderTexture.active = renderTexture;
                    Texture2D previewTexture = new Texture2D(
                        PREVIEW_TEXTURE_SIZE,
                        PREVIEW_TEXTURE_SIZE,
                        TextureFormat.RGBA32,
                        false);
                    previewTexture.name = $"{previewName}Preview_{index}";
                    previewTexture.ReadPixels(
                        new Rect(0f, 0f, PREVIEW_TEXTURE_SIZE, PREVIEW_TEXTURE_SIZE),
                        0,
                        0);
                    previewTexture.Apply(false, true);
                    previews[index] = previewTexture;
                    textures.Add(previewTexture);
                }
            }
            finally
            {
                RenderTexture.active = previousActiveTexture;
                previewCamera.targetTexture = null;
                renderTexture.Release();
                Destroy(renderTexture);
                previewStage.SetActive(false);
                Destroy(previewStage);
                foreach (Material material in previewMaterials) Destroy(material);
            }

            return previews;
        }

        /// <summary>
        /// material이 투명 재질이면 미리보기용 복제본을 불투명 렌더링으로 전환해 형태를 표시한다.
        /// 원본 재질은 유지하고 복제본은 ownedMaterials에 등록해 렌더링 후 해제하며 표시용 재질을 반환한다.
        /// </summary>
        private Material CreatePreviewMaterial(Material material, List<Material> ownedMaterials)
        {
            return AssemblyCatalogPreview.CreatePreviewMaterial(material, ownedMaterials);
        }
        /// <summary>
        /// 화면 상단에 잘리는 투명 viewport와 그 안의 가로 콘텐츠 영역을 만든다.
        /// overlay 크기와 itemCount를 사용하며 이후 목록 스크롤이 적용될 RectTransform을 저장한다.
        /// </summary>
        private void CreateViewport(int itemCount)
        {
            _viewport = CreateScrollViewport("KeycapMaterial", itemCount, out _content);
            if (_keycapTrayContent != null) _viewport.SetParent(_keycapTrayContent, false);
        }

        /// <summary>
        /// 화면 상단에 항목 개수에 맞는 고정 높이 가로 선택 패널을 만든다.
        /// listName과 itemCount로 패널을 구성하고 viewport와 content를 반환한다.
        /// </summary>
        private RectTransform CreateScrollViewport(string listName, int itemCount, out RectTransform content)
        {
            RectTransform overlayRect = transform as RectTransform;
            float viewportWidth = _viewportWidth;
            if (overlayRect != null && overlayRect.rect.width > 0f)
            {
                viewportWidth = Mathf.Min(viewportWidth, Mathf.Max(240f, overlayRect.rect.width - 40f));
            }

            GameObject viewportObject = new GameObject(
                listName + "Viewport",
                typeof(RectTransform),
                typeof(RectMask2D),
                typeof(Image),
                typeof(ScrollRect));
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(transform, false);
            viewport.anchorMin = new Vector2(0.5f, 1f);
            viewport.anchorMax = new Vector2(0.5f, 1f);
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.anchoredPosition = new Vector2(0f, -24f);
            viewport.sizeDelta = new Vector2(viewportWidth, PICKER_PANEL_HEIGHT);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.sprite = _pickerSprite;
            viewportImage.type = Image.Type.Sliced;
            viewportImage.color = _pickerPanelBorderColor;
            viewportImage.raycastTarget = true;

            GameObject panelBackgroundObject = new GameObject(
                listName + "PanelBackground",
                typeof(RectTransform),
                typeof(Image));
            RectTransform panelBackgroundRect = panelBackgroundObject.GetComponent<RectTransform>();
            panelBackgroundRect.SetParent(viewport, false);
            panelBackgroundRect.anchorMin = Vector2.zero;
            panelBackgroundRect.anchorMax = Vector2.one;
            panelBackgroundRect.offsetMin = Vector2.one;
            panelBackgroundRect.offsetMax = -Vector2.one;
            Image panelBackground = panelBackgroundObject.GetComponent<Image>();
            panelBackground.sprite = _pickerSprite;
            panelBackground.type = Image.Type.Sliced;
            panelBackground.color = _pickerPanelColor;
            panelBackground.raycastTarget = false;

            GameObject contentObject = new GameObject(listName + "Content", typeof(RectTransform));
            content = contentObject.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 0.5f);
            content.anchorMax = new Vector2(0f, 0.5f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            float itemContentWidth = itemCount > 0
                ? itemCount * (_itemWidth + _itemSpacing) - _itemSpacing
                : 0f;
            float contentWidth = Mathf.Max(
                viewportWidth,
                itemContentWidth + PICKER_LEFT_ITEM_PADDING + PICKER_PANEL_PADDING);
            content.sizeDelta = new Vector2(contentWidth, PICKER_ITEM_HEIGHT);

            ScrollRect scrollRect = viewportObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = _wheelScrollSpeed * WHEEL_SCROLL_INPUT_SCALE;

            Scrollbar scrollbar = CreateHorizontalScrollbar(viewport, listName);
            scrollRect.horizontalScrollbar = scrollbar;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            viewport.gameObject.SetActive(false);
            return viewport;
        }

        /// <summary>
        /// listName에 해당하는 가로 목록에 얇은 스크롤 막대를 만든다.
        /// viewport를 부모로 사용하며 생성한 Scrollbar를 반환한다.
        /// </summary>
        private Scrollbar CreateHorizontalScrollbar(RectTransform viewport, string listName)
        {
            GameObject scrollbarObject = new GameObject(
                listName + "Scrollbar",
                typeof(RectTransform),
                typeof(Image),
                typeof(Scrollbar));
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.SetParent(viewport, false);
            scrollbarRect.anchorMin = new Vector2(0f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 0f);
            scrollbarRect.pivot = new Vector2(0.5f, 0f);
            scrollbarRect.anchoredPosition = new Vector2(0f, 7f);
            scrollbarRect.sizeDelta = new Vector2(-PICKER_PANEL_PADDING * 2f, PICKER_SCROLLBAR_HEIGHT);

            Image trackImage = scrollbarObject.GetComponent<Image>();
            trackImage.sprite = _pickerSprite;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color32(28, 40, 53, 220);
            trackImage.raycastTarget = true;

            GameObject handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(Image));
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.SetParent(scrollbarRect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(1f, 0f);
            handleRect.offsetMax = new Vector2(-1f, 0f);

            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.sprite = _pickerSprite;
            handleImage.type = Image.Type.Sliced;
            handleImage.color = _pickerScrollbarColor;
            handleImage.raycastTarget = true;

            Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            return scrollbar;
        }

        /// <summary>
        /// 지원하는 축 타입마다 아이콘과 이름이 있는 선택 버튼을 만든다.
        /// 축 타입 배열과 저장된 글꼴을 사용하며 목록과 시각 요소를 구성한다.
        /// </summary>
        private void CreateSwitchSoundPicker()
        {
            _switchSoundViewport = CreateScrollViewport("SwitchSound", _switchSoundTypes.Length, out RectTransform content);
            for (int index = 0; index < _switchSoundTypes.Length; index++)
            {
                SwitchType type = _switchSoundTypes[index];
                KeyboardEditButton button = CreatePickerItemButton(
                    content,
                    "SwitchSound_" + type,
                    index,
                    KeyboardPartDisplayNames.SwitchName(type),
                    out Image cardBackground,
                    out Text selectionIndicator);
                CreateSwitchPreview(button.transform, type);
                selectionIndicator.transform.SetAsLastSibling();
                _switchSoundButtons.Add(button);
                _switchSoundCardBackgrounds.Add(cardBackground);
                _switchSoundSelectionIndicators.Add(selectionIndicator);
            }

            RefreshSwitchSoundSelection();
        }

        /// <summary>
        /// 적용된 축 사운드와 일치하는 버튼의 배경과 체크를 갱신한다.
        /// 저장된 선택 타입과 버튼 목록을 사용하며 각 버튼의 선택 상태를 변경한다.
        /// </summary>
        private void RefreshSwitchSoundSelection()
        {
            for (int index = 0; index < _switchSoundButtons.Count; index++)
            {
                // 스위치 카드 선택 강조는 사용하지 않는다.
                // bool isSelected = _selectedSwitchSound == _switchSoundTypes[index];
                bool isSelected = false;
                SetPickerItemSelection(
                    _switchSoundButtons[index],
                    _switchSoundCardBackgrounds[index],
                    _switchSoundSelectionIndicators[index],
                    isSelected);
            }
        }

        /// <summary>
        /// content의 index 위치에 이름과 선택 표시가 있는 아이템 버튼을 만든다.
        /// labelText를 표시하고 버튼, 배경 Image, 선택 Text를 반환 또는 저장한다.
        /// </summary>
        private KeyboardEditButton CreatePickerItemButton(
            RectTransform content,
            string itemName,
            int index,
            string labelText,
            out Image cardBackground,
            out Text selectionIndicator)
        {
            GameObject itemObject = new GameObject(
                itemName,
                typeof(RectTransform),
                typeof(Image),
                typeof(KeyboardEditButton));
            RectTransform itemRect = itemObject.GetComponent<RectTransform>();
            itemRect.SetParent(content, false);
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(0f, 0.5f);
            itemRect.pivot = new Vector2(0.5f, 0.5f);
            itemRect.sizeDelta = new Vector2(_itemWidth, PICKER_ITEM_HEIGHT);
            itemRect.anchoredPosition = new Vector2(
                PICKER_LEFT_ITEM_PADDING + index * (_itemWidth + _itemSpacing) + _itemWidth * 0.5f,
                0f);

            Image frameImage = itemObject.GetComponent<Image>();
            frameImage.sprite = _pickerSprite;
            frameImage.type = Image.Type.Sliced;
            frameImage.color = Color.white;
            frameImage.raycastTarget = true;

            GameObject backgroundObject = new GameObject(
                "CardBackground",
                typeof(RectTransform),
                typeof(Image));
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.SetParent(itemRect, false);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = new Vector2(2f, 2f);
            backgroundRect.offsetMax = new Vector2(-2f, -2f);
            cardBackground = backgroundObject.GetComponent<Image>();
            cardBackground.sprite = _pickerSprite;
            cardBackground.type = Image.Type.Sliced;
            cardBackground.color = _pickerCardColor;
            cardBackground.raycastTarget = false;

            KeyboardEditButton button = itemObject.GetComponent<KeyboardEditButton>();
            button.targetGraphic = frameImage;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(itemRect, false);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 7f);
            labelRect.sizeDelta = new Vector2(-12f, 22f);
            Text label = labelObject.GetComponent<Text>();
            label.font = _pickerFont;
            label.text = labelText;
            label.fontSize = 12;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            label.raycastTarget = false;

            GameObject indicatorObject = new GameObject(
                "SelectedIndicator",
                typeof(RectTransform),
                typeof(Text));
            RectTransform indicatorRect = indicatorObject.GetComponent<RectTransform>();
            indicatorRect.SetParent(itemRect, false);
            indicatorRect.anchorMin = Vector2.one;
            indicatorRect.anchorMax = Vector2.one;
            indicatorRect.pivot = Vector2.one;
            indicatorRect.anchoredPosition = new Vector2(-7f, -6f);
            indicatorRect.sizeDelta = new Vector2(18f, 18f);
            selectionIndicator = indicatorObject.GetComponent<Text>();
            selectionIndicator.font = _pickerFont;
            selectionIndicator.text = "✓";
            selectionIndicator.fontSize = 13;
            selectionIndicator.fontStyle = FontStyle.Bold;
            selectionIndicator.alignment = TextAnchor.MiddleCenter;
            selectionIndicator.color = new Color32(128, 234, 255, 255);
            selectionIndicator.raycastTarget = false;

            SetPickerItemSelection(button, cardBackground, selectionIndicator, false);
            return button;
        }

        /// <summary>
        /// parent 아래 크기와 색상을 지정한 둥근 아이콘 Image를 만든다.
        /// name, size, position, color를 사용하며 아이콘 배경 Image를 반환한다.
        /// </summary>
        private Image CreatePickerImage(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 position,
            Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.SetParent(parent, false);
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = position;
            imageRect.sizeDelta = size;

            Image image = imageObject.GetComponent<Image>();
            image.sprite = _pickerSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 재질 렌더 텍스처를 아이템 버튼의 미리보기 이미지로 배치한다.
        /// parent, preview, size, verticalOffset을 사용하며 클릭 입력은 부모 버튼에 맡긴다.
        /// </summary>
        private RawImage CreateMaterialPreviewImage(
            Transform parent,
            string name,
            Texture2D preview,
            Vector2 size,
            float verticalOffset)
        {
            GameObject imageObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.SetParent(parent, false);
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = new Vector2(0f, verticalOffset);
            imageRect.sizeDelta = size;

            RawImage previewImage = imageObject.GetComponent<RawImage>();
            previewImage.texture = preview;
            previewImage.color = Color.white;
            previewImage.raycastTarget = false;
            return previewImage;
        }

        /// <summary>
        /// parent 카드에 type의 실제 출고 스위치를 렌더링한 이미지를 배치한다.
        /// 기존 키캡과 같은 이미지 요소를 사용하며 원본 부품과 재고는 변경하지 않는다.
        /// </summary>
        private void CreateSwitchPreview(Transform parent, SwitchType type)
        {
            Texture2D texture = GetComponent<WorkshopKeycapPlacement>().GetSwitchPreview(type);
            CreateMaterialPreviewImage(parent, "SwitchPreview", texture, new Vector2(96f, 88f), 14f);
        }
        /// <summary>
        /// button의 테두리, 배경과 체크 표시를 선택 상태에 맞춰 갱신한다.
        /// isSelected를 사용하며 세 시각 요소의 색상과 활성 상태를 변경한다.
        /// </summary>
        private void SetPickerItemSelection(
            KeyboardEditButton button,
            Image cardBackground,
            Text selectionIndicator,
            bool isSelected)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = isSelected ? _pickerSelectedColor : _pickerBorderColor;
            colors.highlightedColor = isSelected ? _pickerFocusColor : _pickerHoverColor;
            colors.pressedColor = isSelected ? new Color32(128, 234, 255, 255) : new Color32(64, 90, 112, 255);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            cardBackground.color = isSelected ? _pickerSelectedCardColor : _pickerCardColor;
            selectionIndicator.enabled = isSelected;
        }

        /// <summary>
        /// type에 등록된 축 색상을 선택 카드용으로 반환한다.
        /// 참조 HTML의 타입별 색상을 사용하며 알 수 없는 타입에는 기본 색상을 반환한다.
        /// </summary>
        private static Color GetSwitchSoundColor(SwitchType type)
        {
            switch (type)
            {
                case SwitchType.Blue: return new Color32(38, 124, 179, 255);
                case SwitchType.Red: return new Color32(217, 88, 99, 255);
                case SwitchType.Brown: return new Color32(156, 103, 72, 255);
                case SwitchType.Sans: return new Color32(175, 185, 195, 255);
                case SwitchType.Pop: return new Color32(235, 145, 197, 255);
                case SwitchType.Typewriter: return new Color32(146, 153, 161, 255);
                default: return new Color32(98, 129, 151, 255);
            }
        }

        /// <summary>
        /// 텍스처가 생성된 재질마다 이름과 미리보기가 있는 선택 버튼을 추가한다.
        /// previews, materials와 definitions의 대응 순서로 이름, 부품 SO와 선택 시각 요소를 저장한다.
        /// </summary>
        private void CreatePreviewItems(Texture2D[] previews, IReadOnlyList<Material> materials,
            IReadOnlyList<KeycapDefinition> definitions)
        {
            if (_content == null)
            {
                return;
            }

            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null || materials[index] == null)
                {
                    continue;
                }

                Material material = materials[index];
                KeycapDefinition definition = definitions[index];
                string label = KeyboardPartDisplayNames.KeycapName(definition, material);
                KeyboardEditButton previewButton = CreatePickerItemButton(
                    _content,
                    "KeycapMaterial_" + (definition != null ? definition.Id : material.name),
                    _previewButtons.Count,
                    label,
                    out Image cardBackground,
                    out Text selectionIndicator);
                CreateMaterialPreviewImage(
                    previewButton.transform,
                    "KeycapPreviewImage",
                    previews[index],
                    new Vector2(PICKER_PREVIEW_WIDTH, PICKER_PREVIEW_HEIGHT),
                    14f);
                selectionIndicator.transform.SetAsLastSibling();

                _previewButtons.Add(previewButton);
                _previewMaterials.Add(material);
                _previewDefinitions.Add(definition);
                _previewCardBackgrounds.Add(cardBackground);
                _selectionIndicators.Add(selectionIndicator);
            }
        }

        /// <summary>
        /// placement에 현재 키캡 카드와 재료 및 반환 영역을 연결한다.
        /// 기존 전체 적용 패널 대신 재고 드래그를 사용하며 키보드 상태는 변경하지 않는다. 반환값은 없다.
        /// </summary>
        public void ConnectKeycapPlacement(WorkshopKeycapPlacement placement)
        {
            _keycapPlacement = placement;
            for (int index = 0; index < _previewButtons.Count; index++)
            {
                _selectionIndicators[index].gameObject.SetActive(false);
                placement.BindCard(_previewButtons[index], _previewMaterials[index], _previewDefinitions[index], _viewport, _keycapTrayContent != null ? _keycapTrayTransition : null);
            }
            for (int index = 0; index < _switchSoundButtons.Count; index++)
                placement.BindSwitchCard(_switchSoundButtons[index], _switchSoundTypes[index], _switchSoundViewport, _keycapTrayContent != null ? _keycapTrayTransition : null);
            if (_keycapTrayContent != null) ConnectKeycapTray();
            _keycapUI.Close();
        }

        /// <summary>
        /// index 재질을 선택 표시와 현재 키캡 범위의 키보드 미리보기에 반영한다.
        /// 유효한 재질, 대응하는 키캡 SO와 패널 대상을 사용하며 모델을 포함한 적용 전 변경으로 보관한다.
        /// </summary>
        private void SelectMaterial(int index)
        {
            if (index < 0 || index >= _previewMaterials.Count)
            {
                return;
            }

            _selectedMaterial = _previewMaterials[index];
            _selectedKeycapDefinition = _previewDefinitions[index];
            RefreshSelectionIndicators();
            if (_pickerMode != PickerMode.Keycap)
                return;

            _hasPendingKeycapMaterial = true;
            _keycapUI.SetSingleKeyOnly(_selectedKeycapDefinition != null && _selectedKeycapDefinition.IsArtisan);
            bool targetIsValid = _keycapUI.TryGetTarget(out bool allKeys, out int keycapIndex);
            if (!targetIsValid)
            {
                _hasPendingKeycapMaterial = true;
                _hasActiveKeycapMaterialPreview = false;
                _photoStorage.Customization.CancelKeycapMaterialPreview();
            }
            else
            {
                _hasActiveKeycapMaterialPreview = _photoStorage.Customization.PreviewKeycapMaterial(
                    _selectedMaterial,
                    allKeys,
                    keycapIndex,
                    _selectedKeycapDefinition);
            }
            _keycapUI.SetCanApply(_hasActiveKeycapMaterialPreview, _hasPendingKeycapMaterial);
            if (targetIsValid && !_hasActiveKeycapMaterialPreview)
                _keycapUI.ShowPlacementFailure(_photoStorage.Customization.KeycapPlacementError);
        }

        /// <summary>
        /// 저장된 재질과 각 미리보기 재질을 비교해 카드 선택 상태를 갱신한다.
        /// 선택된 SO를 우선 비교하고 SO가 없는 기존 구성은 일반 재질로 비교해 버튼의 배경과 체크를 켠다.
        /// </summary>
        private void RefreshSelectionIndicators()
        {
            for (int index = 0; index < _selectionIndicators.Count; index++)
            {
                SetPickerItemSelection(
                    _previewButtons[index],
                    _previewCardBackgrounds[index],
                    _selectionIndicators[index],
                    // 키캡 카드 선택 강조는 사용하지 않는다.
                    // _selectedKeycapDefinition != null ? _previewDefinitions[index] == _selectedKeycapDefinition
                    //     : (_previewDefinitions[index] == null || _previewDefinitions[index].ShellMesh == null) && _previewMaterials[index] == _selectedMaterial
                    false);
            }
        }

        /// <summary>
        /// 현재 키보드의 케이스 파츠와 재질 목록으로 이름 있는 미리보기 목록을 만든다.
        /// 저장된 편집 대상을 사용하며 이전 목록과 텍스처를 정리하고 버튼 및 선택 요소를 저장한다.
        /// </summary>
        private void CreateCaseMaterialPicker()
        {
            if (_caseMaterialViewport != null)
            {
                Destroy(_caseMaterialViewport.gameObject);
                _caseMaterialViewport = null;
            }

            _casePreviewButtons.Clear();
            _caseCardBackgrounds.Clear();
            _caseMaterialIndices.Clear();
            _caseSelectionIndicators.Clear();
            DestroyPreviewTextures(_casePreviewTextures);
            _caseButton.interactable = _caseCustomization != null
                && _keycapMaterials.Length > 0
                && _caseCustomization.CaseRenderers.Count > 0;
            if (!_caseButton.interactable)
                return;

            Mesh caseMesh = CreateCasePreviewMesh();
            if (caseMesh == null)
            {
                _caseButton.interactable = false;
                return;
            }

            Texture2D[] previews;
            try
            {
                previews = CreateMaterialPreviewTextures(caseMesh, _keycapMaterials, _casePreviewTextures, "Case");
            }
            finally
            {
                Destroy(caseMesh);
            }

            _caseMaterialViewport = CreateScrollViewport("CaseMaterial", previews.Length, out RectTransform content);
            CreateCasePreviewItems(content, previews);
            RefreshCaseMaterialSelection();
        }

        /// <summary>
        /// 편집 키보드의 보강판 색상과 효과를 탭으로 나눈 상단 선택 패널에 구성한다.
        /// 키캡과 공유하는 일반 재질 및 효과 목록을 사용하며 재질 미리보기, 효과 카드, 선택 상태를 저장한다.
        /// </summary>
        private void CreatePlatePicker()
        {
            if (_plateTrayTabs != null)
            {
                Destroy(_plateTrayTabs.gameObject);
                _plateTrayTabs = null;
            }
            if (_plateViewport != null)
            {
                if (_keycapTrayTransition != null)
                    _keycapTrayTransition.DisconnectCatalogScroll(
                        _plateViewport.GetComponent<ScrollRect>(), WorkshopDrawerCategory.Plate);
                Destroy(_plateViewport.gameObject);
                _plateViewport = null;
            }

            _plateMaterialContent = null;
            _plateEffectContent = null;
            _plateMaterialTabButton = null;
            _plateEffectTabButton = null;
            _plateMaterialButtons.Clear();
            _plateMaterialCardBackgrounds.Clear();
            _plateMaterials.Clear();
            _plateMaterialButtonMaterials.Clear();
            _plateMaterialLabels.Clear();
            _plateMaterialSelectionIndicators.Clear();
            _plateEffectButtons.Clear();
            _plateEffectCardBackgrounds.Clear();
            _plateEffects.Clear();
            _plateEffectSelectionIndicators.Clear();
            DestroyPreviewTextures(_platePreviewTextures);

            KeyboardCustomization previewCustomization = _plateCustomization;
            if (previewCustomization == null && UsesScreenTrays && _pullerController.LowerHousingPreviewPrefab != null)
                previewCustomization = _pullerController.LowerHousingPreviewPrefab.GetComponent<KeyboardCustomization>();
            _plateButton.interactable = previewCustomization != null
                && previewCustomization.PlateRenderer != null;
            if (!_plateButton.interactable)
                return;

            MeshFilter plateMeshFilter = previewCustomization.PlateRenderer.GetComponent<MeshFilter>();
            if (plateMeshFilter == null || plateMeshFilter.sharedMesh == null)
            {
                _plateButton.interactable = false;
                return;
            }

            foreach (Material material in _keycapMaterials)
            {
                if (material == null)
                    continue;

                _plateMaterials.Add(material);
                _plateMaterialLabels.Add(KeyboardPartDisplayNames.MaterialName(material));
            }

            if (_plateMaterials.Count == 0)
            {
                _plateButton.interactable = false;
                return;
            }

            _plateEffects.AddRange(previewCustomization.AvailablePlateEffects);
            Texture2D[] previews = CreateMaterialPreviewTextures(
                plateMeshFilter.sharedMesh,
                _plateMaterials,
                _platePreviewTextures,
                "Plate");
            _plateViewport = CreateScrollViewport("Plate", _plateMaterials.Count, out _plateMaterialContent);
            Vector2 plateViewportSize = _plateViewport.sizeDelta;
            plateViewportSize.y += PLATE_PICKER_TAB_HEIGHT;
            _plateViewport.sizeDelta = plateViewportSize;
            ConfigurePlatePickerContent(_plateMaterialContent, _plateMaterials.Count);
            _plateEffectContent = CreatePlatePickerContent("PlateEffectContent", _plateEffects.Count);
            CreatePlateMaterialItems(_plateMaterialContent, previews);
            CreatePlateEffectItems(_plateEffectContent);
            _plateMaterialTabButton = CreatePlatePickerTab(
                _plateViewport,
                "PlateMaterialTab",
                "보강판 색상",
                new Vector2(12f, -PLATE_PICKER_TAB_TOP_PADDING));
            _plateEffectTabButton = CreatePlatePickerTab(
                _plateViewport,
                "PlateEffectTab",
                "타건 효과",
                new Vector2(166f, -PLATE_PICKER_TAB_TOP_PADDING));
            SetPlatePickerSection(_platePickerSection);
            RefreshPlateSelection();
            ConnectPlateTray();
        }

        /// <summary>
        /// viewport 안에 효과 카드가 표시될 가로 content 영역을 만든다.
        /// name, itemCount, 현재 보강판 viewport를 사용하며 스크롤 content를 반환한다.
        /// </summary>
        private RectTransform CreatePlatePickerContent(string name, int itemCount)
        {
            GameObject contentObject = new GameObject(name, typeof(RectTransform));
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(_plateViewport, false);
            ConfigurePlatePickerContent(content, itemCount);
            return content;
        }

        /// <summary>
        /// 보강판 카드 content를 탭 아래에 배치하고 스크롤 가능한 너비를 설정한다.
        /// content와 항목 수를 사용하며 content의 위치와 크기를 변경한다.
        /// </summary>
        private void ConfigurePlatePickerContent(RectTransform content, int itemCount)
        {
            float viewportWidth = _plateViewport.rect.width;
            float itemContentWidth = itemCount > 0
                ? itemCount * (_itemWidth + _itemSpacing) - _itemSpacing
                : 0f;
            float contentWidth = Mathf.Max(
                viewportWidth,
                itemContentWidth + PICKER_LEFT_ITEM_PADDING + PICKER_PANEL_PADDING);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = new Vector2(
                0f,
                -(PLATE_PICKER_TAB_TOP_PADDING + PLATE_PICKER_TAB_HEIGHT + PICKER_PANEL_PADDING));
            content.sizeDelta = new Vector2(contentWidth, PLATE_PICKER_ITEM_HEIGHT);
        }

        /// <summary>
        /// 보강판 선택 영역 상단에 라벨이 있는 탭 버튼을 만든다.
        /// parent, name, label, position을 사용하며 클릭 이벤트를 받을 버튼을 반환한다.
        /// </summary>
        private KeyboardEditButton CreatePlatePickerTab(
            RectTransform parent,
            string name,
            string label,
            Vector2 position)
        {
            GameObject tabObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image),
                typeof(KeyboardEditButton));
            RectTransform tabRect = tabObject.GetComponent<RectTransform>();
            tabRect.SetParent(parent, false);
            tabRect.anchorMin = Vector2.up;
            tabRect.anchorMax = Vector2.up;
            tabRect.pivot = Vector2.up;
            tabRect.anchoredPosition = position;
            tabRect.sizeDelta = new Vector2(146f, PLATE_PICKER_TAB_HEIGHT);

            Image tabImage = tabObject.GetComponent<Image>();
            tabImage.sprite = _pickerSprite;
            tabImage.type = Image.Type.Sliced;
            tabImage.color = _pickerCardColor;
            tabImage.raycastTarget = true;

            KeyboardEditButton button = tabObject.GetComponent<KeyboardEditButton>();
            button.targetGraphic = tabImage;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(tabRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 1f);
            labelRect.offsetMax = new Vector2(-4f, -1f);
            Text tabLabel = labelObject.GetComponent<Text>();
            tabLabel.font = _pickerFont;
            tabLabel.text = label;
            tabLabel.fontSize = 12;
            tabLabel.fontStyle = FontStyle.Bold;
            tabLabel.alignment = TextAnchor.MiddleCenter;
            tabLabel.color = Color.white;
            tabLabel.raycastTarget = false;
            return button;
        }

        /// <summary>
        /// 재질 카드마다 미리보기와 현지화한 색상 이름을 만든다.
        /// content와 previews를 사용하며 클릭할 Material 및 선택 시각 요소를 저장한다.
        /// </summary>
        private void CreatePlateMaterialItems(RectTransform content, Texture2D[] previews)
        {
            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null)
                    continue;

                Material material = _plateMaterials[index];
                KeyboardEditButton button = CreatePlatePickerItemButton(
                    content,
                    "PlateMaterial_" + material.name,
                    _plateMaterialButtons.Count,
                    _plateMaterialLabels[index],
                    out Image cardBackground,
                    out Text selectionIndicator);
                CreateMaterialPreviewImage(
                    button.transform,
                    "PlatePreviewImage",
                    previews[index],
                    new Vector2(PLATE_PICKER_PREVIEW_WIDTH, PLATE_PICKER_PREVIEW_HEIGHT),
                    18f);
                selectionIndicator.transform.SetAsLastSibling();

                _plateMaterialButtons.Add(button);
                _plateMaterialCardBackgrounds.Add(cardBackground);
                _plateMaterialButtonMaterials.Add(material);
                _plateMaterialSelectionIndicators.Add(selectionIndicator);
                if (_assemblyCatalog)
                    button.gameObject.AddComponent<WorkshopStructureMaterialDrag>().Initialize(this, material, AssemblyStructurePart.Plate);
            }
        }

        /// <summary>
        /// 지정한 타건 효과마다 이름과 간단한 설명이 있는 선택 카드를 만든다.
        /// _plateEffects와 content를 사용하며 버튼, 효과 값, 선택 시각 요소를 저장한다.
        /// </summary>
        private void CreatePlateEffectItems(RectTransform content)
        {
            for (int index = 0; index < _plateEffects.Count; index++)
            {
                PlateEffect effect = _plateEffects[index];
                KeyboardEditButton button = CreatePlatePickerItemButton(
                    content,
                    "PlateEffect_" + effect,
                    index,
                    GetPlateEffectLabel(effect),
                    out Image cardBackground,
                    out Text selectionIndicator);
                CreatePlateEffectDescription(button.transform, GetPlateEffectDescription(effect));
                selectionIndicator.transform.SetAsLastSibling();

                _plateEffectButtons.Add(button);
                _plateEffectCardBackgrounds.Add(cardBackground);
                _plateEffectSelectionIndicators.Add(selectionIndicator);
            }
        }

        /// <summary>
        /// 보강판 목록의 카드 버튼을 탭 아래 위치와 높이로 만든다.
        /// content, itemName, index, labelText를 사용하며 카드와 배경 및 선택 표시를 반환한다.
        /// </summary>
        private KeyboardEditButton CreatePlatePickerItemButton(
            RectTransform content,
            string itemName,
            int index,
            string labelText,
            out Image cardBackground,
            out Text selectionIndicator)
        {
            KeyboardEditButton button = CreatePickerItemButton(
                content,
                itemName,
                index,
                labelText,
                out cardBackground,
                out selectionIndicator);
            RectTransform itemRect = button.transform as RectTransform;
            itemRect.sizeDelta = new Vector2(_itemWidth, PLATE_PICKER_ITEM_HEIGHT);
            itemRect.anchoredPosition = new Vector2(
                PICKER_LEFT_ITEM_PADDING + index * (_itemWidth + _itemSpacing) + _itemWidth * 0.5f,
                0f);
            return button;
        }

        /// <summary>
        /// 효과 카드 중앙에 효과 동작을 설명하는 텍스트를 배치한다.
        /// parent와 description을 사용하며 카드 안에 설명 라벨을 생성한다.
        /// </summary>
        private void CreatePlateEffectDescription(Transform parent, string description)
        {
            GameObject descriptionObject = new GameObject(
                "EffectDescription",
                typeof(RectTransform),
                typeof(Text));
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.SetParent(parent, false);
            descriptionRect.anchorMin = new Vector2(0.5f, 0.5f);
            descriptionRect.anchorMax = new Vector2(0.5f, 0.5f);
            descriptionRect.pivot = new Vector2(0.5f, 0.5f);
            descriptionRect.anchoredPosition = new Vector2(0f, 18f);
            descriptionRect.sizeDelta = new Vector2(_itemWidth - 12f, 38f);

            Text descriptionText = descriptionObject.GetComponent<Text>();
            descriptionText.font = _pickerFont;
            descriptionText.text = description;
            descriptionText.fontSize = 11;
            descriptionText.resizeTextForBestFit = true;
            descriptionText.resizeTextMinSize = 9;
            descriptionText.resizeTextMaxSize = 11;
            descriptionText.alignment = TextAnchor.MiddleCenter;
            descriptionText.color = new Color32(195, 211, 224, 255);
            descriptionText.raycastTarget = false;
        }

        /// <summary>
        /// 보강판 탭과 카드의 C# 클릭 이벤트를 활성화하거나 해제한다.
        /// isBound를 사용하며 색상 및 효과 버튼의 핸들러 구독 상태를 변경한다.
        /// </summary>
        private void SetPlatePickerEventBindings(bool isBound)
        {
            if (_plateMaterialTabButton != null)
            {
                if (isBound) _plateMaterialTabButton.Clicked += HandlePlateMaterialTabClicked;
                else _plateMaterialTabButton.Clicked -= HandlePlateMaterialTabClicked;
            }
            if (_plateEffectTabButton != null)
            {
                if (isBound) _plateEffectTabButton.Clicked += HandlePlateEffectTabClicked;
                else _plateEffectTabButton.Clicked -= HandlePlateEffectTabClicked;
            }
            foreach (KeyboardEditButton button in _plateMaterialButtons)
            {
                if (isBound) button.Clicked += HandlePlateMaterialClicked;
                else button.Clicked -= HandlePlateMaterialClicked;
            }
            foreach (KeyboardEditButton button in _plateEffectButtons)
            {
                if (isBound) button.Clicked += HandlePlateEffectClicked;
                else button.Clicked -= HandlePlateEffectClicked;
            }
        }

        /// <summary>
        /// 보강판 선택 패널에서 색상 또는 효과 목록으로 전환한다.
        /// section에 맞춰 content와 탭 강조를 갱신하며 재질이나 효과 값은 변경하지 않는다.
        /// </summary>
        private void SetPlatePickerSection(PlatePickerSection section)
        {
            if (_plateViewport == null || _plateMaterialContent == null || _plateEffectContent == null)
                return;

            _platePickerSection = section;
            RectTransform activeContent = section == PlatePickerSection.Material
                ? _plateMaterialContent
                : _plateEffectContent;
            _plateMaterialContent.gameObject.SetActive(section == PlatePickerSection.Material);
            _plateEffectContent.gameObject.SetActive(section == PlatePickerSection.Effect);
            ScrollRect scrollRect = _plateViewport.GetComponent<ScrollRect>();
            scrollRect.content = activeContent;
            scrollRect.horizontalNormalizedPosition = 0f;
            if (UsesScreenTrays) scrollRect.verticalNormalizedPosition = 1f;
            SetPlatePickerTabSelection(_plateMaterialTabButton, section == PlatePickerSection.Material);
            SetPlatePickerTabSelection(_plateEffectTabButton, section == PlatePickerSection.Effect);
            ResetPlateTrayScroll();
        }

        /// <summary>
        /// 보강판 탭 버튼의 색상을 선택 여부에 맞춰 갱신한다.
        /// button과 isSelected를 사용하며 버튼 배경 및 포인터 강조 색을 변경한다.
        /// </summary>
        private void SetPlatePickerTabSelection(KeyboardEditButton button, bool isSelected)
        {
            if (UsesScreenTrays)
            {
                GetComponent<WorkshopKeycapPlacement>().ApplyPlacementModeButtonStyle(button, isSelected);
                Text label = button.GetComponentInChildren<Text>();
                label.font = _pickerFont;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
                return;
            }
            Image background = button.GetComponent<Image>();
            background.color = isSelected ? _pickerSelectedCardColor : _pickerCardColor;
            ColorBlock colors = button.colors;
            colors.normalColor = isSelected ? _pickerSelectedColor : _pickerBorderColor;
            colors.highlightedColor = isSelected ? _pickerFocusColor : _pickerHoverColor;
            colors.pressedColor = new Color32(128, 234, 255, 255);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        /// <summary>
        /// PlateType에 대응하는 재질 선택 카드 이름을 반환한다.
        /// type을 사용하며 표시용 한국어 이름 또는 타입 기본 이름을 반환한다.
        /// </summary>
        private static string GetPlateMaterialLabel(PlateType type)
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

        /// <summary>
        /// 효과 선택 카드에 사용할 짧은 한국어 이름을 반환한다.
        /// effect를 사용하며 None, Reverb, Pitch, Delay의 이름을 반환한다.
        /// </summary>
        private static string GetPlateEffectLabel(PlateEffect effect)
        {
            switch (effect)
            {
                case PlateEffect.None: return "기본";
                case PlateEffect.Reverb: return "수중";
                case PlateEffect.Pitch: return "하이 피치";
                case PlateEffect.Delay: return "에코";
                default: return effect.ToString();
            }
        }

        /// <summary>
        /// 효과 카드에 표시할 동작 설명을 반환한다.
        /// effect를 사용하며 실제 효과를 짧은 한국어 문구로 반환한다.
        /// </summary>
        private static string GetPlateEffectDescription(PlateEffect effect)
        {
            switch (effect)
            {
                case PlateEffect.None: return "효과 없이 재생";
                case PlateEffect.Reverb: return "물속 잔향";
                case PlateEffect.Pitch: return "높은 음색";
                case PlateEffect.Delay: return "짧은 메아리";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 등록된 케이스 렌더러의 메시를 키보드 로컬 좌표로 합친다.
        /// 편집 대상의 파츠 변환과 서브메시를 사용하며 합친 메시 또는 파츠가 없으면 null을 반환한다.
        /// </summary>
        private Mesh CreateCasePreviewMesh()
        {
            if (_assemblyCatalog && _caseCustomization.CaseRenderers.Count == 1)
                return Instantiate(_caseCustomization.CaseRenderers[0].GetComponent<MeshFilter>().sharedMesh);
            List<CombineInstance> parts = new List<CombineInstance>();
            Matrix4x4 keyboardInverse = _caseCustomization.transform.worldToLocalMatrix;
            foreach (MeshRenderer renderer in _caseCustomization.CaseRenderers)
            {
                MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    continue;

                for (int subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                {
                    parts.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = subMesh,
                        transform = keyboardInverse * renderer.transform.localToWorldMatrix
                    });
                }
            }

            if (parts.Count == 0)
                return null;

            Mesh mesh = new Mesh { name = "CasePreviewMesh", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            return mesh;
        }

        /// <summary>
        /// 케이스 재질 텍스처마다 이름과 미리보기가 있는 선택 버튼을 만든다.
        /// content와 previews를 사용하며 원본 인덱스와 선택 시각 요소를 저장한다.
        /// </summary>
        private void CreateCasePreviewItems(RectTransform content, Texture2D[] previews)
        {
            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null)
                    continue;

                Material material = _keycapMaterials[index];
                KeyboardEditButton button = CreatePickerItemButton(
                    content,
                    "CaseMaterial_" + material.name,
                    _casePreviewButtons.Count,
                    KeyboardPartDisplayNames.MaterialName(material),
                    out Image cardBackground,
                    out Text selectionIndicator);
                CreateMaterialPreviewImage(
                    button.transform,
                    "CasePreviewImage",
                    previews[index],
                    new Vector2(PICKER_PREVIEW_WIDTH, PICKER_PREVIEW_HEIGHT),
                    14f);
                selectionIndicator.transform.SetAsLastSibling();

                _casePreviewButtons.Add(button);
                _caseCardBackgrounds.Add(cardBackground);
                _caseMaterialIndices.Add(index);
                _caseSelectionIndicators.Add(selectionIndicator);
                if (_assemblyCatalog)
                {
                    WorkshopStructureMaterialDrag drag = button.gameObject.AddComponent<WorkshopStructureMaterialDrag>();
                    drag.Initialize(this, material, _pickerMode == PickerMode.Upper
                        ? AssemblyStructurePart.Upper : AssemblyStructurePart.Lower);
                    drag.enabled = _pickerMode == PickerMode.Upper || _pickerMode == PickerMode.CaseMaterial;
                }
            }
        }

        /// <summary>
        /// 목록이 소유한 미리보기 텍스처를 해제한다.
        /// textures의 생성된 텍스처를 파괴하고 목록을 비운다.
        /// </summary>
        private void DestroyPreviewTextures(List<Texture2D> textures)
        {
            foreach (Texture2D texture in textures)
            {
                if (texture != null)
                    Destroy(texture);
            }

            textures.Clear();
        }

        /// <summary>
        /// 선택한 편집 항목에 해당하는 목록만 표시한다.
        /// mode와 각 viewport를 사용하며 목록의 활성 상태와 좌측 버튼의 선택 강조를 변경한다.
        /// </summary>
        private void SetPickerMode(PickerMode mode)
        {
            if (_keycapButton == null || _switchAxisButton == null || _ledButton == null
                || _caseButton == null || _plateButton == null || _customButton == null
                || _assemblyCatalog && (_pcbButton == null || _upperButton == null)) return;
            PickerMode previous = _pickerMode;
            _pickerMode = mode == PickerMode.Keycap && _viewport == null ? PickerMode.None : mode;
            if (previous == PickerMode.Keycap && _pickerMode != PickerMode.Keycap)
            {
                CancelKeycapMaterialPreview();
            }
            _keycapButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(_pickerMode == PickerMode.Keycap);
            _switchAxisButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(_pickerMode == PickerMode.SwitchSound);
            _ledButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(_pickerMode == PickerMode.Led);
            _caseButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(_pickerMode == PickerMode.CaseMaterial);
            _plateButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(_pickerMode == PickerMode.Plate);
            _customButton.GetComponent<KeyboardEditSidebarButtonUI>().SetSelected(_pickerMode == PickerMode.Photo);
            if (_viewport != null)
            {
                _viewport.gameObject.SetActive(_keycapTrayContent != null ? _keycapTrayTransition.Category == WorkshopDrawerCategory.Keycaps : _pickerMode == PickerMode.Keycap);
            }

            _switchSoundViewport.gameObject.SetActive(_keycapTrayContent != null ? _keycapTrayTransition.Category == WorkshopDrawerCategory.Switches : _pickerMode == PickerMode.SwitchSound);
            if (_caseMaterialViewport != null)
            {
                _caseMaterialViewport.gameObject.SetActive(_pickerMode == PickerMode.CaseMaterial || _pickerMode == PickerMode.Upper);
            }
            if (_plateViewport != null)
            {
                _plateViewport.gameObject.SetActive(_pickerMode == PickerMode.Plate);
            }
            if (_plateTrayTabs != null) _plateTrayTabs.gameObject.SetActive(_pickerMode == PickerMode.Plate);
            if (_pickerMode != PickerMode.Keycap)
                _keycapUI.Close();
            if (_pickerMode != PickerMode.Photo)
                _photoUI.HideEmbedded();
            if (_pickerMode != PickerMode.Led)
                _ledUI.Close();
            if (_pickerMode == PickerMode.Photo)
                _photoUI.BeginCustomization();
            if (_pickerMode == PickerMode.Keycap && _keycapPlacement == null)
            {
                _keycapUI.Open(_photoStorage.Customization);
                RefreshKeycapMaterialSelection();
                PreviewMissingKeycapsWithCurrentMaterial();
            }
            if (_pickerMode == PickerMode.Led)
                _ledUI.Open(_ledCustomization);
            if (_assemblyCatalog) RefreshAssemblyControls();
            if (previous != _pickerMode)
            {
                string previousLogPart = GetLogPart(previous);
                string currentLogPart = GetLogPart(_pickerMode);
                if (previousLogPart != currentLogPart)
                    Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "PartChanged", currentLogPart,
                        previous: previousLogPart,
                        details: _catalogLayout != null && _catalogLayout.IsTransitioning ? "DrawerTransition" : "");
            }
        }

        /// <summary>
        /// mode를 일반·조립 편집 항목의 고정 로그 식별자로 변환하여 반환한다.
        /// 목록이 닫힌 상태는 빈 값으로 반환한다.
        /// </summary>
        private string GetLogPart(PickerMode mode)
        {
            switch (mode)
            {
                case PickerMode.Keycap: return "Keycap";
                case PickerMode.SwitchSound: return "Switch";
                case PickerMode.Led: return "LED";
                case PickerMode.CaseMaterial: return _assemblyCatalog ? "Lower" : "Case";
                case PickerMode.Pcb: return "Pcb";
                case PickerMode.Plate: return "Plate";
                case PickerMode.Upper: return "Upper";
                case PickerMode.Photo: return "CustomImage";
                default: return "";
            }
        }

        /// <summary>
        /// 현재 UI에 실제 제공된 파츠 요소를 관찰자에게 전달한다.
        /// 만들어진 미리보기 목록을 읽으며 초기화나 자동 복원을 선택으로 세지 않는다.
        /// </summary>
        public void ObserveAvailableOptions()
        {
            for (int index = 0; index < _previewMaterials.Count; index++)
                ObserveKeycap("AvailableOption", _previewDefinitions[index], _previewMaterials[index]);
            foreach (SwitchType type in _switchSoundTypes)
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "AvailableOption", "Switch", type.ToString());
            foreach (int index in _caseMaterialIndices)
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "AvailableOption", "Case", _keycapMaterials[index].name);
            foreach (Material material in _plateMaterialButtonMaterials)
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "AvailableOption", "PlateMaterial", material.name);
            foreach (PlateEffect effect in _plateEffects)
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "AvailableOption", "PlateEffect", effect.ToString());
            foreach (LEDPattern pattern in Enum.GetValues(typeof(LEDPattern)))
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "AvailableOption", "LED", pattern.ToString());
            foreach (string scope in new[] { "CaseWhole", "SingleKeycap", "AllKeycapsCanvas" })
                Game.Logging.CustomizationTelemetry.Emit(_caseCustomization, "AvailableOption", "CustomImage", scope);
        }

        /// <summary>
        /// rect의 화면 사각형에 포인터 위치가 들어가는지 확인한다.
        /// rect와 screenPosition을 사용하며 포함 여부를 반환한다.
        /// </summary>
        private bool ContainsPointer(RectTransform rect, Vector2 screenPosition)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
            {
                return false;
            }

            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, eventCamera);
        }

        /// <summary>
        /// type과 definition 및 material을 키캡 파츠 관찰값으로 전달한다.
        /// 적용 범위와 이전 파츠 및 성공 여부를 기록하며 게임 상태는 변경하지 않는다.
        /// </summary>
        private void ObserveKeycap(string type, KeycapDefinition definition, Material material,
            bool allKeys = false, int keycapIndex = -1, bool success = true, string previous = "", bool changed = false)
        {
            Game.Logging.CustomizationTelemetry.Emit(_photoStorage.Customization, type, "Keycap",
                Game.Logging.CustomizationTelemetry.GetKeycapElementId(definition, material), previous, success, changed,
                keycapIndex >= 0 || allKeys ? (allKeys ? "AllKeycaps" : "SingleKeycap") : "",
                !allKeys && keycapIndex >= 0 ? keycapIndex.ToString() : "",
                keycapKind: Game.Logging.CustomizationTelemetry.GetKeycapKind(definition, material),
                materialId: definition != null ? definition.MaterialId : material != null ? material.name : "");
        }
    }
}

