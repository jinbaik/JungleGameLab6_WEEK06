using System;

using UnityEngine;

[Serializable]
public sealed class KeyboardCustomizationState
{
    [SerializeField] private int _version = 3;
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

    /// <summary>
    /// LED 설정, 선택값, 카탈로그 ID, 렌더러별 재질과 축 및 Plate 타입을 저장용 상태로 구성한다.
    /// 입력받은 배열과 값을 보관하며 Unity 오브젝트 참조는 저장하지 않는다.
    /// </summary>
    public KeyboardCustomizationState(bool ledEnabled, Color ledColor, float ledIntensity, int[] selection,
        string[] catalogIds, string[] rendererPaths, int[] slotCounts, int[] materialIndices,
        SwitchType[] switchTypes, PlateType plateType, LEDPattern ledPattern = LEDPattern.Static, float ledSpeed = 1f,
        KeyboardImageState imageState = null)
    {
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
    }
}
