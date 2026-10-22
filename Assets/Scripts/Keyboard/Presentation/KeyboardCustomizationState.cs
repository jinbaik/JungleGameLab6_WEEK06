using System;

using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public sealed class KeyboardCustomizationState
{
    [SerializeField] private int _version = 6;
    [SerializeField] private bool _ledEnabled;
    [SerializeField] private Color _ledColor;
    [SerializeField] private float _ledIntensity;
    [SerializeField] private LEDPattern _ledPattern;
    [SerializeField] private float _ledSpeed = 1f;
    [SerializeField] private int[] _selection;
    [SerializeField] private string[] _catalogIds;
    [SerializeField] private string[] _rendererPaths;
    [SerializeField] private int[] _slotCounts;
    [SerializeField] private int[] _materialIndices;
    [SerializeField] private SwitchType[] _switchTypes;
    [SerializeField] private PlateType _plateType;
    [SerializeField] private KeyboardImageState _imageState;
    [SerializeField] private string _plateMaterialId;
    [SerializeField] private PlateEffect _plateEffect;
    [SerializeField] private bool[] _keycapInstalled;
    [SerializeField] private bool[] _switchInstalled;

    [SerializeField] private Key[] _keycapKeys;
    [SerializeField] private string[] _keycapDefinitionIds;

    // 개발 중 사용한 SO 전용 버전 5도 보유 필드로 구분해 읽는다.
    public bool HasKeycapDefinitions => _version >= 6 || (_version == 5 && (_keycapKeys != null || _keycapDefinitionIds != null));
    public bool HasInstallationState => _version >= 6 || (_version == 5 && (_keycapInstalled != null || _switchInstalled != null));
    public Key[] KeycapKeys => _keycapKeys;
    public string[] KeycapDefinitionIds => _keycapDefinitionIds;
    public int Version => _version;
    public bool LedEnabled => _ledEnabled;
    public Color LedColor => _ledColor;
    public float LedIntensity => _ledIntensity;
    public LEDPattern LedPattern => _version < 2 ? LEDPattern.Static : _ledPattern;
    public float LedSpeed => _version < 2 ? 1f : _ledSpeed;
    public int[] Selection => _selection;
    public string[] CatalogIds => _catalogIds;
    public string[] RendererPaths => _rendererPaths;
    public int[] SlotCounts => _slotCounts;
    public int[] MaterialIndices => _materialIndices;
    public SwitchType[] SwitchTypes => _switchTypes;
    public PlateType PlateType => _plateType;
    public KeyboardImageState ImageState => _version < 3 ? null : _imageState;
    public string PlateMaterialId => _plateMaterialId;
    public PlateEffect PlateEffect => _plateEffect;
    public bool[] KeycapInstalled => _keycapInstalled;
    public bool[] SwitchInstalled => _switchInstalled;

    /// <summary>
    /// LED, 재질, 사진, 보강판, 키캡 SO ID와 부품 설치 여부를 저장용 상태로 구성한다.
    /// 입력받은 배열과 값을 보관하며 Unity 오브젝트 참조는 저장하지 않는다.
    /// </summary>
    public KeyboardCustomizationState(bool ledEnabled, Color ledColor, float ledIntensity, int[] selection,
        string[] catalogIds, string[] rendererPaths, int[] slotCounts, int[] materialIndices,
        SwitchType[] switchTypes, PlateType plateType, LEDPattern ledPattern = LEDPattern.Static, float ledSpeed = 1f,
        KeyboardImageState imageState = null, string plateMaterialId = null, PlateEffect plateEffect = PlateEffect.None,
        bool[] keycapInstalled = null, bool[] switchInstalled = null,
        Key[] keycapKeys = null, string[] keycapDefinitionIds = null)
    {
        _version = keycapKeys != null || keycapDefinitionIds != null ? 6 : 5;
        _keycapKeys = keycapKeys;
        _keycapDefinitionIds = keycapDefinitionIds;
        _ledEnabled = ledEnabled;
        _ledColor = ledColor;
        _ledIntensity = ledIntensity;
        _ledPattern = ledPattern;
        _ledSpeed = ledSpeed;
        _selection = selection;
        _catalogIds = catalogIds;
        _rendererPaths = rendererPaths;
        _slotCounts = slotCounts;
        _materialIndices = materialIndices;
        _switchTypes = switchTypes;
        _plateType = plateType;
        _imageState = imageState;
        _plateMaterialId = plateMaterialId;
        _plateEffect = plateEffect;
        _keycapInstalled = keycapInstalled;
        _switchInstalled = switchInstalled;
    }
}
