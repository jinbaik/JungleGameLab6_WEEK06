using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class KeyboardEditOverlayUI : MonoBehaviour, IPointerDownHandler
    {
        private const int PREVIEW_LAYER = 31;
        private const int PREVIEW_TEXTURE_SIZE = 128;
        private const float WHEEL_SCROLL_INPUT_SCALE = 20f;

        private static readonly Color _activeModeButtonColor = new Color(0.12f, 0.48f, 0.58f, 1f);
        private static readonly Color _inactiveModeButtonColor = new Color(0.12f, 0.17f, 0.24f, 0.98f);
        private static readonly SwitchType[] _switchSoundTypes = (SwitchType[])Enum.GetValues(typeof(SwitchType));

        [Header("Edit Buttons")]
        [SerializeField] private KeyboardEditButton _keycapButton;
        [SerializeField] private KeyboardEditButton _switchAxisButton;
        [SerializeField] private KeyboardEditButton _ledButton;
        [SerializeField] private KeyboardEditButton _caseButton;
        [SerializeField] private KeyboardEditButton _plateButton;
        [SerializeField] private KeyboardEditButton _customButton;

        [Header("Mode Buttons")]
        [SerializeField] private KeyboardEditButton _generalModeButton;
        [SerializeField] private KeyboardEditButton _attackModeButton;
        [SerializeField] private Text _modeStatus;
        private PickerMode _pickerMode;

        [Header("Keycap Material Picker")]
        [SerializeField] private GameObject _previewKeycapPrefab;
        [SerializeField] private Material[] _keycapMaterials = new Material[0];
        [SerializeField, Min(1f)] private float _viewportWidth = 860f;
        [SerializeField, Min(1f)] private float _itemWidth = 98f;
        [SerializeField, Min(0f)] private float _itemSpacing = 14f;
        [SerializeField, Min(0f)] private float _wheelScrollSpeed = 0.55f;

        private Canvas _canvas;
        private RectTransform _keyboardEditPanel;
        private RectTransform _viewport;
        private RectTransform _content;
        private readonly List<KeyboardEditButton> _previewButtons = new List<KeyboardEditButton>();
        private readonly List<Material> _previewMaterials = new List<Material>();
        private readonly List<Image> _selectionIndicators = new List<Image>();
        private readonly List<Texture2D> _previewTextures = new List<Texture2D>();
        private Material _selectedMaterial;

        [Header("Switch Sound Picker")]
        private RectTransform _switchSoundViewport;
        private readonly List<KeyboardEditButton> _switchSoundButtons = new List<KeyboardEditButton>();
        private SwitchType? _selectedSwitchSound;

        [Header("Case Material Picker")]
        private KeyboardCustomization _caseCustomization;
        private RectTransform _caseMaterialViewport;
        private readonly List<KeyboardEditButton> _casePreviewButtons = new List<KeyboardEditButton>();
        private readonly List<int> _caseMaterialIndices = new List<int>();
        private readonly List<Image> _caseSelectionIndicators = new List<Image>();
        private readonly List<Texture2D> _casePreviewTextures = new List<Texture2D>();

        [Header("Plate Picker")]
        private KeyboardCustomization _plateCustomization;
        private RectTransform _plateViewport;
        private readonly List<KeyboardEditButton> _platePreviewButtons = new List<KeyboardEditButton>();
        private readonly List<PlateType> _plateTypes = new List<PlateType>();
        private readonly List<PlateType> _platePreviewTypes = new List<PlateType>();
        private readonly List<Image> _plateSelectionIndicators = new List<Image>();
        private readonly List<Texture2D> _platePreviewTextures = new List<Texture2D>();

        [Header("Photo Customization")]
        [SerializeField] private KeyboardCustomizationStorage _photoStorage;
        private KeyboardCustomizationTestUI _photoUI;

        [Header("LED Customization")]
        private KeyboardCustomization _ledCustomization;
        private KeyboardLEDPanelUI _ledUI;

        public event Action<Material> KeycapMaterialSelected;
        public event Action<SwitchType> SwitchSoundSelected;
        public event Action<int> CaseMaterialSelected;
        public event Action<PlateType> PlateTypeSelected;
        public event Action<bool> AttackModeSelected;

        private enum PickerMode
        {
            None,
            Keycap,
            SwitchSound,
            CaseMaterial,
            Plate,
            Photo,
            Led
        }

        void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();
            _keyboardEditPanel = transform.Find("KeyboardEditPanel") as RectTransform;
            CreateMaterialPicker();
            CreateSwitchSoundPicker();
            _photoUI = gameObject.AddComponent<KeyboardCustomizationTestUI>();
            _photoUI.InitializeEmbedded(_photoStorage);
            _photoUI.CloseRequested += HandlePhotoCloseRequested;
            _photoUI.CustomizationLoaded += HandlePhotoCustomizationLoaded;
            GameObject ledPanel = new GameObject("LEDCustomizationPanel", typeof(RectTransform));
            ledPanel.transform.SetParent(transform, false);
            ledPanel.SetActive(false);
            _ledUI = ledPanel.AddComponent<KeyboardLEDPanelUI>();
            _ledUI.Initialize();
        }

        void OnEnable()
        {
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

            foreach (KeyboardEditButton button in _platePreviewButtons)
            {
                button.Clicked += HandlePlatePreviewClicked;
            }

            SetPickerMode(PickerMode.None);
        }

        void OnDisable()
        {
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

            foreach (KeyboardEditButton button in _platePreviewButtons)
            {
                button.Clicked -= HandlePlatePreviewClicked;
            }

            SetPickerMode(PickerMode.None);
        }

        void OnDestroy()
        {
            _photoUI.CloseRequested -= HandlePhotoCloseRequested;
            _photoUI.CustomizationLoaded -= HandlePhotoCustomizationLoaded;
            DestroyPreviewTextures(_previewTextures);
            DestroyPreviewTextures(_casePreviewTextures);
            DestroyPreviewTextures(_platePreviewTextures);
        }

        /// <summary>
        /// 편집 UI의 빈 영역을 왼쪽 클릭하면 열린 선택 목록을 닫는다.
        /// eventData의 위치를 사용하며 목록 안에서 시작한 입력은 유지한다.
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left
                && !ContainsPointer(_viewport, eventData.position)
                && !ContainsPointer(_switchSoundViewport, eventData.position)
                && !ContainsPointer(_caseMaterialViewport, eventData.position)
                && !ContainsPointer(_plateViewport, eventData.position)
                && !_photoUI.IsPointerOverUI(eventData.position)
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

        /// <summary>
        /// 편집할 키보드의 케이스 재질과 미리보기 원본을 지정한다.
        /// customization을 저장하며 다음 활성화 시 해당 키보드의 목록을 만든다.
        /// </summary>
        public void SetCaseCustomization(KeyboardCustomization customization)
        {
            _caseCustomization = customization;
        }

        /// <summary>
        /// 보강판 선택 UI가 사용할 편집 키보드를 지정한다.
        /// customization의 보강판 데이터와 현재 타입을 목록 생성 및 선택 표시에 사용한다.
        /// </summary>
        public void SetPlateCustomization(KeyboardCustomization customization)
        {
            _plateCustomization = customization;
        }

        /// <summary>
        /// customization을 기존 사진 편집 UI의 저장 및 복원 대상으로 연결한다.
        /// 작업대에서 편집하는 키보드만 변경되며 단일 저장 파일과 재질 카탈로그는 유지한다.
        /// </summary>
        public void SetPhotoCustomization(KeyboardCustomization customization)
        {
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
        /// 공격 모드 또는 일반 모드 버튼의 선택 색상을 갱신한다.
        /// attackModeSelected를 사용하며 선택된 모드 버튼을 활성 색상으로 표시한다.
        /// </summary>
        public void SetSelectedAttackMode(bool attackModeSelected)
        {
            _generalModeButton.targetGraphic.color = attackModeSelected
                ? _inactiveModeButtonColor
                : _activeModeButtonColor;
            _attackModeButton.targetGraphic.color = attackModeSelected
                ? _activeModeButtonColor
                : _inactiveModeButtonColor;
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
            SetPickerMode(PickerMode.None);
        }

        /// <summary>
        /// 사진 저장 상태를 복원한 키보드의 재질 선택 표시를 갱신한다.
        /// 현재 저장 대상의 키캡 및 케이스 선택을 사용하며 사진 편집 모드는 유지한다.
        /// </summary>
        private void HandlePhotoCustomizationLoaded()
        {
            SetSelectedMaterial(_photoStorage.Customization.KeycapMaterial);
            RefreshCaseMaterialSelection();
            RefreshPlateSelection();
        }

        /// <summary>
        /// 현재 케이스의 재질 인덱스와 일치하는 미리보기에 선택 표시를 켠다.
        /// 편집 대상과 재질 인덱스 목록을 사용하며 각 표시의 활성 상태를 갱신한다.
        /// </summary>
        public void RefreshCaseMaterialSelection()
        {
            for (int index = 0; index < _caseSelectionIndicators.Count; index++)
            {
                _caseSelectionIndicators[index].enabled = _caseMaterialIndices[index] == _caseCustomization.CaseMaterialIndex;
            }
        }

        /// <summary>
        /// 현재 보강판 타입과 일치하는 미리보기의 선택 표시를 갱신한다.
        /// 편집 대상의 현재 타입과 목록 순서를 사용하며 선택된 항목만 표시한다.
        /// </summary>
        public void RefreshPlateSelection()
        {
            if (_plateCustomization == null)
                return;

            for (int index = 0; index < _plateSelectionIndicators.Count; index++)
            {
                _plateSelectionIndicators[index].enabled = _platePreviewTypes[index] == _plateCustomization.CurrentPlateType;
            }
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

            return ContainsPointer(_viewport, screenPosition)
                || ContainsPointer(_switchSoundViewport, screenPosition)
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
            if (button == _keycapButton)
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
            SetPickerMode(mode == _pickerMode ? PickerMode.None : mode);
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
            SelectMaterial(_previewButtons.IndexOf(button));
        }

        /// <summary>
        /// 클릭한 축 사운드 버튼의 타입을 편집 대상에 전달한다.
        /// button의 목록 인덱스를 사용하며 실제 적용은 C# 이벤트 구독자에게 요청한다.
        /// </summary>
        private void HandleSwitchSoundClicked(KeyboardEditButton button)
        {
            SwitchSoundSelected?.Invoke(_switchSoundTypes[_switchSoundButtons.IndexOf(button)]);
        }

        /// <summary>
        /// 클릭한 케이스 미리보기에 해당하는 공유 재질 인덱스를 전달한다.
        /// button으로 원본 인덱스를 찾아 C# 이벤트로 적용을 요청하며 목록은 유지한다.
        /// </summary>
        private void HandleCasePreviewClicked(KeyboardEditButton button)
        {
            CaseMaterialSelected?.Invoke(_caseMaterialIndices[_casePreviewButtons.IndexOf(button)]);
        }

        /// <summary>
        /// 클릭한 보강판 미리보기의 타입을 편집 대상으로 전달한다.
        /// button의 목록 인덱스를 사용하며 보강판 타입을 C# 이벤트로 전달한다.
        /// </summary>
        private void HandlePlatePreviewClicked(KeyboardEditButton button)
        {
            int index = _platePreviewButtons.IndexOf(button);
            if (index < 0 || index >= _platePreviewTypes.Count)
                return;

            PlateTypeSelected?.Invoke(_platePreviewTypes[index]);
        }

        /// <summary>
        /// 편집 패널에 재질 미리보기 목록과 가로 스크롤 영역을 만든다.
        /// 프리팹 메시와 재질 배열을 사용하며 화면에는 키캡 이미지만 표시한다.
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

            Texture2D[] previews = CreateMaterialPreviewTextures(shellMeshFilter.sharedMesh);
            CreateViewport();
            CreatePreviewItems(previews);
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
        /// 각 재질을 공통 키캡 메시로 오프스크린 렌더링해 UI용 텍스처를 만든다.
        /// keycapMesh와 _keycapMaterials를 사용하며 배열 순서를 유지한 Texture2D 목록을 반환한다.
        /// </summary>
        private Texture2D[] CreateMaterialPreviewTextures(Mesh keycapMesh)
        {
            return CreateMaterialPreviewTextures(keycapMesh, _keycapMaterials, _previewTextures, "Keycap");
        }

        /// <summary>
        /// 미리보기 메시와 재질 목록을 오프스크린 렌더링해 텍스처 배열을 만든다.
        /// previewMesh, materials, previewName을 사용하며 생성한 텍스처를 textures에 기록하고 반환한다.
        /// </summary>
        private Texture2D[] CreateMaterialPreviewTextures(
            Mesh previewMesh,
            IReadOnlyList<Material> materials,
            List<Texture2D> textures,
            string previewName)
        {
            Texture2D[] previews = new Texture2D[materials.Count];
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

            try
            {
                for (int index = 0; index < materials.Count; index++)
                {
                    Material material = materials[index];
                    if (material == null)
                    {
                        continue;
                    }

                    meshRenderer.sharedMaterial = material;
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
            }

            return previews;
        }

        /// <summary>
        /// 화면 상단에 잘리는 투명 viewport와 그 안의 가로 콘텐츠 영역을 만든다.
        /// overlay 크기를 사용하며 이후 목록 스크롤이 적용될 RectTransform을 저장한다.
        /// </summary>
        private void CreateViewport()
        {
            _viewport = CreateScrollViewport("KeycapMaterial", _keycapMaterials.Length, out _content);
        }

        /// <summary>
        /// 화면 상단에 이름과 항목 개수에 맞는 가로 스크롤 목록을 만든다.
        /// listName과 itemCount로 영역을 구성하고 viewport와 content를 반환한다.
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
            viewport.sizeDelta = new Vector2(viewportWidth, 132f);
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = Color.clear;
            viewportImage.raycastTarget = true;

            GameObject contentObject = new GameObject(listName + "Content", typeof(RectTransform));
            content = contentObject.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 0.5f);
            content.anchorMax = new Vector2(0f, 0.5f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            float contentWidth = itemCount * (_itemWidth + _itemSpacing) - _itemSpacing;
            content.sizeDelta = new Vector2(Mathf.Max(0f, contentWidth), 116f);

            ScrollRect scrollRect = viewportObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = _wheelScrollSpeed * WHEEL_SCROLL_INPUT_SCALE;

            viewport.gameObject.SetActive(false);
            return viewport;
        }

        /// <summary>
        /// 지원하는 축 타입마다 이름이 있는 사운드 선택 버튼을 만든다.
        /// 축 타입 배열과 기존 버튼의 글꼴을 사용하며 상단 목록과 버튼 참조를 저장한다.
        /// </summary>
        private void CreateSwitchSoundPicker()
        {
            _switchSoundViewport = CreateScrollViewport("SwitchSound", _switchSoundTypes.Length, out RectTransform content);
            Font font = _switchAxisButton.GetComponentInChildren<Text>().font;
            float stride = _itemWidth + _itemSpacing;
            for (int index = 0; index < _switchSoundTypes.Length; index++)
            {
                GameObject itemObject = new GameObject(
                    "SwitchSound_" + _switchSoundTypes[index],
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(KeyboardEditButton));
                RectTransform itemRect = itemObject.GetComponent<RectTransform>();
                itemRect.SetParent(content, false);
                itemRect.anchorMin = new Vector2(0f, 0.5f);
                itemRect.anchorMax = new Vector2(0f, 0.5f);
                itemRect.pivot = new Vector2(0.5f, 0.5f);
                itemRect.sizeDelta = new Vector2(_itemWidth, 116f);
                itemRect.anchoredPosition = new Vector2(index * stride + _itemWidth * 0.5f, 0f);
                Image itemImage = itemObject.GetComponent<Image>();
                itemImage.color = Color.clear;
                itemImage.raycastTarget = true;

                GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.SetParent(itemRect, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(2f, 10f);
                labelRect.offsetMax = new Vector2(-2f, -10f);
                Text label = labelObject.GetComponent<Text>();
                label.font = font;
                label.text = _switchSoundTypes[index].ToString();
                label.fontSize = 20;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 20;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.raycastTarget = false;

                KeyboardEditButton button = itemObject.GetComponent<KeyboardEditButton>();
                button.targetGraphic = label;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                _switchSoundButtons.Add(button);
            }

            RefreshSwitchSoundSelection();
        }

        /// <summary>
        /// 적용된 축 사운드와 일치하는 버튼을 청록색으로 표시한다.
        /// 저장된 선택 타입과 버튼 목록을 사용하며 각 버튼의 기본 색상을 갱신한다.
        /// </summary>
        private void RefreshSwitchSoundSelection()
        {
            for (int index = 0; index < _switchSoundButtons.Count; index++)
            {
                bool isSelected = _selectedSwitchSound == _switchSoundTypes[index];
                ColorBlock colors = _switchSoundButtons[index].colors;
                colors.normalColor = isSelected
                    ? new Color(0.36f, 0.82f, 1f, 1f)
                    : Color.white;
                colors.highlightedColor = isSelected ? colors.normalColor : new Color(0.85f, 0.95f, 1f, 1f);
                colors.selectedColor = colors.normalColor;
                _switchSoundButtons[index].colors = colors;
            }
        }

        /// <summary>
        /// 텍스처가 생성된 재질마다 이름 없는 미리보기 버튼을 콘텐츠에 추가한다.
        /// previews의 텍스처와 재질 순서를 사용하며 버튼과 선택 표시를 저장한다.
        /// </summary>
        private void CreatePreviewItems(Texture2D[] previews)
        {
            if (_content == null)
            {
                return;
            }

            float stride = _itemWidth + _itemSpacing;
            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null || _keycapMaterials[index] == null)
                {
                    continue;
                }

                GameObject itemObject = new GameObject(
                    "KeycapMaterialPreview",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(KeyboardEditButton));
                RectTransform itemRect = itemObject.GetComponent<RectTransform>();
                itemRect.SetParent(_content, false);
                itemRect.anchorMin = new Vector2(0f, 0.5f);
                itemRect.anchorMax = new Vector2(0f, 0.5f);
                itemRect.pivot = new Vector2(0.5f, 0.5f);
                itemRect.sizeDelta = new Vector2(_itemWidth, 116f);
                itemRect.anchoredPosition = new Vector2(index * stride + _itemWidth * 0.5f, 0f);
                Image itemImage = itemObject.GetComponent<Image>();
                itemImage.color = Color.clear;
                itemImage.raycastTarget = true;

                GameObject imageObject = new GameObject(
                    "KeycapPreviewImage",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                RectTransform imageRect = imageObject.GetComponent<RectTransform>();
                imageRect.SetParent(itemRect, false);
                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.anchoredPosition = new Vector2(0f, 5f);
                imageRect.sizeDelta = new Vector2(92f, 92f);
                RawImage previewImage = imageObject.GetComponent<RawImage>();
                previewImage.texture = previews[index];
                previewImage.color = Color.white;
                previewImage.raycastTarget = false;

                KeyboardEditButton previewButton = itemObject.GetComponent<KeyboardEditButton>();
                previewButton.targetGraphic = previewImage;
                previewButton.navigation = new Navigation { mode = Navigation.Mode.None };

                GameObject indicatorObject = new GameObject(
                    "SelectedIndicator",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                RectTransform indicatorRect = indicatorObject.GetComponent<RectTransform>();
                indicatorRect.SetParent(itemRect, false);
                indicatorRect.anchorMin = new Vector2(0.5f, 0f);
                indicatorRect.anchorMax = new Vector2(0.5f, 0f);
                indicatorRect.pivot = new Vector2(0.5f, 0f);
                indicatorRect.anchoredPosition = new Vector2(0f, 2f);
                indicatorRect.sizeDelta = new Vector2(48f, 3f);
                Image indicator = indicatorObject.GetComponent<Image>();
                indicator.color = new Color(0.36f, 0.82f, 1f, 1f);
                indicator.raycastTarget = false;
                indicator.enabled = false;

                _previewButtons.Add(previewButton);
                _previewMaterials.Add(_keycapMaterials[index]);
                _selectionIndicators.Add(indicator);
            }
        }

        /// <summary>
        /// index에 해당하는 Material을 저장하고 선택 표시와 C# 이벤트를 갱신한다.
        /// 유효한 항목의 재질을 사용하며 반환값 없이 선택 상태를 변경한다.
        /// </summary>
        private void SelectMaterial(int index)
        {
            if (index < 0 || index >= _previewMaterials.Count)
            {
                return;
            }

            _selectedMaterial = _previewMaterials[index];
            RefreshSelectionIndicators();
            KeycapMaterialSelected?.Invoke(_selectedMaterial);
        }

        /// <summary>
        /// 저장된 재질과 각 미리보기 재질을 비교해 선택 표시를 갱신한다.
        /// _selectedMaterial과 목록을 사용하며 일치하는 항목의 표시만 켠다.
        /// </summary>
        private void RefreshSelectionIndicators()
        {
            for (int index = 0; index < _selectionIndicators.Count; index++)
            {
                _selectionIndicators[index].enabled = _previewMaterials[index] == _selectedMaterial;
            }
        }

        /// <summary>
        /// 현재 키보드의 케이스 파츠와 재질 목록으로 이름 없는 미리보기 목록을 만든다.
        /// 저장된 편집 대상을 사용하며 이전 목록과 텍스처를 정리하고 버튼 및 선택 표시를 저장한다.
        /// </summary>
        private void CreateCaseMaterialPicker()
        {
            if (_caseMaterialViewport != null)
            {
                Destroy(_caseMaterialViewport.gameObject);
                _caseMaterialViewport = null;
            }

            _casePreviewButtons.Clear();
            _caseMaterialIndices.Clear();
            _caseSelectionIndicators.Clear();
            DestroyPreviewTextures(_casePreviewTextures);
            _caseButton.interactable = _caseCustomization != null
                && _caseCustomization.CaseMaterials.Count > 0
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
                previews = CreateMaterialPreviewTextures(caseMesh, _caseCustomization.CaseMaterials, _casePreviewTextures, "Case");
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
        /// 편집 키보드의 등록된 보강판 재질을 이름 없는 가로 미리보기 목록으로 만든다.
        /// 보강판 메시와 PlateData를 사용하며 선택 버튼, 타입, 표시 텍스처를 저장한다.
        /// </summary>
        private void CreatePlatePicker()
        {
            if (_plateViewport != null)
            {
                Destroy(_plateViewport.gameObject);
                _plateViewport = null;
            }

            _platePreviewButtons.Clear();
            _plateTypes.Clear();
            _platePreviewTypes.Clear();
            _plateSelectionIndicators.Clear();
            DestroyPreviewTextures(_platePreviewTextures);

            _plateButton.interactable = _plateCustomization != null
                && _plateCustomization.PlateRenderer != null;
            if (!_plateButton.interactable)
                return;

            MeshFilter plateMeshFilter = _plateCustomization.PlateRenderer.GetComponent<MeshFilter>();
            if (plateMeshFilter == null || plateMeshFilter.sharedMesh == null)
            {
                _plateButton.interactable = false;
                return;
            }

            List<Material> materials = new List<Material>();
            foreach (PlateData plateData in _plateCustomization.AvailablePlates)
            {
                if (plateData == null || plateData.Material == null)
                    continue;

                _plateTypes.Add(plateData.Type);
                materials.Add(plateData.Material);
            }

            if (materials.Count == 0)
            {
                _plateButton.interactable = false;
                return;
            }

            Texture2D[] previews = CreateMaterialPreviewTextures(
                plateMeshFilter.sharedMesh,
                materials,
                _platePreviewTextures,
                "Plate");
            _plateViewport = CreateScrollViewport("Plate", previews.Length, out RectTransform content);
            CreatePlatePreviewItems(content, previews);
            RefreshPlateSelection();
        }

        /// <summary>
        /// 등록된 케이스 렌더러의 메시를 키보드 로컬 좌표로 합친다.
        /// 편집 대상의 파츠 변환과 서브메시를 사용하며 합친 메시 또는 파츠가 없으면 null을 반환한다.
        /// </summary>
        private Mesh CreateCasePreviewMesh()
        {
            List<CombineInstance> parts = new List<CombineInstance>();
            Matrix4x4 keyboardInverse = _caseCustomization.transform.worldToLocalMatrix;
            foreach (MeshRenderer renderer in _caseCustomization.CaseRenderers)
            {
                MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null)
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
        /// 케이스 재질 텍스처마다 이름 없는 선택 버튼과 선택 표시를 만든다.
        /// content와 previews를 사용하며 원본 재질 인덱스와 버튼 참조를 저장한다.
        /// </summary>
        private void CreateCasePreviewItems(RectTransform content, Texture2D[] previews)
        {
            float stride = _itemWidth + _itemSpacing;
            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null)
                    continue;

                GameObject itemObject = new GameObject(
                    "CaseMaterialPreview", typeof(RectTransform), typeof(Image), typeof(KeyboardEditButton));
                RectTransform itemRect = itemObject.GetComponent<RectTransform>();
                itemRect.SetParent(content, false);
                itemRect.anchorMin = new Vector2(0f, 0.5f);
                itemRect.anchorMax = new Vector2(0f, 0.5f);
                itemRect.pivot = new Vector2(0.5f, 0.5f);
                itemRect.anchoredPosition = new Vector2(index * stride + _itemWidth * 0.5f, 0f);
                itemRect.sizeDelta = new Vector2(_itemWidth, 116f);
                Image itemImage = itemObject.GetComponent<Image>();
                itemImage.color = Color.clear;
                itemImage.raycastTarget = true;

                GameObject imageObject = new GameObject("CasePreviewImage", typeof(RectTransform), typeof(RawImage));
                RectTransform imageRect = imageObject.GetComponent<RectTransform>();
                imageRect.SetParent(itemRect, false);
                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.anchoredPosition = new Vector2(0f, 5f);
                imageRect.sizeDelta = new Vector2(92f, 92f);
                RawImage image = imageObject.GetComponent<RawImage>();
                image.texture = previews[index];
                image.raycastTarget = false;

                KeyboardEditButton button = itemObject.GetComponent<KeyboardEditButton>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };

                GameObject indicatorObject = new GameObject("SelectedIndicator", typeof(RectTransform), typeof(Image));
                RectTransform indicatorRect = indicatorObject.GetComponent<RectTransform>();
                indicatorRect.SetParent(itemRect, false);
                indicatorRect.anchorMin = new Vector2(0.5f, 0f);
                indicatorRect.anchorMax = new Vector2(0.5f, 0f);
                indicatorRect.pivot = new Vector2(0.5f, 0f);
                indicatorRect.anchoredPosition = new Vector2(0f, 2f);
                indicatorRect.sizeDelta = new Vector2(48f, 3f);
                Image indicator = indicatorObject.GetComponent<Image>();
                indicator.color = new Color(0.36f, 0.82f, 1f, 1f);
                indicator.raycastTarget = false;

                _casePreviewButtons.Add(button);
                _caseMaterialIndices.Add(index);
                _caseSelectionIndicators.Add(indicator);
            }
        }

        /// <summary>
        /// 보강판 재질 미리보기마다 이름 없는 선택 버튼과 선택 표시를 만든다.
        /// content와 previews를 사용하며 버튼, PlateType, 표시 참조를 같은 순서로 저장한다.
        /// </summary>
        private void CreatePlatePreviewItems(RectTransform content, Texture2D[] previews)
        {
            float stride = _itemWidth + _itemSpacing;
            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null)
                    continue;

                GameObject itemObject = new GameObject(
                    "PlateMaterialPreview", typeof(RectTransform), typeof(Image), typeof(KeyboardEditButton));
                RectTransform itemRect = itemObject.GetComponent<RectTransform>();
                itemRect.SetParent(content, false);
                itemRect.anchorMin = new Vector2(0f, 0.5f);
                itemRect.anchorMax = new Vector2(0f, 0.5f);
                itemRect.pivot = new Vector2(0.5f, 0.5f);
                itemRect.anchoredPosition = new Vector2(index * stride + _itemWidth * 0.5f, 0f);
                itemRect.sizeDelta = new Vector2(_itemWidth, 116f);
                Image itemImage = itemObject.GetComponent<Image>();
                itemImage.color = Color.clear;
                itemImage.raycastTarget = true;

                GameObject imageObject = new GameObject("PlatePreviewImage", typeof(RectTransform), typeof(RawImage));
                RectTransform imageRect = imageObject.GetComponent<RectTransform>();
                imageRect.SetParent(itemRect, false);
                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.anchoredPosition = new Vector2(0f, 5f);
                imageRect.sizeDelta = new Vector2(92f, 92f);
                RawImage image = imageObject.GetComponent<RawImage>();
                image.texture = previews[index];
                image.raycastTarget = false;

                KeyboardEditButton button = itemObject.GetComponent<KeyboardEditButton>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };

                GameObject indicatorObject = new GameObject("SelectedIndicator", typeof(RectTransform), typeof(Image));
                RectTransform indicatorRect = indicatorObject.GetComponent<RectTransform>();
                indicatorRect.SetParent(itemRect, false);
                indicatorRect.anchorMin = new Vector2(0.5f, 0f);
                indicatorRect.anchorMax = new Vector2(0.5f, 0f);
                indicatorRect.pivot = new Vector2(0.5f, 0f);
                indicatorRect.anchoredPosition = new Vector2(0f, 2f);
                indicatorRect.sizeDelta = new Vector2(48f, 3f);
                Image indicator = indicatorObject.GetComponent<Image>();
                indicator.color = new Color(0.36f, 0.82f, 1f, 1f);
                indicator.raycastTarget = false;
                indicator.enabled = false;

                _platePreviewButtons.Add(button);
                _platePreviewTypes.Add(_plateTypes[index]);
                _plateSelectionIndicators.Add(indicator);
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
        /// mode와 각 viewport를 사용하며 표시할 목록의 종류와 활성 상태를 변경한다.
        /// </summary>
        private void SetPickerMode(PickerMode mode)
        {
            _pickerMode = mode == PickerMode.Keycap && _viewport == null ? PickerMode.None : mode;
            if (_viewport != null)
            {
                _viewport.gameObject.SetActive(_pickerMode == PickerMode.Keycap);
            }

            _switchSoundViewport.gameObject.SetActive(_pickerMode == PickerMode.SwitchSound);
            if (_caseMaterialViewport != null)
            {
                _caseMaterialViewport.gameObject.SetActive(_pickerMode == PickerMode.CaseMaterial);
            }
            if (_plateViewport != null)
            {
                _plateViewport.gameObject.SetActive(_pickerMode == PickerMode.Plate);
            }
            if (_pickerMode != PickerMode.Photo)
                _photoUI.HideEmbedded();
            if (_pickerMode != PickerMode.Led)
                _ledUI.Close();
            if (_pickerMode == PickerMode.Photo)
                _photoUI.BeginCustomization();
            if (_pickerMode == PickerMode.Led)
                _ledUI.Open(_ledCustomization);
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
    }
}
