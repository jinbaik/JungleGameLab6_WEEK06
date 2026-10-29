using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

using KeyboardModeling;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class KeyboardCustomization : MonoBehaviour
{
    private const float MAX_LED_INTENSITY = 10f;
    private const float MAX_LED_SPEED = 10f;

    private static readonly IReadOnlyList<PlateEffect> _plateEffects = Array.AsReadOnly((PlateEffect[])Enum.GetValues(typeof(PlateEffect)));

    [Header("LED Layer")]
    [SerializeField, HideInInspector] private MeshRenderer _ledRenderer;
    [SerializeField] private KeyboardLED _ledLayer;

    [Header("LED Settings")]
    [SerializeField] private bool _ledEnabled = true;
    [SerializeField, ColorUsage(false, true)] private Color _ledColor = new Color(0f, 0.8f, 1f, 1f);
    [SerializeField, Range(0f, MAX_LED_INTENSITY)] private float _ledIntensity = 3f;
    [SerializeField] private LEDPattern _ledPattern = LEDPattern.Static;
    [SerializeField, Range(0f, MAX_LED_SPEED)] private float _ledSpeed = 1f;

    public static float MaxLEDIntensity => MAX_LED_INTENSITY;
    public static float MaxLEDSpeed => MAX_LED_SPEED;
    public bool LEDEnabled => _ledEnabled;
    public Color LEDColor => _ledColor;
    public float LEDIntensity => _ledIntensity;
    public LEDPattern CurrentLEDPattern => _ledPattern;
    public float LEDSpeed => _ledSpeed;

    [Header("Shared Materials")]
    [FormerlySerializedAs("_caseMaterials")]
    [Tooltip("키보드 테두리, 키캡과 보강판이 사용하는 공유 Material")]
    [SerializeField] private Material[] _materials = new Material[0];

    [Header("Case: 키보드 테두리(상하좌우 아래)")]
    [FormerlySerializedAs("_housingRenderers")]
    [SerializeField] private MeshRenderer[] _caseRenderers = new MeshRenderer[0];
    [FormerlySerializedAs("_housingMaterial")]
    [FormerlySerializedAs("_casseMaterial")]
    [SerializeField] private Material _caseMaterial;
    [SerializeField, Min(0)] private int _caseMaterialIndex;

    /// <summary>
    /// 케이스 선택 UI에 사용할 공유 재질 목록을 반환한다.
    /// 등록된 _materials를 읽기 전용 목록으로 제공한다.
    /// </summary>
    public IReadOnlyList<Material> CaseMaterials => _materials;

    /// <summary>
    /// 케이스 미리보기에 사용할 파츠 렌더러 목록을 반환한다.
    /// 등록된 _caseRenderers를 읽기 전용 목록으로 제공한다.
    /// </summary>
    public IReadOnlyList<MeshRenderer> CaseRenderers => _caseRenderers;

    /// <summary>
    /// 현재 케이스에 선택된 공유 재질의 인덱스를 반환한다.
    /// 저장된 _caseMaterialIndex를 UI 선택 표시에 제공한다.
    /// </summary>
    public int CaseMaterialIndex => _caseMaterialIndex;

    [Header("Keycaps")]
    [SerializeField] private MeshRenderer[] _keycapRenderers = new MeshRenderer[0];
    [SerializeField] private Material _keycapMaterial;
    [SerializeField, Min(0)] private int _keycapMaterialIndex;
    private Transform[] _keycaps;
    private KeyboardImageCustomization _imageCustomization;
    private readonly Dictionary<Transform, Material> _keycapMaterialOverrides = new Dictionary<Transform, Material>();

    public string ImageError => _imageCustomization != null ? _imageCustomization.LastError : "";
    public int PhotoKeycapCount => GetImageCustomization().KeycapCount;

    /// <summary>
    /// 입력 키에 바인딩된 물리 키캡을 사진용 키캡 배열의 인덱스로 변환한다.
    /// key를 사용하며, 대응하는 키캡이 없으면 false와 -1을 반환한다.
    /// </summary>
    public bool TryGetPhotoKeycapIndex(Key key, out int keycapIndex)
    {
        keycapIndex = -1;
        KeyboardInputController input = GetComponent<KeyboardInputController>();
        if (input == null || !input.TryGetKeycapTransform(key, out Transform inputKeycap))
            return false;

        if (_keycaps == null)
            CacheKeycaps();

        for (int index = 0; index < _keycaps.Length; index++)
        {
            Transform photoKeycap = _keycaps[index];
            if (photoKeycap == null)
                continue;

            if (photoKeycap == inputKeycap
                || photoKeycap.IsChildOf(inputKeycap)
                || inputKeycap.IsChildOf(photoKeycap))
            {
                keycapIndex = index;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 현재 전체 키캡에 지정된 Material을 반환한다.
    /// 사용자 지정 상태를 읽으며 반환값은 _keycapMaterial이다.
    /// </summary>
    public Material KeycapMaterial => _keycapMaterial;

    [Header("Photo Customization")]
    [SerializeField] private Material _photoMaterialTemplate;

    [Header("Part Installation")]
    [SerializeField] private KeyboardPartInstallation _partInstallation;

    [Header("Switches")]
    [SerializeField] private KeyboardSwitch[] _switches = new KeyboardSwitch[0];
    [SerializeField] private SwitchType _switchType = SwitchType.Red;

    [Header("보강판 재질")]
    [SerializeField] private KeyboardPlate _plate;
    [SerializeField] private Material _plateMaterial;
    [SerializeField, Min(-1)] private int _plateMaterialIndex = -1;
    [SerializeField, HideInInspector] private PlateType _plateType = PlateType.Nomal;
    [SerializeField, HideInInspector] private bool _plateSettingsSeparated;
    private Material _appliedPlateMaterial;
    private int _appliedPlateMaterialIndex = -1;
    private bool _plateMaterialApplied;

    [Header("보강판 효과")]
    [SerializeField] private PlateEffect _plateEffect = PlateEffect.None;
    private PlateEffect _appliedPlateEffect;
    private bool _plateEffectApplied;
    private AudioManager _plateAudioManager;
    private int _plateAudioSelection;

    public KeyboardPlate Plate => _plate;

    public IReadOnlyList<PlateData> AvailablePlates => _plate != null ? _plate.AvailablePlates : Array.Empty<PlateData>();
    public MeshRenderer PlateRenderer => _plate != null ? _plate.PlateRenderer : null;
    public PlateType CurrentPlateType => _plateType;
    public IReadOnlyList<Material> PlateMaterials => Array.AsReadOnly(_materials);
    public Material PlateMaterial => _plateMaterial;
    public int PlateMaterialIndex => _plateMaterialIndex;
    public IReadOnlyList<PlateEffect> AvailablePlateEffects => _plateEffects;
    public PlateEffect CurrentPlateEffect => _plateEffect;
    public event Action<Material> PlateMaterialChanged;
    public event Action<PlateEffect> PlateEffectChanged;

    /// <summary>
    /// 실제 적용된 파츠 설정을 로그용 식별자 목록으로 반환한다.
    /// 현재 재질과 축, 보강판 및 LED 값을 읽으며 이미지 바이트나 경로는 포함하지 않는다.
    /// </summary>
    public IEnumerable<string[]> GetCustomizationLogValues()
    {
        yield return new[] { "Keycap", _keycapMaterial != null ? _keycapMaterial.name : "" };
        yield return new[] { "Case", _caseMaterial != null ? _caseMaterial.name : "" };
        yield return new[] { "Switch", _switchType.ToString() };
        yield return new[] { "Plate", _plateMaterial != null ? _plateMaterial.name : "" };
        yield return new[] { "PlateEffect", _plateEffect.ToString() };
        yield return new[] { "LED", _ledPattern.ToString() };
        yield return new[] { "LEDColor", ColorUtility.ToHtmlStringRGBA(_ledColor) };
        yield return new[] { "LEDEnabled", _ledEnabled ? "true" : "false" };
        if (_imageCustomization != null)
            foreach (string[] value in _imageCustomization.GetLogValues()) yield return value;
    }

    void OnEnable()
    {
        CacheKeycaps();
        ApplySelectedCaseMaterial();
        ApplySelectedKeycapMaterial();
        ApplySelectedPlateSettings();
        if (_switches.Length > 0) ApplySwitchType(_switchType, false);
        if (_plate != null) SetPlateType(_plateType);
        
        if (_ledRenderer != null)
        {
            ApplyLED();
        }
        if (Application.IsPlaying(gameObject))
        {
            AudioManager.Ready -= HandleAudioManagerReady;
            AudioManager.Ready += HandleAudioManagerReady;
            if (AudioManager.instance != null) HandleAudioManagerReady(AudioManager.instance);
        }
        if (Application.IsPlaying(gameObject))
        {
            DisableKeycaps();
            DisableSwitches();
            SetLEDEnabled(false);
        }
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= ApplyCaseEditorPreview;
        EditorApplication.delayCall += ApplyCaseEditorPreview;
#endif
    }

    void OnDisable()
    {
        AudioManager.Ready -= HandleAudioManagerReady;
        if (_plateAudioManager != null) _plateAudioManager.SuspendKeyboardEffects(this);
#if UNITY_EDITOR
        EditorApplication.delayCall -= ApplyCaseEditorPreview;
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector 변경 처리가 끝난 뒤 Case와 키캡 선택값 및 렌더러 목록으로 외관을 갱신한다.
    /// 반환값 없이 프리팹 변경을 기록하고 에디터 화면의 재렌더링을 요청한다.
    /// </summary>
    public void ApplyCaseEditorPreview()
    {
        if (this == null)
        {
            return;
        }

        ApplyLED();

        ApplySelectedCaseMaterial();
        ApplySelectedKeycapMaterial();
        ApplySelectedPlateSettings();
        if (_switches.Length > 0) ApplySwitchType(_switchType, false);
        if (_plate != null) SetPlateType(_plateType);
        
        if (!Application.IsPlaying(gameObject))
        {
            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this)) PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            foreach (MeshRenderer renderer in _caseRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }

            foreach (MeshRenderer renderer in _keycapRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }

            foreach (KeyboardSwitch keyboardSwitch in _switches)
            {
                foreach (MeshRenderer renderer in keyboardSwitch.GetComponentsInChildren<MeshRenderer>(true))
                {
                    EditorUtility.SetDirty(renderer);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }

            if (_plate != null)
            {
                MeshRenderer renderer = _plate.GetComponent<MeshRenderer>();
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
#endif

    #region 저장 및 불러오기

    /// <summary>
    /// catalog와 materialIds로 현재 렌더러 재질을 식별하고 LED 및 파츠 선택 상태를 수집한다.
    /// 개별 키캡 변경을 포함하는 저장 데이터를 반환하며 등록되지 않은 재질은 예외로 알린다.
    /// </summary>
    public KeyboardCustomizationState CaptureState(Material[] catalog, string[] materialIds)
    {
        ApplySelectedPlateSettings();
        if (catalog.Length != materialIds.Length) throw new InvalidOperationException("Material registry is invalid.");
        MeshRenderer[] renderers = GetCustomizationRenderers();
        var paths = new string[renderers.Length];
        var counts = new int[renderers.Length];
        var indices = new List<int>();
        for (int index = 0; index < renderers.Length; index++)
        {
            MeshRenderer renderer = renderers[index];
            if (renderer == null) throw new InvalidOperationException("A customization renderer is missing.");
            paths[index] = GetRendererPath(renderer.transform);
            Material[] materials = _imageCustomization != null ? _imageCustomization.GetMaterialsForStorage(renderer) : renderer.sharedMaterials;
            counts[index] = materials.Length;
            foreach (Material material in materials) indices.Add(GetMaterialIndex(material, catalog));
        }

        var switchTypes = new SwitchType[_switches.Length];
        for (int index = 0; index < _switches.Length; index++)
        {
            switchTypes[index] = _switches[index].CurrentData != null ? _switches[index].CurrentData.Type : _switchType;
        }

        int plateMaterialIndex = GetMaterialIndex(_plateMaterial, catalog);
        return new KeyboardCustomizationState(_ledEnabled, _ledColor, _ledIntensity,
            new[] { GetMaterialIndex(_caseMaterial, catalog), GetMaterialIndex(_keycapMaterial, catalog), _caseMaterialIndex, _keycapMaterialIndex },
            (string[])materialIds.Clone(), paths, counts, indices.ToArray(), switchTypes,
            _plateType, _ledPattern, _ledSpeed,
            GetImageCustomization().CaptureState(),
            plateMaterialIndex >= 0 ? materialIds[plateMaterialIndex] : null, _plateEffect);
    }

    /// <summary>
    /// state와 현재 Material 카탈로그를 검증한 뒤 LED, 실제 재질, 축 및 Plate 선택을 복원한다.
    /// 반환값 없이 현재 인스턴스만 변경하며 호환되지 않는 데이터는 적용 전에 예외로 알린다.
    /// </summary>
    public void ApplyState(KeyboardCustomizationState state, Material[] catalog, string[] materialIds)
    {
        if (state == null || (state.Version < 1 || state.Version > 4) || state.Selection == null || state.Selection.Length != 4 ||
            state.CatalogIds == null || state.RendererPaths == null || state.SlotCounts == null ||
            state.MaterialIndices == null || state.SwitchTypes == null || catalog.Length != materialIds.Length)
            throw new InvalidOperationException("Save data is invalid or unsupported.");

        var remap = new int[state.CatalogIds.Length];
        for (int index = 0; index < remap.Length; index++)
        {
            remap[index] = Array.IndexOf(materialIds, state.CatalogIds[index]);
            if (remap[index] < 0 || catalog[remap[index]] == null) throw new InvalidOperationException("A saved material is unavailable.");
        }

        MeshRenderer[] renderers = GetCustomizationRenderers();
        if (state.RendererPaths.Length != renderers.Length || state.SlotCounts.Length != renderers.Length ||
            state.SwitchTypes.Length != _switches.Length || state.LedIntensity < 0f ||
            float.IsNaN(state.LedIntensity) || float.IsInfinity(state.LedIntensity) ||
            !Enum.IsDefined(typeof(LEDPattern), state.LedPattern) || state.LedSpeed < 0f ||
            float.IsNaN(state.LedSpeed) || float.IsInfinity(state.LedSpeed))
            throw new InvalidOperationException("The saved keyboard layout or settings are incompatible.");

        int totalSlots = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index] == null || state.RendererPaths[index] != GetRendererPath(renderers[index].transform) ||
                state.SlotCounts[index] != renderers[index].sharedMaterials.Length)
                throw new InvalidOperationException("The saved renderer layout is incompatible.");
            totalSlots += state.SlotCounts[index];
        }

        if (totalSlots != state.MaterialIndices.Length) throw new InvalidOperationException("Material slot data is incomplete.");
        foreach (int index in state.MaterialIndices) ValidateSavedMaterialIndex(index, remap.Length);
        ValidateSavedMaterialIndex(state.Selection[0], remap.Length);
        ValidateSavedMaterialIndex(state.Selection[1], remap.Length);
        for (int index = 0; index < _switches.Length; index++)
        {
            if (!_switches[index].HasSwitchType(state.SwitchTypes[index])) throw new InvalidOperationException("A saved switch type is unavailable.");
        }

        Material plateMaterial = null;
        PlateEffect plateEffect;
        if (state.Version >= 4)
        {
            plateEffect = state.PlateEffect;
            if (!Enum.IsDefined(typeof(PlateEffect), plateEffect)) throw new InvalidOperationException("The saved Plate effect is invalid.");
            if (_plate != null)
            {
                int materialIndex = Array.IndexOf(materialIds, state.PlateMaterialId);
                if (string.IsNullOrEmpty(state.PlateMaterialId) || materialIndex < 0 || catalog[materialIndex] == null)
                    throw new InvalidOperationException("The saved Plate material is unavailable.");
                plateMaterial = catalog[materialIndex];
            }
        }
        else
        {
            plateEffect = GetLegacyPlateEffect(state.PlateType);
            if (_plate != null && !_plate.TryGetMaterial(state.PlateType, out plateMaterial))
                throw new InvalidOperationException("The saved Plate type is unavailable.");
        }

        KeyboardImageCustomization images = GetImageCustomization();
        Texture2D[] preparedImages = images.PrepareState(state.ImageState);
        images.ClearKeycapImage();
        images.ClearCaseImage();

        for (int index = 0; index < _switches.Length; index++) _switches[index].ChangeSwitch(state.SwitchTypes[index]);
        if (_switches.Length > 0)
        {
            _switchType = state.SwitchTypes[0];
            GetComponent<KeyboardInputController>().SetSwitchSound(_switchType);
        }
        if (_plate != null) SetPlateMaterial(plateMaterial);
        SetPlateEffect(plateEffect);
        _caseMaterial = state.Selection[0] < 0 ? null : catalog[remap[state.Selection[0]]];
        _keycapMaterial = state.Selection[1] < 0 ? null : catalog[remap[state.Selection[1]]];
        _caseMaterialIndex = Mathf.Max(0, Array.IndexOf(_materials, _caseMaterial));
        _keycapMaterialIndex = Mathf.Max(0, Array.IndexOf(_materials, _keycapMaterial));
        int slot = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            var materials = new Material[state.SlotCounts[index]];
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                int savedIndex = state.MaterialIndices[slot++];
                materials[materialIndex] = savedIndex < 0 ? null : catalog[remap[savedIndex]];
            }

            renderers[index].sharedMaterials = materials;
            KeyboardToonSurface.ApplyBounds(renderers[index]);
        }

        RememberKeycapMaterialOverrides();
        images.RestoreState(state.ImageState, preparedImages);

        _ledEnabled = state.LedEnabled;
        _ledColor = state.LedColor;
        _ledIntensity = Mathf.Clamp(state.LedIntensity, 0f, MAX_LED_INTENSITY);
        _ledPattern = state.LedPattern;
        _ledSpeed = Mathf.Clamp(state.LedSpeed, 0f, MAX_LED_SPEED);
        if (_ledRenderer != null) ApplyLED();
    }

    /// <summary>
    /// material을 catalog 참조 배열에서 찾아 저장용 인덱스를 반환한다.
    /// null은 -1로 표시하며 등록되지 않은 재질은 예외로 알린다.
    /// </summary>
    private int GetMaterialIndex(Material material, Material[] catalog)
    {
        if (material == null) return -1;
        int index = Array.IndexOf(catalog, material);
        if (index < 0) throw new InvalidOperationException($"Material is not registered: {material.name}");
        return index;
    }

    /// <summary>
    /// 저장된 index와 카탈로그 count로 Material 참조 범위를 검사한다.
    /// 반환값 없이 잘못된 인덱스에 대해 예외를 발생시킨다.
    /// </summary>
    private void ValidateSavedMaterialIndex(int index, int count)
    {
        if (index < -1 || index >= count) throw new InvalidOperationException("A saved material index is invalid.");
    }

    /// <summary>
    /// Case 및 키캡 렌더러 참조를 현재 배열 순서로 합쳐 반환한다.
    /// 입력 없이 저장 대상 렌더러 목록을 구성한다.
    /// </summary>
    private MeshRenderer[] GetCustomizationRenderers()
    {
        var renderers = new List<MeshRenderer>(_caseRenderers);
        renderers.AddRange(_keycapRenderers);
        return renderers.ToArray();
    }

    /// <summary>
    /// target의 부모 이름과 형제 인덱스로 키보드 루트 기준 식별 경로를 만든다.
    /// 중복 이름의 Mesh도 구별할 수 있는 문자열을 반환한다.
    /// </summary>
    private string GetRendererPath(Transform target)
    {
        string path = "";
        while (target != transform)
        {
            if (target == null) throw new InvalidOperationException("A renderer belongs to another keyboard.");
            path = target.GetSiblingIndex() + ":" + target.name + "/" + path;
            target = target.parent;
        }

        return path;
    }

    #endregion

    #region 사진

    /// <summary>
    /// image를 키캡 전체가 하나로 이어지는 사진 캔버스로 적용한다.
    /// 기존 개별 사진을 덮어쓰고 성공 여부를 반환한다.
    /// </summary>
    public bool SetKeycapImage(Texture2D image)
    {
        bool applied = GetImageCustomization().SetKeycapImage(image);
        if (applied) EnableKeycaps();
        return applied;
    }

    /// <summary>
    /// keycapIndex에 해당하는 키캡에 image 한 장을 개별 적용한다.
    /// 전체 사진보다 우선하는 개별 배치를 저장하고 성공 여부를 반환한다.
    /// </summary>
    public bool SetKeycapImage(int keycapIndex, Texture2D image)
    {
        bool applied = GetImageCustomization().SetKeycapImage(image, keycapIndex);
        if (applied) EnableKeycaps(keycapIndex);
        return applied;
    }

    /// <summary>
    /// image를 Case 전체에 이어지는 Base Map 사진으로 적용한다.
    /// 다른 키보드의 Material 애셋은 유지하고 성공 여부를 반환한다.
    /// </summary>
    public bool SetCaseImage(Texture2D image)
    {
        return GetImageCustomization().SetCaseImage(image);
    }

    /// <summary>
    /// 이미지 편집 프레임에 사용할 전체 키캡, 개별 키캡 또는 케이스 경계 크기를 반환한다.
    /// caseImage와 keycapIndex를 사용하며 키보드 로컬 XZ 크기를 반환한다.
    /// </summary>
    public Vector2 GetPhotoCanvasSize(bool caseImage, int keycapIndex = -1)
    {
        return GetImageCustomization().GetCanvasSize(caseImage, keycapIndex);
    }

    /// <summary>
    /// 이미지 편집 중 전체 키캡 또는 지정 키캡에 임시 사진 배치를 표시한다.
    /// image와 배치값을 사용하며 원래 저장 상태는 변경하지 않는다.
    /// </summary>
    public bool PreviewKeycapImage(Texture2D image, int keycapIndex, Vector2 offset, float zoom)
    {
        return GetImageCustomization().PreviewKeycapImage(image, keycapIndex, offset, zoom);
    }

    /// <summary>
    /// 이미지 편집 중 케이스 전체에 임시 사진 배치를 표시한다.
    /// image와 배치값을 사용하며 원래 저장 상태는 변경하지 않는다.
    /// </summary>
    public bool PreviewCaseImage(Texture2D image, Vector2 offset, float zoom)
    {
        return GetImageCustomization().PreviewCaseImage(image, offset, zoom);
    }

    /// <summary>
    /// 편집 화면을 닫을 때 임시 사진을 지우고 기존 적용 상태를 복구한다.
    /// 반환값 없이 이미지 미리보기 오버레이를 정리한다.
    /// </summary>
    public void ClearPhotoImagePreview()
    {
        if (_imageCustomization != null) _imageCustomization.ClearImagePreview();
    }

    /// <summary>
    /// path의 PNG 또는 JPEG 파일을 전체 키캡이나 keycapIndex 한 곳에 적용한다.
    /// 성공 여부를 반환하고 음수 인덱스는 전체 캔버스를 의미한다.
    /// </summary>
    public bool LoadKeycapImage(string path, int keycapIndex = -1)
    {
        bool applied = GetImageCustomization().LoadImage(path, false, keycapIndex);
        if (applied) EnableKeycaps(keycapIndex);
        return applied;
    }

    /// <summary>
    /// PNG 또는 JPEG 이미지를 지정 키캡에 배치값과 함께 적용한다.
    /// path, keycapIndex, 오프셋과 배율을 사용하며 성공 시 해당 키캡을 활성화한다.
    /// </summary>
    public bool LoadKeycapImage(string path, int keycapIndex, Vector2 offset, float zoom)
    {
        bool applied = GetImageCustomization().LoadImage(path, false, keycapIndex, offset, zoom);
        if (applied) EnableKeycaps(keycapIndex);
        return applied;
    }

    /// <summary>
    /// path의 PNG 또는 JPEG 파일을 읽어 Case 사진으로 적용한다.
    /// 성공 여부를 반환하며 잘못된 파일은 기존 외관을 유지한다.
    /// </summary>
    public bool LoadCaseImage(string path)
    {
        return GetImageCustomization().LoadImage(path, true);
    }

    /// <summary>
    /// PNG 또는 JPEG 이미지를 케이스 전체에 배치값과 함께 적용한다.
    /// path, 오프셋과 배율을 사용하며 성공 여부를 반환한다.
    /// </summary>
    public bool LoadCaseImage(string path, Vector2 offset, float zoom)
    {
        return GetImageCustomization().LoadImage(path, true, -1, offset, zoom);
    }

    /// <summary>
    /// keycapIndex의 개별 사진 또는 음수 입력 시 모든 키캡 사진을 해제한다.
    /// 반환값 없이 사진 아래의 선택 Material과 원본 UV를 다시 표시한다.
    /// </summary>
    public void ClearKeycapImage(int keycapIndex = -1)
    {
        if (_imageCustomization != null) _imageCustomization.ClearKeycapImage(keycapIndex);
    }

    /// <summary>
    /// 입력 없이 Case의 사진 효과를 해제한다.
    /// 반환값 없이 현재 선택한 Case Material과 원본 UV를 표시한다.
    /// </summary>
    public void ClearCaseImage()
    {
        if (_imageCustomization != null) _imageCustomization.ClearCaseImage();
    }

    /// <summary>
    /// 현재 씬 인스턴스의 사진 컴포넌트를 준비하고 Case와 키캡 참조를 연결한다.
    /// 컴포넌트를 반환하며 공통 프리팹이나 Prefab Mode에는 사진을 기록하지 않는다.
    /// </summary>
    private KeyboardImageCustomization GetImageCustomization()
    {
#if UNITY_EDITOR
        if (EditorUtility.IsPersistent(this) || UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
            throw new InvalidOperationException("Apply photos to a scene keyboard instance, outside Prefab Mode.");
#endif
        if (_imageCustomization == null)
        {
            _imageCustomization = GetComponent<KeyboardImageCustomization>();
            if (_imageCustomization == null) _imageCustomization = gameObject.AddComponent<KeyboardImageCustomization>();
        }
        _imageCustomization.Initialize(_caseRenderers, _keycapRenderers, _photoMaterialTemplate, HasSwitchForKeycap);
        return _imageCustomization;
    }

    #endregion

    #region 플레이트

    /// <summary>
    /// manager와 selectionVersion을 현재 키보드의 오디오 선택 정보로 보관한다.
    /// 재활성화 시 최신 선택인지 확인할 수 있도록 참조와 번호를 변경한다.
    /// </summary>
    public void RememberPlateAudioSelection(AudioManager manager, int selectionVersion)
    {
        _plateAudioManager = manager;
        _plateAudioSelection = selectionVersion;
    }

    /// <summary>
    /// manager가 준비되면 현재 키보드의 선택 번호로 효과 연결을 복원한다.
    /// 런타임의 활성 인스턴스만 처리하여 편집 모드 미리보기의 FMOD 제어를 막는다.
    /// </summary>
    private void HandleAudioManagerReady(AudioManager manager)
    {
        if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled) return;
        if (_plateAudioManager == null || _plateAudioManager == manager)
            manager.ResumeKeyboardEffects(this, _plateAudioSelection);
    }

    /// <summary>
    /// 기존 UI의 type 입력을 대응하는 재질 선택으로 변환한다.
    /// 성공 여부와 호환용 타입을 갱신하며 독립적으로 선택한 효과는 유지한다.
    /// </summary>
    public bool SetPlateType(PlateType type)
    {
        if (_plate == null || !_plate.TryGetMaterial(type, out Material material) || !SetPlateMaterial(material)) return false;
        _plateType = type;
        return true;
    }

    /// <summary>
    /// material을 현재 보강판 재질로 선택하고 Renderer와 공유 목록 인덱스를 동기화한다.
    /// 성공 여부를 반환하며 효과는 유지하고 실제 선택 변경 시 재질 이벤트를 발생시킨다.
    /// </summary>
    public bool SetPlateMaterial(Material material)
    {
        InitializePlateSettings();
        if (_plate == null || !_plate.SetMaterial(material)) return false;
        int index = Array.IndexOf(_materials, material);
        bool changed = !_plateMaterialApplied || _appliedPlateMaterial != material || _appliedPlateMaterialIndex != index;
        _plateMaterial = material;
        _plateMaterialIndex = index;
        _appliedPlateMaterial = material;
        _appliedPlateMaterialIndex = index;
        _plateMaterialApplied = true;
        foreach (PlateData data in _plate.AvailablePlates)
        {
            if (data != null && data.Material == material) { _plateType = data.Type; break; }
        }
        if (changed) PlateMaterialChanged?.Invoke(material);
        return true;
    }

    /// <summary>
    /// index에 해당하는 공유 재질을 기존 재질 변경 경로로 적용한다.
    /// 선택 성공 여부를 반환하며 범위 밖 인덱스는 현재 설정을 변경하지 않는다.
    /// </summary>
    public bool SetPlateMaterialIndex(int index)
    {
        InitializePlateSettings();
        return index >= 0 && index < _materials.Length && SetPlateMaterial(_materials[index]);
    }

    /// <summary>
    /// effect를 현재 보강판 효과로 선택하고 변경 이벤트를 발생시킨다.
    /// 성공 여부를 반환하며 재질과 재질 인덱스는 변경하지 않는다.
    /// </summary>
    public bool SetPlateEffect(PlateEffect effect)
    {
        InitializePlateSettings();
        if (!Enum.IsDefined(typeof(PlateEffect), effect)) return false;
        bool changed = !_plateEffectApplied || _appliedPlateEffect != effect;
        _plateEffect = effect;
        _appliedPlateEffect = effect;
        _plateEffectApplied = true;
        if (changed) PlateEffectChanged?.Invoke(effect);
        return true;
    }

    /// <summary>
    /// 이전 보강판 타입을 독립 재질과 효과의 초기값으로 한 번만 변환한다.
    /// 기존 PlateData의 실제 재질과 과거 오디오 매핑을 보존하고 변환 완료 상태를 기록한다.
    /// </summary>
    private void InitializePlateSettings()
    {
        if (_plateSettingsSeparated) return;
        if (_plateMaterial == null && _plate != null)
        {
            if (!_plate.TryGetMaterial(_plateType, out _plateMaterial))
                _plateMaterial = _plate.PlateRenderer.sharedMaterial;
        }
        _plateMaterialIndex = Array.IndexOf(_materials, _plateMaterial);
        _plateEffect = GetLegacyPlateEffect(_plateType);
        _plateSettingsSeparated = true;
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject))
        {
            EditorUtility.SetDirty(this);
            if (PrefabUtility.IsPartOfPrefabInstance(this)) PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }

    /// <summary>
    /// legacyType에 대응하던 과거 오디오 효과를 반환한다.
    /// 이전 데이터 변환에만 사용하며 현재 재질 선택에는 효과를 연결하지 않는다.
    /// </summary>
    private static PlateEffect GetLegacyPlateEffect(PlateType legacyType)
    {
        return legacyType switch
        {
            PlateType.UnderWater => PlateEffect.Reverb,
            PlateType.Golden => PlateEffect.Delay,
            PlateType.RainBow or PlateType.Steel => PlateEffect.Pitch,
            _ => PlateEffect.None
        };
    }

    /// <summary>
    /// Inspector 또는 초기화에서 선택한 재질과 효과를 각각의 공개 변경 경로로 적용한다.
    /// 변경된 인덱스를 우선 반영하며 재질 직접 지정과 효과 변경도 동일한 이벤트로 알린다.
    /// </summary>
    private void ApplySelectedPlateSettings()
    {
        InitializePlateSettings();
        if (_plate != null)
        {
            bool materialApplied;
            if ((_plateMaterialApplied || _plateMaterial == null) &&
                _plateMaterialIndex != _appliedPlateMaterialIndex && _plateMaterialIndex >= 0 && _plateMaterialIndex < _materials.Length)
                materialApplied = SetPlateMaterialIndex(_plateMaterialIndex);
            else
                materialApplied = SetPlateMaterial(_plateMaterial);
            if (!materialApplied)
                SetPlateMaterial(_appliedPlateMaterial != null ? _appliedPlateMaterial : _plate.PlateRenderer.sharedMaterial);
        }
        if (!SetPlateEffect(_plateEffect)) SetPlateEffect(_plateEffectApplied ? _appliedPlateEffect : PlateEffect.None);
    }

    #endregion

    #region 축

    /// <summary>
    /// 입력 없이 등록된 모든 축의 GameObject를 비활성화한다.
    /// 반환값 없이 축만 숨기며 키캡과 선택한 축 데이터는 유지한다.
    /// </summary>
    public void DisableSwitches()
    {
        foreach (KeyboardSwitch keyboardSwitch in _switches)
        {
            if (keyboardSwitch != null) keyboardSwitch.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// type 입력을 모든 축 컴포넌트에 전달해 외형과 사운드 선택을 일괄 변경한다.
    /// 모든 축에 데이터가 있을 때만 적용하며 성공 여부와 현재 선택 타입을 갱신한다.
    /// </summary>
    public bool SetSwitchType(SwitchType type)
    {
        return ApplySwitchType(type, true);
    }

    /// <summary>
    /// type의 축 외형과 소리를 적용하고 activate가 true이면 축 GameObject도 활성화한다.
    /// 적용 성공 여부를 반환하며 초기화와 에디터 미리보기에서는 장착 상태를 유지한다.
    /// </summary>
    private bool ApplySwitchType(SwitchType type, bool activate)
    {
        if (_switches.Length == 0) return false;
        foreach (KeyboardSwitch keyboardSwitch in _switches)
        {
            if (!keyboardSwitch.HasSwitchType(type))
            {
                Debug.LogWarning($"{keyboardSwitch.name}: {type} 축 데이터가 없어 변경하지 않았습니다.", this);
                return false;
            }
        }

        if (activate && Application.IsPlaying(gameObject)) PreparePartInstallation();
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _switches.Length; index++)
        {
            KeyboardSwitch keyboardSwitch = _switches[index];
            if (activate && Application.IsPlaying(gameObject))
            {
                Transform keycap = index < _keycaps.Length ? _keycaps[index] : null;
                _partInstallation.ReplaceSwitch(keyboardSwitch.transform, keycap, () => keyboardSwitch.ChangeSwitch(type));
            }
            else
            {
                keyboardSwitch.ChangeSwitch(type);
                if (activate) keyboardSwitch.gameObject.SetActive(true);
            }
        }

        _switchType = type;
        GetComponent<KeyboardInputController>().SetSwitchSound(type);
        return true;
    }

    #endregion

    #region 키캡

    /// <summary>
    /// part의 설치 연출 컴포넌트를 준비하고 위에서 내려오는 움직임을 재생한다.
    /// 편집 모드에서는 활성화만 수행하고 런타임에서는 기존 파츠도 다시 설치한다.
    /// </summary>
    private void PlayPartInstallation(Transform part)
    {
        if (!Application.IsPlaying(gameObject))
        {
            part.gameObject.SetActive(true);
            return;
        }
        PreparePartInstallation();
        _partInstallation.Play(part);
    }

    /// <summary>
    /// 입력 없이 같은 키보드의 설치 연출 컴포넌트를 조회하거나 추가한다.
    /// 반환값 없이 _partInstallation 참조를 준비한다.
    /// </summary>
    private void PreparePartInstallation()
    {
        if (_partInstallation == null)
        {
            _partInstallation = GetComponent<KeyboardPartInstallation>();
            if (_partInstallation == null) _partInstallation = gameObject.AddComponent<KeyboardPartInstallation>();
        }
    }

    /// <summary>
    /// keycapIndex와 같은 인덱스에 축이 설치되어 있는지 확인한다.
    /// 유효한 축 GameObject의 activeSelf가 true일 때만 true를 반환한다.
    /// </summary>
    public bool HasSwitchForKeycap(int keycapIndex)
    {
        return keycapIndex >= 0 && keycapIndex < _switches.Length &&
            _switches[keycapIndex] != null && _switches[keycapIndex].gameObject.activeSelf;
    }

    /// <summary>
    /// keycapIndex 키캡을 활성화하며 음수 입력은 전체 키캡을 대상으로 한다.
    /// 반환값 없이 꺼진 루트만 켜고 축과 다른 키의 활성 상태는 유지한다.
    /// </summary>
    private void EnableKeycaps(int keycapIndex = -1)
    {
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (keycapIndex >= 0 && index != keycapIndex) continue;
            if (!HasSwitchForKeycap(index)) continue;
            Transform keycap = _keycaps[index];
            if (keycap != null) PlayPartInstallation(keycap);
        }
    }

    /// <summary>
    /// 입력 없이 캐시된 모든 키캡 루트 GameObject를 비활성화한다.
    /// 반환값 없이 키캡만 숨기며 축과 커스터마이징 설정은 유지한다.
    /// </summary>
    public void DisableKeycaps()
    {
        if (_keycaps == null) CacheKeycaps();
        foreach (Transform keycap in _keycaps)
        {
            if (keycap != null) keycap.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// keycapIndex의 키캡에만 material을 적용하며 각인과 다른 키캡은 유지한다.
    /// 키캡 순서는 렌더러 배열의 외피 순서이며 잘못된 입력 또는 파괴된 키캡은 false를 반환한다.
    /// </summary>
    public bool SetKeycapMaterial(int keycapIndex, Material material)
    {
        if (_keycaps == null) CacheKeycaps();
        if (material == null || keycapIndex < 0 || keycapIndex >= _keycaps.Length || !HasSwitchForKeycap(keycapIndex))
        {
            return false;
        }

        Transform keycap = _keycaps[keycapIndex];
        if (keycap == null) return false;

        ClearKeycapImage(keycapIndex);
        _keycapMaterialOverrides[keycap] = material;

        bool applied = false;
        foreach (MeshRenderer renderer in _keycapRenderers)
        {
            if (renderer != null && renderer.transform.IsChildOf(keycap))
            {
                renderer.sharedMaterial = material;
                KeyboardToonSurface.ApplyBounds(renderer);
                applied = true;
            }
        }

        if (applied) EnableKeycaps(keycapIndex);
        return applied;
    }

    /// <summary>
    /// 등록된 렌더러 배열의 PBT_SculptedShell을 사용해 키캡별 루트를 순서대로 저장한다.
    /// 입력과 반환값 없이 키캡 인덱스를 고정해 파괴 이후에도 다른 키의 인덱스를 유지한다.
    /// </summary>
    private void CacheKeycaps()
    {
        var keycaps = new List<Transform>();
        foreach (MeshRenderer renderer in _keycapRenderers)
        {
            if (renderer != null && renderer.name == "PBT_SculptedShell")
            {
                keycaps.Add(renderer.transform.parent);
            }
        }

        _keycaps = keycaps.ToArray();
    }

    /// <summary>
    /// material 입력을 전체 키캡의 공통 재질로 저장하고 연결된 키캡에 적용한다.
    /// 반환값 없이 키캡 외형 재질을 변경하며 null 입력은 무시한다.
    /// </summary>
    public void SetKeycapMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        _keycapMaterial = material;
        _keycapMaterialIndex = Array.IndexOf(_materials, material);
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (!HasSwitchForKeycap(index)) continue;
            SetKeycapMaterial(index, material);
        }
    }

    /// <summary>
    /// index 입력으로 공유 Material 배열 항목을 선택해 전체 키캡에 적용한다.
    /// 반환값 없이 선택 인덱스와 재질을 변경하며 범위 밖 입력은 무시한다.
    /// </summary>
    public void SetKeycapMaterialIndex(int index)
    {
        if (index < 0 || index >= _materials.Length)
        {
            return;
        }

        SetKeycapMaterial(_materials[index]);
    }

    /// <summary>
    /// 공유 Material 배열과 인덱스로 공통 재질을 선택해 전체 키캡을 갱신한다.
    /// 반환값 없이 유효한 항목을 반영하며 빈 배열에서는 직접 지정한 재질을 사용한다.
    /// </summary>
    private void ApplySelectedKeycapMaterial()
    {
        if (_materials.Length > 0
            && _keycapMaterialIndex >= 0
            && _keycapMaterialIndex < _materials.Length)
        {
            _keycapMaterial = _materials[_keycapMaterialIndex];
        }

        ApplyKeycapMaterial();
    }

    /// <summary>
    /// 선택한 공통 Material과 키캡 렌더러 목록으로 키캡 재질을 갱신한다.
    /// 반환값 없이 살아 있는 키캡만 변경하며 파괴된 키캡과 미선택 재질은 건너뛴다.
    /// </summary>
    private void ApplyKeycapMaterial()
    {
        if (_keycapMaterial == null)
        {
            return;
        }

        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            Transform keycap = _keycaps[index];
            if (keycap == null || !HasSwitchForKeycap(index)) continue;
            Material material = _keycapMaterialOverrides.TryGetValue(keycap, out Material selected) ? selected : _keycapMaterial;
            foreach (MeshRenderer renderer in _keycapRenderers)
                if (renderer != null && renderer.transform.IsChildOf(keycap) &&
                    (_imageCustomization == null || !_imageCustomization.HasImage(renderer)))
                {
                    renderer.sharedMaterial = material;
                    KeyboardToonSurface.ApplyBounds(renderer);
                }
        }
    }

    /// <summary>
    /// 복원된 각 키캡 외피 Material을 전체 기본 재질과 비교한다.
    /// 입력과 반환값 없이 개별 재질 차이를 보관해 이후 Inspector 갱신에서도 유지한다.
    /// </summary>
    private void RememberKeycapMaterialOverrides()
    {
        _keycapMaterialOverrides.Clear();
        foreach (MeshRenderer renderer in _keycapRenderers)
            if (renderer != null && renderer.name == "PBT_SculptedShell" && renderer.sharedMaterial != _keycapMaterial)
                _keycapMaterialOverrides[renderer.transform.parent] = renderer.sharedMaterial;
    }

    #endregion

    #region 케이스

    /// <summary>
    /// material 입력을 하우징 재질로 저장하고 연결된 케이스 파츠에 적용한다.
    /// 반환값 없이 하우징의 렌더러 재질을 변경하며 null 입력은 무시한다.
    /// </summary>
    public void SetCaseMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        _caseMaterial = material;
        ClearCaseImage();
        ApplyCaseMaterial();
    }

    /// <summary>
    /// index 입력으로 공유 Material 배열의 항목을 선택하고 케이스에 적용한다.
    /// 반환값 없이 선택 인덱스와 재질을 변경하며 범위 밖 입력은 무시한다.
    /// </summary>
    public void SetCaseMaterialIndex(int index)
    {
        if (index < 0 || index >= _materials.Length)
        {
            return;
        }

        _caseMaterialIndex = index;
        ClearCaseImage();
        ApplySelectedCaseMaterial();
    }

    /// <summary>
    /// 공유 Material 배열과 선택 인덱스로 현재 재질을 결정해 케이스에 적용한다.
    /// 반환값 없이 유효한 배열 항목을 반영하며 빈 배열에서는 직접 지정한 재질을 사용한다.
    /// </summary>
    private void ApplySelectedCaseMaterial()
    {
        if (_materials.Length > 0)
        {
            _caseMaterialIndex = Mathf.Clamp(_caseMaterialIndex, 0, _materials.Length - 1);
            _caseMaterial = _materials[_caseMaterialIndex];
        }

        ApplyCaseMaterial();
    }

    /// <summary>
    /// 선택된 하우징 Material과 렌더러 목록을 사용해 케이스 재질을 갱신한다.
    /// 반환값 없이 각 렌더러의 재질 참조를 교체하며 미선택 상태는 유지한다.
    /// </summary>
    private void ApplyCaseMaterial()
    {
        if (_caseMaterial == null)
        {
            return;
        }

        foreach (MeshRenderer renderer in _caseRenderers)
        {
            // Inspector에서 렌더러 배열을 편집하는 중에는 빈 항목이 생길 수 있다.
            if (renderer != null && (_imageCustomization == null || !_imageCustomization.HasImage(renderer)))
            {
                renderer.sharedMaterial = _caseMaterial;
                KeyboardToonSurface.ApplyBounds(renderer);
            }
        }
    }

    #endregion

    #region LED

    /// <summary>
    /// pattern 입력을 LED 패턴으로 선택하고 전용 레이어에 전달한다.
    /// 반환값 없이 Static, Rainbow 또는 Breathing 표시를 변경하며 잘못된 타입은 무시한다.
    /// </summary>
    public void SetLEDPattern(LEDPattern pattern)
    {
        if (!Enum.IsDefined(typeof(LEDPattern), pattern)) return;
        _ledPattern = pattern;
        ApplyLED();
    }

    /// <summary>
    /// speed 입력을 LED 애니메이션 속도로 저장하고 레이어에 전달한다.
    /// 반환값 없이 속도를 0~10으로 제한하고 유효하지 않은 실수는 무시한다.
    /// </summary>
    public void SetLEDSpeed(float speed)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed)) return;
        _ledSpeed = Mathf.Clamp(speed, 0f, MAX_LED_SPEED);
        ApplyLED();
    }

    /// <summary>
    /// intensity 입력을 LED 밝기로 저장하고 전용 레이어에 전달한다.
    /// 반환값 없이 밝기를 0~10으로 제한하고 유효하지 않은 실수는 무시한다.
    /// </summary>
    public void SetLEDIntensity(float intensity)
    {
        if (float.IsNaN(intensity) || float.IsInfinity(intensity)) return;
        _ledIntensity = Mathf.Clamp(intensity, 0f, MAX_LED_INTENSITY);
        ApplyLED();
    }

    /// <summary>
    /// enabled 입력으로 LED 전원을 저장하고 레이어의 투명도를 갱신한다.
    /// 반환값 없이 OFF 상태의 알파를 0으로 변경한다.
    /// </summary>
    public void SetLEDEnabled(bool enabled)
    {
        _ledEnabled = enabled;
        ApplyLED();
    }

    /// <summary>
    /// color 입력을 LED 색상으로 저장하고 현재 전원 상태에 맞게 반영한다.
    /// 반환값 없이 전용 레이어의 색상을 변경한다.
    /// </summary>
    public void SetLEDColor(Color color)
    {
        _ledColor = color;
        ApplyLED();
    }

    /// <summary>
    /// Inspector의 전원, 패턴, 색상, 밝기, 속도를 LED 컴포넌트에 전달한다.
    /// 반환값 없이 표시 설정을 변경하며 애니메이션 계산은 레이어에 위임한다.
    /// </summary>
    private void ApplyLED()
    {
        _ledIntensity = Mathf.Clamp(_ledIntensity, 0f, MAX_LED_INTENSITY);
        _ledSpeed = Mathf.Clamp(_ledSpeed, 0f, MAX_LED_SPEED);
        if (_ledLayer == null && _ledRenderer != null) _ledLayer = _ledRenderer.GetComponent<KeyboardLED>();
        // 기존 프리팹을 새 컴포넌트로 연결하는 동안에는 레이어가 아직 없을 수 있다.
        if (_ledLayer != null) _ledLayer.ApplySettings(_ledEnabled, _ledPattern, _ledColor, _ledIntensity, _ledSpeed);
    }

    #endregion
}

