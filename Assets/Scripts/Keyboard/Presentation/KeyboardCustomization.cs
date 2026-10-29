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
public sealed class KeyboardCustomization : MonoBehaviour, IKeyboardLEDSettings, IKeyboardPhotoTarget
{
    private const float MAX_LED_INTENSITY = 10f;
    private const float MAX_LED_SPEED = 10f;
    private const float PCB_SWITCH_CLEARANCE = -0.15f;

    private const float PCB_STABILIZER_HEIGHT = 0.24f;

    private static readonly IReadOnlyList<PlateEffect> _plateEffects = Array.AsReadOnly((PlateEffect[])Enum.GetValues(typeof(PlateEffect)));

    [Header("Assembly PCB Slots")]
    private Transform _pcbSlotRoot;
    private Vector3[] _pcbSwitchPositions;
    private Quaternion[] _pcbSwitchRotations;
    private Vector3[] _pcbKeycapPositions;
    private Quaternion[] _pcbKeycapRotations;
    private Bounds[] _pcbSwitchBounds;
    private Bounds[] _pcbKeycapBounds;

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
    public Material CaseMaterial => _caseMaterial;

    [Header("Keycaps")]
    [SerializeField] private MeshRenderer[] _keycapRenderers = new MeshRenderer[0];
    [SerializeField] private Material _keycapMaterial;
    [SerializeField, Min(0)] private int _keycapMaterialIndex;
    private Transform[] _keycaps;
    private MeshRenderer[] _keycapSlotShells;
    private MeshRenderer[] _switchSlotShells;
    private KeyboardImageCustomization _imageCustomization;
    private readonly Dictionary<Transform, Material> _keycapMaterialOverrides = new Dictionary<Transform, Material>();
    private KeycapMaterialPreviewSnapshot _keycapMaterialPreviewSnapshot;
    private readonly Dictionary<Transform, KeycapModelAppearance> _keycapModels = new Dictionary<Transform, KeycapModelAppearance>();
    private readonly Dictionary<Transform, Game.Tournament.KeycapDefinition> _keycapPartDefinitions = new Dictionary<Transform, Game.Tournament.KeycapDefinition>();
    private readonly Dictionary<Transform, Game.Tournament.KeycapDefinition> _keycapModelDefinitions = new Dictionary<Transform, Game.Tournament.KeycapDefinition>();

    public event Action KeycapDefinitionChanged;

    public string ImageError => _imageCustomization != null ? _imageCustomization.LastError : "";
    public int PhotoKeycapCount => GetImageCustomization().KeycapCount;
    public int KeycapCount
    {
        get
        {
            if (_keycaps == null) CacheKeycaps();
            return _keycaps.Length;
        }
    }

    /// <summary>
    /// index 위치의 키캡 Transform을 반환한다. 키캡 캐시를 준비하며 설치 상태는 변경하지 않는다.
    /// </summary>
    public Transform GetKeycapSlotTransform(int index)
    {
        if (_keycaps == null) CacheKeycaps();
        return _keycaps[index];
    }

    /// <summary>
    /// index 슬롯에 연결된 입력 키의 안정된 이름을 반환한다.
    /// 키보드 입력 매핑을 사용하며 찾지 못하면 Keycap과 index를 결합한 ID를 반환한다.
    /// </summary>
    public string GetKeycapSlotId(int index)
    {
        if (_keycaps == null) CacheKeycaps();
        if (index < 0 || index >= _keycaps.Length) return "";
        KeyboardInputController input = GetComponent<KeyboardInputController>();
        if (input != null)
        {
            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None || !input.TryGetKeycapTransform(key, out Transform inputKeycap)) continue;
                if (_keycaps[index] == inputKeycap || _keycaps[index].IsChildOf(inputKeycap)
                    || inputKeycap.IsChildOf(_keycaps[index])) return key.ToString();
            }
        }
        return "Keycap:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ray가 통과하는 슬롯 중 가장 가까운 위치의 인덱스를 반환한다.
    /// switchSlot이 true이면 축 경계를, 아니면 키캡 경계를 검사하며 슬롯이 없으면 -1을 반환한다.
    /// </summary>
    public int FindKeycapSlot(Ray ray, bool switchSlot = false)
    {
        if (_keycaps == null) CacheKeycaps();
        KeyboardModeling.Assembly.KeyboardAssemblyState assembly = GetComponent<KeyboardModeling.Assembly.KeyboardAssemblyState>();
        if (assembly != null && switchSlot && !assembly.CanInstallSwitch(false, false)) return -1;
        float nearest = float.PositiveInfinity;
        int result = -1;
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (assembly != null && (switchSlot
                ? !assembly.CanInstallSwitch(HasSwitchForKeycap(index), IsKeycapInstalled(index))
                : !assembly.CanInstallKeycap(IsKeycapInstalled(index), HasSwitchForKeycap(index)))) continue;
            Bounds bounds = switchSlot ? GetSwitchSlotBounds(index) : GetKeycapSlotBounds(index);
            if (bounds.IntersectRay(ray, out float distance) && distance < nearest)
            {
                nearest = distance;
                result = index;
            }
        }
        return result;
    }

    /// <summary>
    /// index에 캐시한 외피 참조로 현재 월드 슬롯 경계를 반환한다.
    /// 전체 렌더러를 다시 검색하지 않으며 비활성 슬롯도 같은 좌표로 계산한다. 설치 상태는 변경하지 않는다.
    /// </summary>
    public Bounds GetKeycapSlotBounds(int index)
    {
        if (_keycaps == null) CacheKeycaps();
        if (_pcbSlotRoot != null) return TransformSlotBounds(_pcbKeycapBounds[index], _pcbSlotRoot);
        return GetRendererSlotBounds(_keycapSlotShells[index]);
    }

    /// <summary>
    /// index에 캐시한 축 하우징의 월드 경계를 반환한다.
    /// 비활성 축도 검사하고 스태빌라이저를 설치 범위에서 제외한다. 설치 상태는 변경하지 않는다.
    /// </summary>
    public Bounds GetSwitchSlotBounds(int index)
    {
        if (_keycaps == null) CacheKeycaps();
        if (_pcbSlotRoot != null) return TransformSlotBounds(_pcbSwitchBounds[index], _pcbSlotRoot);
        return GetRendererSlotBounds(_switchSlotShells[index]);
    }

    /// <summary>
    /// renderer의 로컬 메시 경계를 현재 Transform으로 변환해 월드 슬롯 경계로 반환한다.
    /// renderer가 없으면 빈 경계를 반환하고 오브젝트 상태는 변경하지 않는다.
    /// </summary>
    private static Bounds GetRendererSlotBounds(MeshRenderer renderer)
    {
        if (renderer == null) return new Bounds();
        return TransformSlotBounds(renderer.localBounds, renderer.transform);
    }

    /// <summary>bounds를 space에서 월드 슬롯 경계로 변환한다. 회전과 배율을 반영하며 입력 경계와 부품 상태는 변경하지 않는다.</summary>
    private static Bounds TransformSlotBounds(Bounds bounds, Transform space)
    {
        Vector3 center = space.TransformPoint(bounds.center);
        Vector3 x = space.TransformVector(Vector3.right * bounds.extents.x);
        Vector3 y = space.TransformVector(Vector3.up * bounds.extents.y);
        Vector3 z = space.TransformVector(Vector3.forward * bounds.extents.z);
        Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(center, extents * 2f);
    }

    /// <summary>pcb의 실제 기판 윗면과 기존 키별 대응으로 슬롯 자세와 경계를 한 번 연결한다. 기존 실물의 위치만 맞추고 바인딩·부모·이름·축 종류는 유지한다.</summary>
    public void ConnectPcbSlots(Transform pcb)
    {
        if (_pcbSlotRoot != null) return;
        if (_keycaps == null) CacheKeycaps();
        _pcbSlotRoot = pcb;
        _pcbSwitchPositions = new Vector3[_switches.Length];
        _pcbSwitchRotations = new Quaternion[_switches.Length];
        _pcbKeycapPositions = new Vector3[_keycaps.Length];
        _pcbKeycapRotations = new Quaternion[_keycaps.Length];
        _pcbSwitchBounds = new Bounds[_switches.Length];
        _pcbKeycapBounds = new Bounds[_keycaps.Length];
        MeshFilter board = pcb.Find("PCB_Board").GetComponent<MeshFilter>();
        Bounds boardBounds = board.sharedMesh.bounds;
        float surface = pcb.InverseTransformPoint(board.transform.TransformPoint(
            new Vector3(boardBounds.center.x, boardBounds.max.y, boardBounds.center.z))).y;
        KeyboardInputController input = GetComponent<KeyboardInputController>();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (!input.TryGetSwitch(_keycaps[index], out KeyboardSwitch boundSwitch) || boundSwitch != _switches[index])
                throw new InvalidOperationException("PCB slot must preserve its existing key-to-switch binding.");
            MeshRenderer shell = _switchSlotShells[index];
            Bounds shellBounds = shell.localBounds;
            Vector3 bottom = pcb.InverseTransformPoint(shell.transform.TransformPoint(
                new Vector3(shellBounds.center.x, shellBounds.min.y, shellBounds.center.z)));
            Vector3 offset = Vector3.up * (surface + PCB_SWITCH_CLEARANCE - bottom.y);
            _pcbSwitchPositions[index] = pcb.InverseTransformPoint(boundSwitch.transform.position) + offset;
            _pcbKeycapPositions[index] = pcb.InverseTransformPoint(_keycaps[index].position) + offset;
            _pcbSwitchRotations[index] = Quaternion.Inverse(pcb.rotation) * boundSwitch.transform.rotation;
            _pcbKeycapRotations[index] = Quaternion.Inverse(pcb.rotation) * _keycaps[index].rotation;
            RestorePcbSlotPose(index, true);
            RestorePcbSlotPose(index, false);
            AlignPcbStabilizers(boundSwitch.transform, surface + PCB_SWITCH_CLEARANCE + PCB_STABILIZER_HEIGHT); //스테빌라이저 높이 보정 값
            _pcbSwitchBounds[index] = GetPcbLocalBounds(shell);
            _pcbKeycapBounds[index] = GetPcbLocalBounds(_keycapSlotShells[index]);
        }
        input.RefreshKeyRestPositions();
    }

    /// <summary>
    /// switchRoot의 스태빌라이저 하우징 바닥을 PCB 로컬 targetHeight에 맞춘다.
    /// 하우징과 철심 및 스템을 같은 거리로 이동해 결합 간격을 유지한다.
    /// </summary>
    private void AlignPcbStabilizers(Transform switchRoot, float targetHeight)
    {
        float bottom = float.PositiveInfinity;
        foreach (Transform child in switchRoot)
        {
            if (!child.name.StartsWith("Stabilizer_Housing", StringComparison.Ordinal)) continue;
            foreach (MeshRenderer renderer in child.GetComponentsInChildren<MeshRenderer>(true))
                bottom = Mathf.Min(bottom, GetPcbLocalBounds(renderer).min.y);
        }
        if (float.IsPositiveInfinity(bottom)) return;
        Vector3 offset = _pcbSlotRoot.TransformVector(Vector3.up * (targetHeight - bottom));
        foreach (Transform child in switchRoot)
            if (child.name.StartsWith("Stabilizer_", StringComparison.Ordinal)) child.position += offset;
    }

    /// <summary>renderer의 원본 슬롯 형상을 PCB 로컬 경계로 반환한다. 비활성 실물도 검사하며 설치 연출과 키 눌림의 임시 이동은 저장하지 않는다.</summary>
    private Bounds GetPcbLocalBounds(MeshRenderer renderer)
    {
        Bounds source = renderer.localBounds;
        Matrix4x4 matrix = _pcbSlotRoot.worldToLocalMatrix * renderer.localToWorldMatrix;
        Bounds result = new Bounds(matrix.MultiplyPoint3x4(source.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            result.Encapsulate(matrix.MultiplyPoint3x4(source.center + Vector3.Scale(source.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
        return result;
    }

    /// <summary>index와 switchSlot에 대응하는 기존 실물을 PCB 슬롯 자세로 맞춘다. 크기·재질·설치 상태와 부모 경로는 유지한다.</summary>
    private void RestorePcbSlotPose(int index, bool switchSlot)
    {
        if (_pcbSlotRoot == null) return;
        Transform part = switchSlot ? _switches[index].transform : _keycaps[index];
        Vector3 position = switchSlot ? _pcbSwitchPositions[index] : _pcbKeycapPositions[index];
        Quaternion rotation = switchSlot ? _pcbSwitchRotations[index] : _pcbKeycapRotations[index];
        part.SetPositionAndRotation(_pcbSlotRoot.TransformPoint(position), _pcbSlotRoot.rotation * rotation);
    }

    /// <summary>
    /// index에 해당하는 축 루트 Transform을 반환한다. 배율과 설치 상태는 변경하지 않는다.
    /// </summary>
    public Transform GetSwitchSlotTransform(int index) => _switches[index].transform;

    /// <summary>index의 PCB 키캡 설치 여부를 반환한다. 슬롯의 현재 외형 표시를 검사하며 상태는 변경하지 않는다.</summary>
    public bool IsPcbKeycapInstalled(int index)
    {
        if (_keycaps == null) CacheKeycaps();
        return IsKeycapInstalled(index);
    }

    /// <summary>index의 PCB 축 설치 여부를 반환한다. 연결된 키캡 슬롯의 축 상태를 검사하며 상태는 변경하지 않는다.</summary>
    public bool IsPcbSwitchInstalled(int index)
    {
        if (_keycaps == null) CacheKeycaps();
        return HasSwitchForKeycap(index);
    }

    /// <summary>
    /// index의 빈 축 슬롯에 type을 설치하고 성공 여부를 반환한다.
    /// 키캡이나 축이 이미 있으면 거부하고 성공 시 외형과 타건음을 갱신하며 설치 연출을 재생한다.
    /// </summary>
    public bool InstallSwitchFromInventory(int index, SwitchType type)
    {
        if (_keycaps == null) CacheKeycaps();
        if (!CanInstallSwitchFromInventory(index, type)) return false;
        RestorePcbSlotPose(index, true);
        KeyboardSwitch keyboardSwitch = _switches[index];
        if (!keyboardSwitch.ChangeSwitch(type)) return false;
        PlayPartInstallation(keyboardSwitch.transform);
        _switchType = type;
        GetComponent<KeyboardInputController>().SetSwitchSound(type);
        return true;
    }

    /// <summary>
    /// index의 빈 키캡 슬롯에 material과 definition을 설치하고 성공 여부를 반환한다.
    /// 축 설치와 키캡 규격을 검사하며 재고는 호출자가 관리한다. 성공 시 외형과 설치 상태를 갱신한다.
    /// </summary>
    public bool InstallKeycapFromInventory(int index, Material material, Game.Tournament.KeycapDefinition definition)
    {
        if (_keycaps == null) CacheKeycaps();
        if (!CanInstallKeycapFromInventory(index, material, definition)) return false;
        RestorePcbSlotPose(index, false);
        return ApplyKeycapMaterialToKey(index, material, true, definition);
    }

    /// <summary>index의 축 슬롯 점유와 type 데이터로 설치 가능 여부를 반환한다. 외형, 재고와 효과음은 변경하지 않는다.</summary>
    public bool CanInstallSwitchFromInventory(int index, SwitchType type)
    {
        if (_keycaps == null) CacheKeycaps();
        if (index < 0 || index >= _switches.Length) return false;
        if (TryGetComponent<KeyboardModeling.Assembly.KeyboardAssemblyState>(out var assembly)
            && !assembly.CanInstallSwitch(HasSwitchForKeycap(index), IsKeycapInstalled(index))) return false;
        return index >= 0 && index < _switches.Length && !HasSwitchForKeycap(index)
            && !IsKeycapInstalled(index) && _switches[index].HasSwitchType(type);
    }

    /// <summary>index의 점유와 축 및 material, definition 규격으로 설치 가능 여부를 반환한다. 실제 설치는 수행하지 않는다.</summary>
    public bool CanInstallKeycapFromInventory(int index, Material material, Game.Tournament.KeycapDefinition definition)
    {
        if (_keycaps == null) CacheKeycaps();
        if (index < 0 || index >= _keycaps.Length) return false;
        if (TryGetComponent<KeyboardModeling.Assembly.KeyboardAssemblyState>(out var assembly)
            && !assembly.CanInstallKeycap(IsKeycapInstalled(index), HasSwitchForKeycap(index))) return false;
        if (index < 0 || index >= _keycaps.Length || IsKeycapInstalled(index) || material == null
            || !HasSwitchForKeycap(index) || _keycaps[index] == null) return false;
        Transform keycap = _keycaps[index];
        if (definition != null && definition.OneUnitOnly && !_keycapModels[keycap].IsOneUnit) return false;
        foreach (MeshRenderer renderer in _keycapRenderers)
            if (renderer != null && renderer.transform.IsChildOf(keycap)) return true;
        return false;
    }

    /// <summary>
    /// 입력 키에 바인딩된 물리 키캡을 재질 및 이미지 편집용 인덱스로 변환한다.
    /// key를 사용하며 대응하는 키캡이 없으면 false와 -1을 반환한다.
    /// </summary>
    public bool TryGetKeycapIndex(Key key, out int keycapIndex)
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
    /// 기존 사진 편집 입력 키를 사진용 키캡 배열 인덱스로 변환한다.
    /// key를 사용하며 대응하는 키캡이 없으면 false와 -1을 반환한다.
    /// </summary>
    public bool TryGetPhotoKeycapIndex(Key key, out int keycapIndex)
    {
        return TryGetKeycapIndex(key, out keycapIndex);
    }

    /// <summary>
    /// 현재 전체 키캡에 지정된 Material을 반환한다.
    /// 사용자 지정 상태를 읽으며 반환값은 _keycapMaterial이다.
    /// </summary>
    public Material KeycapMaterial => _keycapMaterial;

    public string KeycapPlacementError { get; private set; }

    /// <summary>
    /// keycapIndex 키캡에 현재 지정된 개별 재질 또는 공통 재질을 반환한다.
    /// 유효하지 않은 인덱스는 null을 반환하며 개별 재질이 있으면 우선 사용한다.
    /// </summary>
    public Material GetKeycapMaterial(int keycapIndex)
    {
        if (_keycaps == null) CacheKeycaps();
        if (keycapIndex < 0 || keycapIndex >= _keycaps.Length || _keycaps[keycapIndex] == null)
            return null;

        return _keycapMaterialOverrides.TryGetValue(_keycaps[keycapIndex], out Material material)
            ? material
            : _keycapMaterial;
    }

    /// <summary>
    /// keycapIndex에 명시적으로 캐시된 키캡 외피의 실제 첫 재질 슬롯을 반환한다.
    /// 공통 선택값이나 글자 및 부속 재질은 읽지 않으며 외피 참조 누락은 설정 오류로 알린다.
    /// </summary>
    public Material GetKeycapSurfaceMaterial(int keycapIndex)
    {
        if (_keycaps == null) CacheKeycaps();
        if (keycapIndex < 0 || keycapIndex >= _keycapSlotShells.Length || _keycapSlotShells[keycapIndex] == null)
            throw new InvalidOperationException("필수 키캡 외피 렌더러 참조가 없습니다.");
        return _keycapSlotShells[keycapIndex].sharedMaterial;
    }

    /// <summary>
    /// 진행 중인 미리보기를 제외한 적용 재질을 범위에 맞춰 반환한다.
    /// allKeycaps이면 공통 재질을 반환하고 아니면 keycapIndex의 개별 재질을 반환한다.
    /// </summary>
    public Material GetCommittedKeycapMaterial(bool allKeycaps, int keycapIndex)
    {
        if (_keycapMaterialPreviewSnapshot == null)
            return allKeycaps ? _keycapMaterial : GetKeycapMaterial(keycapIndex);

        if (allKeycaps)
            return _keycapMaterialPreviewSnapshot.KeycapMaterial;

        if (_keycaps == null) CacheKeycaps();
        if (keycapIndex < 0 || keycapIndex >= _keycaps.Length || _keycaps[keycapIndex] == null)
            return null;

        return _keycapMaterialPreviewSnapshot.MaterialOverrides.TryGetValue(_keycaps[keycapIndex], out Material material)
            ? material
            : _keycapMaterialPreviewSnapshot.KeycapMaterial;
    }

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
    /// 현재 재질과 축, 보강판 및 LED 값을 읽고 이미지 상태 수집에 필요한 참조를 준비한다.
    /// 미사용 이미지도 None으로 반환하며 이미지 바이트나 경로는 포함하지 않는다.
    /// </summary>
    public IEnumerable<string[]> GetCustomizationLogValues()
    {
        Material committedMaterial = GetCommittedKeycapMaterial(true, -1);
        yield return new[] { "Keycap", committedMaterial != null ? committedMaterial.name : "" };
        foreach (string[] value in GetKeycapLogValues()) yield return value;
        yield return new[] { "Case", _caseMaterial != null ? _caseMaterial.name : "" };
        yield return new[] { "Switch", _switchType.ToString() };
        yield return new[] { "Plate", _plateMaterial != null ? _plateMaterial.name : "" };
        yield return new[] { "PlateEffect", _plateEffect.ToString() };
        yield return new[] { "LED", _ledPattern.ToString() };
        yield return new[] { "LEDColor", ColorUtility.ToHtmlStringRGBA(_ledColor) };
        yield return new[] { "LEDEnabled", _ledEnabled ? "true" : "false" };
        KeyboardImageCustomization images = GetImageCustomization();
        foreach (string[] value in images.GetLogValues()) yield return value;
    }

    /// <summary>
    /// 현재 키캡과 미리보기 이전 상태를 읽어 키별 확정 장착 정보를 반환한다.
    /// 입력 없이 파츠 ID, 인덱스, 종류, 재질 및 장착 여부를 열거하며 상태는 변경하지 않는다.
    /// </summary>
    public IEnumerable<string[]> GetKeycapLogValues()
    {
        if (_keycaps == null) CacheKeycaps();
        KeycapMaterialPreviewSnapshot preview = _keycapMaterialPreviewSnapshot;
        for (int index = 0; index < _keycaps.Length; index++)
        {
            Transform keycap = _keycaps[index];
            var definitions = preview != null ? preview.PartDefinitions : _keycapPartDefinitions;
            Game.Tournament.KeycapDefinition definition = null;
            if (keycap != null) definitions.TryGetValue(keycap, out definition);
            bool installed = preview != null ? preview.KeycapActiveStates[index] : IsKeycapInstalled(index);
            Material material = GetCommittedKeycapMaterial(false, index);
            yield return new[] { "Keycap:" + index, Game.Logging.CustomizationTelemetry.GetKeycapElementId(definition, material),
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Game.Logging.CustomizationTelemetry.GetKeycapKind(definition, material),
                definition != null ? definition.MaterialId : material != null ? material.name : "",
                installed ? "true" : "false" };
        }
    }

    void OnEnable()
    {
        KeyboardModeling.Assembly.KeyboardAssemblyState assembly = GetComponent<KeyboardModeling.Assembly.KeyboardAssemblyState>();
        if (Application.IsPlaying(gameObject) && assembly != null && assembly.Initialized)
        {
            AudioManager.Ready -= HandleAudioManagerReady;
            AudioManager.Ready += HandleAudioManagerReady;
            if (AudioManager.instance != null) HandleAudioManagerReady(AudioManager.instance);
            return;
        }
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
            if (assembly != null) assembly.InitializeNewAssembly();
            else
            {
                DisableKeycaps();
                DisableSwitches();
                SetLEDEnabled(false);
            }
        }
    }

    /// <summary>
    /// 새 전투 키보드의 축과 키캡을 즉시 표시한다.
    /// 현재 재질과 축별 타입을 유지하고 작업실 초기 비활성 상태만 해제한다.
    /// </summary>
    public void PrepareForBattle()
    {
        if (TryGetComponent<KeyboardModeling.Assembly.KeyboardAssemblyState>(out _)) return;
        foreach (KeyboardSwitch keyboardSwitch in _switches) keyboardSwitch.gameObject.SetActive(true);
        GetComponent<KeyboardInputController>().SetSwitchSound(_switchType);
        if (_keycaps == null) CacheKeycaps();
        foreach (Transform keycap in _keycaps) keycap.gameObject.SetActive(true);
    }

    /// <summary>
    /// 신규 조립 초기화에서 숨긴 기본 축과 키캡을 완성 키보드로 표시한다.
    /// 저장된 축 종류로 입력음을 복원하고 부품과 LED를 활성화한다.
    /// </summary>
    public void PrepareCompletedAssembly()
    {
        foreach (KeyboardSwitch keyboardSwitch in _switches) keyboardSwitch.gameObject.SetActive(true);
        if (_keycaps == null) CacheKeycaps();
        foreach (Transform keycap in _keycaps) keycap.gameObject.SetActive(true);
        GetComponent<KeyboardInputController>().SetSwitchSound(_switchType);
        SetLEDEnabled(true);
    }

    /// <summary>
    /// catalog와 materialIds로 구성을 복사하고 미설치 부품만 defaults의 값으로 채운다.
    /// 전투용 상태를 반환하며 작업실 키보드의 재질과 설치 여부는 유지한다.
    /// </summary>
    public KeyboardCustomizationState CaptureBattleState(Material[] catalog, string[] materialIds, KeyboardCustomizationState defaults,
        IReadOnlyList<Game.Tournament.KeycapDefinition> keycapDefinitions = null)
    {
        KeyboardCustomizationState state = CaptureState(catalog, materialIds, keycapDefinitions);
        MeshRenderer[] renderers = GetCustomizationRenderers();
        var counts = (int[])state.SlotCounts.Clone();
        var materials = new List<int>();
        var defaultOffsets = new Dictionary<string, int>();
        int offset = 0;
        for (int index = 0; index < defaults.RendererPaths.Length; index++)
        {
            defaultOffsets.Add(defaults.RendererPaths[index], offset);
            offset += defaults.SlotCounts[index];
        }
        int slot = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            int missingKey = -1;
            for (int key = 0; key < _keycaps.Length; key++)
                if (renderers[index].transform.IsChildOf(_keycaps[key]) && !_keycaps[key].gameObject.activeSelf)
                {
                    missingKey = key;
                    break;
                }
            if (missingKey >= 0)
            {
                state.ImageState.KeycapImages[missingKey] = -1;
                state.ImageState.IndividualImages[missingKey] = false;
                int defaultIndex = Array.IndexOf(defaults.RendererPaths, state.RendererPaths[index]);
                counts[index] = defaults.SlotCounts[defaultIndex];
                int start = defaultOffsets[state.RendererPaths[index]];
                for (int material = 0; material < counts[index]; material++)
                {
                    int oldIndex = defaults.MaterialIndices[start + material];
                    materials.Add(oldIndex < 0 ? -1 : Array.IndexOf(state.CatalogIds, defaults.CatalogIds[oldIndex]));
                }
                if (state.HasKeycapDefinitions && renderers[index].name == "PBT_SculptedShell")
                {
                    var layout = GetComponent<KeyboardInputController>().GetKeycapLayout();
                    for (int key = 0; key < state.KeycapKeys.Length; key++)
                        if (layout[state.KeycapKeys[key]].transform == _keycaps[missingKey])
                        {
                            if (defaults.HasKeycapDefinitions)
                                state.KeycapDefinitionIds[key] = defaults.KeycapDefinitionIds[Array.IndexOf(defaults.KeycapKeys, state.KeycapKeys[key])];
                            else
                            {
                                int material = defaults.MaterialIndices[start];
                                int registered = material < 0 ? -1 : Array.IndexOf(materialIds, defaults.CatalogIds[material]);
                                var part = registered < 0 ? null : FindLegacyKeycapDefinition(catalog[registered], keycapDefinitions);
                                state.KeycapDefinitionIds[key] = part != null ? part.Id : "";
                            }
                            break;
                        }
                }
            }
            else
                for (int material = 0; material < counts[index]; material++) materials.Add(state.MaterialIndices[slot + material]);
            slot += state.SlotCounts[index];
        }
        for (int index = 0; index < _switches.Length; index++)
            if (!_switches[index].gameObject.activeSelf) state.SwitchTypes[index] = defaults.SwitchTypes[index];
        for (int index = 0; index < state.KeycapInstalled.Length; index++) state.KeycapInstalled[index] = true;
        for (int index = 0; index < state.SwitchInstalled.Length; index++) state.SwitchInstalled[index] = true;
        return new KeyboardCustomizationState(state.LedEnabled, state.LedColor, state.LedIntensity, state.Selection,
            state.CatalogIds, state.RendererPaths, counts, materials.ToArray(), state.SwitchTypes, state.PlateType,
            state.LedPattern, state.LedSpeed, state.ImageState, state.PlateMaterialId, state.PlateEffect,
            state.KeycapInstalled, state.SwitchInstalled, state.KeycapKeys, state.KeycapDefinitionIds);
    }

    /// <summary>
    /// inventory의 고정 키 배치와 전체 부품을 기본 키캡 위에 즉시 적용한다.
    /// defaultKeycap을 사용하며 사진은 유지하고 설치 연출 없이 외형과 축 종류를 갱신한다.
    /// </summary>
    public void ApplyTournamentParts(Game.Tournament.TournamentInventory inventory, Game.Tournament.KeycapDefinition defaultKeycap)
    {
        PrepareForBattle();
        KeyboardImageCustomization images = GetImageCustomization();
        KeyboardImageState photos = images.CaptureState();
        Texture2D[] prepared = images.PrepareState(photos);
        images.ClearKeycapImage();
        images.ClearCaseImage();
        if (inventory.Switch != null) ApplySwitchType(inventory.Switch.Data.Type, false);
        if (inventory.Plate != null) SetPlateMaterial(inventory.Plate.Data.Material);
        if (inventory.Housing != null) SetCaseMaterial(inventory.Housing.Material);
        _keycapMaterial = defaultKeycap.Material;
        _keycapMaterialIndex = Array.IndexOf(_materials, _keycapMaterial);
        _keycapMaterialOverrides.Clear();
        ResetKeycapModels();
        foreach (var binding in GetComponent<KeyboardInputController>().GetKeycapLayout())
            if (inventory.Keycaps.TryGetValue(binding.Key, out Game.Tournament.KeycapDefinition part))
            {
                _keycapMaterialOverrides[binding.Value.transform] = part.Material;
                SetKeycapModel(binding.Value.transform, part);
            }
            else SetKeycapModel(binding.Value.transform, defaultKeycap);
        ApplyKeycapMaterial();
        images.RestoreState(photos, prepared);
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        if (PrefabUtility.IsPartOfPrefabAsset(this)) return;
        EditorApplication.delayCall -= ApplyCaseEditorPreview;
        EditorApplication.delayCall += ApplyCaseEditorPreview;
#endif
    }

    void OnDisable()
    {
        CancelKeycapMaterialPreview();
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
        if (this == null || PrefabUtility.IsPartOfPrefabAsset(this))
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
    /// catalog와 materialIds로 렌더러 상태를 수집하고 keycapDefinitions로 키별 SO ID를 기록한다.
    /// 카탈로그가 있으면 버전 5 상태를 반환하며 등록되지 않은 재질이나 모호한 이전 부품 식별은 예외로 알린다.
    /// </summary>
    public KeyboardCustomizationState CaptureState(Material[] catalog, string[] materialIds,
        IReadOnlyList<Game.Tournament.KeycapDefinition> keycapDefinitions = null)
    {
        if (_keycaps == null) CacheKeycaps();
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
        var switchInstalled = new bool[_switches.Length];
        for (int index = 0; index < _switches.Length; index++)
        {
            switchTypes[index] = _switches[index].CurrentData != null ? _switches[index].CurrentData.Type : _switchType;
            switchInstalled[index] = _switches[index].gameObject.activeSelf;
        }
        var keycapInstalled = new bool[_keycaps.Length];
        for (int index = 0; index < _keycaps.Length; index++)
            keycapInstalled[index] = _keycaps[index] != null && _keycaps[index].gameObject.activeSelf;

        UnityEngine.InputSystem.Key[] keycapKeys = null;
        string[] keycapIds = null;
        if (keycapDefinitions != null)
        {
            var layout = GetComponent<KeyboardInputController>().GetKeycapLayout();
            var keys = new List<UnityEngine.InputSystem.Key>(layout.Keys);
            keys.Sort();
            keycapKeys = keys.ToArray();
            keycapIds = new string[keycapKeys.Length];
            for (int index = 0; index < keycapKeys.Length; index++)
            {
                Transform keycap = layout[keycapKeys[index]].transform;
                if (!_keycapPartDefinitions.TryGetValue(keycap, out Game.Tournament.KeycapDefinition part))
                {
                    MeshRenderer shell = keycap.Find("PBT_SculptedShell").GetComponent<MeshRenderer>();
                    Material[] materials = GetImageCustomization().GetMaterialsForStorage(shell);
                    part = FindLegacyKeycapDefinition(materials[0], keycapDefinitions);
                }
                keycapIds[index] = part != null ? part.Id : "";
            }
        }

        int plateMaterialIndex = GetMaterialIndex(_plateMaterial, catalog);
        return new KeyboardCustomizationState(_ledEnabled, _ledColor, _ledIntensity,
            new[] { GetMaterialIndex(_caseMaterial, catalog), GetMaterialIndex(_keycapMaterial, catalog), _caseMaterialIndex, _keycapMaterialIndex },
            (string[])materialIds.Clone(), paths, counts, indices.ToArray(), switchTypes,
            _plateType, _ledPattern, _ledSpeed,
            GetImageCustomization().CaptureState(),
            plateMaterialIndex >= 0 ? materialIds[plateMaterialIndex] : null, _plateEffect,
            keycapInstalled, switchInstalled, keycapKeys, keycapIds);
    }

    /// <summary>
    /// state의 키별 SO ID와 Material 카탈로그를 검증한 뒤 LED, 모델, 재질과 나머지 부품을 복원한다.
    /// 반환값 없이 현재 인스턴스만 변경하며 호환되지 않는 데이터는 적용 전에 예외로 알린다.
    /// </summary>
    public void ApplyState(KeyboardCustomizationState state, Material[] catalog, string[] materialIds,
        IReadOnlyList<Game.Tournament.KeycapDefinition> keycapDefinitions = null)
    {
        if (_keycaps == null) CacheKeycaps();
        if (state == null || (state.Version < 1 || state.Version > 6) || state.Selection == null || state.Selection.Length != 4 ||
            state.CatalogIds == null || state.RendererPaths == null || state.SlotCounts == null ||
            state.MaterialIndices == null || state.SwitchTypes == null || catalog.Length != materialIds.Length)
            throw new InvalidOperationException("Save data is invalid or unsupported.");

        if (state.HasInstallationState && (state.KeycapInstalled == null || state.SwitchInstalled == null ||
            state.KeycapInstalled.Length != _keycaps.Length || state.SwitchInstalled.Length != _switches.Length))
            throw new InvalidOperationException("Saved part installation layout is incompatible.");

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

        foreach (int index in state.MaterialIndices) ValidateSavedMaterialIndex(index, remap.Length);
        Dictionary<Transform, Game.Tournament.KeycapDefinition> savedParts = ResolveSavedKeycaps(state, renderers,
            catalog, remap, keycapDefinitions);

        int totalSlots = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            int expectedSlots = renderers[index] != null ? renderers[index].sharedMaterials.Length : 0;
            if (renderers[index] != null && renderers[index].name == "PBT_SculptedShell")
            {
                savedParts.TryGetValue(renderers[index].transform.parent, out Game.Tournament.KeycapDefinition model);
                KeycapModelAppearance appearance = _keycapModels[renderers[index].transform.parent];
                if (model != null && model.OneUnitOnly && !appearance.IsOneUnit)
                    throw new InvalidOperationException("A saved keycap model requires a 1u slot.");
                expectedSlots = model != null && model.ShellMesh != null ? model.ModelMaterials.Length : appearance.OriginalMaterialCount;
            }
            if (renderers[index] == null || state.RendererPaths[index] != GetRendererPath(renderers[index].transform) ||
                state.SlotCounts[index] != expectedSlots)
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
        if (_partInstallation != null) _partInstallation.CancelAll();
        images.ClearKeycapImage();
        images.ClearCaseImage();

        ResetKeycapModels();

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
            if (renderers[index].name == "PBT_SculptedShell")
            {
                savedParts.TryGetValue(renderers[index].transform.parent, out Game.Tournament.KeycapDefinition part);
                SetKeycapModel(renderers[index].transform.parent, part);
                if (part == null || part.ShellMesh == null)
                {
                    if (part != null) materials[0] = part.Material;
                    renderers[index].sharedMaterials = materials;
                }
            }
            KeyboardToonSurface.ApplyBounds(renderers[index]);
        }

        RememberKeycapMaterialOverrides();
        images.RestoreState(state.ImageState, preparedImages);

        for (int index = 0; index < _switches.Length; index++)
            _switches[index].gameObject.SetActive(!state.HasInstallationState || state.SwitchInstalled[index]);
        if (!HasAnySwitchForKeycaps()) GetComponent<KeyboardInputController>().DisableSwitchSound();
        for (int index = 0; index < _keycaps.Length; index++)
            if (_keycaps[index] != null) _keycaps[index].gameObject.SetActive(!state.HasInstallationState || state.KeycapInstalled[index]);

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
    /// 반환값 없이 축을 숨기고 FMOD 타건음을 Disabled로 변경하며 키캡과 축 데이터는 유지한다.
    /// </summary>
    public void DisableSwitches()
    {
        foreach (KeyboardSwitch keyboardSwitch in _switches)
        {
            if (keyboardSwitch != null) keyboardSwitch.gameObject.SetActive(false);
        }
        GetComponent<KeyboardInputController>().DisableSwitchSound();
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
        if (activate && TryGetComponent<KeyboardModeling.Assembly.KeyboardAssemblyState>(out var assembly))
        {
            if (_keycaps == null) CacheKeycaps();
            for (int index = 0; index < _switches.Length; index++)
                if (!assembly.CanInstallSwitch(HasSwitchForKeycap(index), IsKeycapInstalled(index))) return false;
        }
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
                bool playSound = !keyboardSwitch.gameObject.activeSelf || keyboardSwitch.CurrentData == null
                    || keyboardSwitch.CurrentData.Type != type;
                _partInstallation.ReplaceSwitch(keyboardSwitch.transform, keycap, () => keyboardSwitch.ChangeSwitch(type), playSound);
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
    private void PlayPartInstallation(Transform part, bool playSound = true)
    {
        if (!Application.IsPlaying(gameObject))
        {
            part.gameObject.SetActive(true);
            return;
        }
        PreparePartInstallation();
        _partInstallation.Play(part, playSound);
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
    /// 현재 인스턴스의 축에 클릭 충돌체가 없으면 렌더 메시 범위로 BoxCollider를 생성한다.
    /// 비활성 축도 준비하되 활성 상태와 프리팹 에셋은 변경하지 않는다. 반환값은 없다.
    /// </summary>
    public void PrepareSwitchRemovalTargets()
    {
        foreach (KeyboardSwitch keyboardSwitch in _switches)
        {
            if (keyboardSwitch.GetComponentInChildren<Collider>(true) != null) continue;
            Transform root = keyboardSwitch.transform;
            Bounds total = new Bounds();
            bool found = false;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue;
                Transform branch = renderer.transform;
                while (branch.parent != root) branch = branch.parent;
                if (branch.name.StartsWith("Stabilizer_", StringComparison.Ordinal)
                    || renderer.name == "AbilityUnlockOutline") continue;
                Bounds bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(
                        bounds.center + Vector3.Scale(bounds.extents, sign)));
                    if (found) total.Encapsulate(point);
                    else { total = new Bounds(point, Vector3.zero); found = true; }
                }
            }
            if (!found) continue;
            BoxCollider collider = keyboardSwitch.gameObject.AddComponent<BoxCollider>();
            collider.center = total.center;
            collider.size = total.size;
        }
    }

    /// <summary>
    /// keyboardSwitch의 현재 데이터 타입을 반환하고 아직 데이터가 없으면 키보드의 기본 축 타입을 반환한다.
    /// 외형과 설치 상태는 변경하지 않는다.
    /// </summary>
    public SwitchType GetInstalledSwitchType(KeyboardSwitch keyboardSwitch)
    {
        return keyboardSwitch.CurrentData != null ? keyboardSwitch.CurrentData.Type : _switchType;
    }

    /// <summary>
    /// hit의 장착 파츠를 extractSwitches 종류에 맞춰 제거하고 removed에 원본 Transform을 반환한다.
    /// 미리보기와 설치 연출을 정리하며 키캡이 남은 축은 거부하고 실제 설치 상태와 타건음을 동기화한다.
    /// </summary>
    public bool TryRemovePart(Transform hit, bool extractSwitches, out Transform removed)
    {
        removed = null;
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            Transform keyboardSwitch = index < _switches.Length && _switches[index] != null ? _switches[index].transform : null;
            Transform part = extractSwitches ? keyboardSwitch : _keycaps[index];
            if (part == null || !part.gameObject.activeInHierarchy || !hit.IsChildOf(part)) continue;
            CancelKeycapMaterialPreview();
            ClearPhotoImagePreview();
            if (extractSwitches ? IsKeycapInstalled(index) : !IsKeycapInstalled(index)) return false;
            if (_partInstallation != null) _partInstallation.CancelForRemoval(keyboardSwitch, _keycaps[index]);
            // if (extractSwitches) KeepSwitchStabilizers(keyboardSwitch);
            part.gameObject.SetActive(false);
            if (extractSwitches && !HasAnySwitchForKeycaps()) GetComponent<KeyboardInputController>().DisableSwitchSound();
            removed = part;
            return true;
        }
        return false;
    }

    /// <summary>
    /// keyboardSwitch 아래의 스태빌라이저를 키보드에 고정된 별도 루트로 옮긴다.
    /// 현재 위치와 회전 및 크기를 유지하며 중앙 축 제거 시 보조 부품이 숨거나 떨어지지 않게 한다. 반환값은 없다.
    /// </summary>
    private void KeepSwitchStabilizers(Transform keyboardSwitch)
    {
        Transform fixedRoot = null;
        for (int index = keyboardSwitch.childCount - 1; index >= 0; index--)
        {
            Transform child = keyboardSwitch.GetChild(index);
            if (!child.name.StartsWith("Stabilizer_", StringComparison.Ordinal)) continue;
            if (fixedRoot == null)
            {
                fixedRoot = new GameObject(keyboardSwitch.name + "_Stabilizers").transform;
                fixedRoot.SetParent(keyboardSwitch.parent, false);
                fixedRoot.SetLocalPositionAndRotation(keyboardSwitch.localPosition, keyboardSwitch.localRotation);
                fixedRoot.localScale = keyboardSwitch.localScale;
            }
            child.SetParent(fixedRoot, true);
        }
    }

    /// <summary>
    /// hit가 가리키는 설치된 키캡 또는 제거 가능한 축을 찾아 part와 extractSwitches로 반환한다.
    /// 키캡이 덮고 있는 축은 제외하며 설치 상태는 변경하지 않는다. 발견하면 true를 반환한다.
    /// </summary>
    public bool TryGetRemovablePart(Transform hit, out Transform part, out bool extractSwitches)
    {
        if (_keycaps == null) CacheKeycaps();
        part = null;
        extractSwitches = false;
        for (int index = 0; index < _keycaps.Length; index++)
        {
            bool hasKeycap = IsKeycapInstalled(index);
            if (hasKeycap && hit.IsChildOf(_keycaps[index]))
            {
                part = _keycaps[index];
                return true;
            }
            if (!hasKeycap && HasSwitchForKeycap(index) && hit.IsChildOf(_switches[index].transform))
            {
                part = _switches[index].transform;
                extractSwitches = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 키캡 대상 중 축이 설치된 키가 하나라도 있는지 반환한다.
    /// 캐시된 키캡 인덱스와 각 인덱스의 축 활성 상태를 확인한다.
    /// </summary>
    public bool HasAnySwitchForKeycaps()
    {
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (HasSwitchForKeycap(index))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 비활성화되거나 파손된 키캡이 하나라도 있는지 반환한다.
    /// 키캡 루트와 외피 Renderer 활성 상태를 확인해 재설치가 필요한 대상을 찾는다.
    /// </summary>
    public bool HasMissingKeycaps()
    {
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (!IsKeycapInstalled(index))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 설치된 키캡이 하나라도 있는지 반환한다.
    /// 캐시된 키캡 루트와 외피 Renderer 상태를 확인한다.
    /// </summary>
    public bool HasAnyInstalledKeycaps()
    {
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (IsKeycapInstalled(index))
                return true;
        }

        return false;
    }

    /// <summary>
    /// keycapIndex 키캡의 루트와 외피 Renderer가 모두 표시 상태인지 반환한다.
    /// 슬롯별로 캐시한 외피를 직접 조회하며 설치 상태나 외형은 변경하지 않는다.
    /// </summary>
    private bool IsKeycapInstalled(int keycapIndex)
    {
        if (keycapIndex < 0 || keycapIndex >= _keycaps.Length)
            return false;

        Transform keycap = _keycaps[keycapIndex];
        if (keycap == null || !keycap.gameObject.activeSelf)
            return false;

        MeshRenderer shell = _keycapSlotShells[keycapIndex];
        return shell != null && shell.enabled;
    }

    /// <summary>
    /// 축이 설치된 키캡만 활성화하며 음수 입력은 전체 키캡을 대상으로 한다.
    /// keycapIndex와 축 상태를 사용하며 설치 대상에는 기존 키캡 연출을 재생한다.
    /// </summary>
    private void EnableKeycaps(int keycapIndex = -1, bool playSound = true)
    {
        if (_keycaps == null) CacheKeycaps();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (keycapIndex >= 0 && index != keycapIndex) continue;
            if (!HasSwitchForKeycap(index)) continue;
            Transform keycap = _keycaps[index];
            if (keycap != null) PlayPartInstallation(keycap, playSound);
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
    /// 축이 설치된 keycapIndex 키캡의 사진과 모델을 해제하고 material을 적용해 설치 연출을 재생한다.
    /// 파손된 키캡은 체력과 외피 표시를 복구한다.
    /// material과 인덱스를 사용하며 축 미설치, 잘못된 인덱스 또는 렌더러 누락은 false를 반환한다.
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

        return ApplyKeycapMaterialToKey(keycapIndex, material, true);
    }

    /// <summary>
    /// keycapIndex 위치의 장착 SO를 반환한다. 음수 인덱스는 설치된 키 전체가 같은 SO일 때만 반환하며 혼합 구성이나 미등록 재질은 null이다.
    /// </summary>
    public Game.Tournament.KeycapDefinition GetKeycapDefinition(int keycapIndex)
    {
        if (_keycaps == null) CacheKeycaps();
        if (keycapIndex >= 0)
            return keycapIndex < _keycaps.Length && _keycapPartDefinitions.TryGetValue(_keycaps[keycapIndex], out var part) ? part : null;

        Game.Tournament.KeycapDefinition common = null;
        bool found = false;
        foreach (Transform keycap in _keycaps)
        {
            if (!keycap.gameObject.activeSelf) continue;
            _keycapPartDefinitions.TryGetValue(keycap, out var part);
            if (found && common != part) return null;
            common = part;
            found = true;
        }
        return common;
    }

    /// <summary>definition과 대상 위치로 아티산의 개별 적용과 크기 및 3개 한도를 검사한다. 실패 원인은 KeycapPlacementError에 저장하고 외형은 변경하지 않는다.</summary>
    private bool CanApplyArtisan(Game.Tournament.KeycapDefinition definition, bool allKeycaps, int keycapIndex)
    {
        // WorkShop0의 전체 적용은 대상별 1칸 크기를 검사하고 나머지 키를 유지한다.
        if (Game.Workshop.CustomizationOnlyWorkshop.IsActive && allKeycaps) return true;
        if (definition == null || !definition.IsArtisan) return true;
        if (allKeycaps)
        {
            KeycapPlacementError = "아티산은 전체 적용할 수 없습니다. 키캡 한 개로 적용하세요.";
            return false;
        }
        if (_keycaps == null) CacheKeycaps();
        if (keycapIndex < 0 || keycapIndex >= _keycaps.Length)
        {
            KeycapPlacementError = "아티산을 놓을 키를 입력하세요.";
            return false;
        }
        if (definition.OneUnitOnly && !_keycapModels[_keycaps[keycapIndex]].IsOneUnit)
        {
            KeycapPlacementError = "아티산은 일반 크기의 1칸 키에만 놓을 수 있습니다. 다른 키를 누르세요.";
            return false;
        }
        // WorkShop0에서도 크기 조건은 유지하고 장착 개수만 제한하지 않는다.
        if (Game.Workshop.CustomizationOnlyWorkshop.IsActive) return true;
        int count = 0;
        for (int index = 0; index < _keycaps.Length; index++)
        {
            if (index == keycapIndex) continue;
            Transform keycap = _keycaps[index];
            if (keycap.gameObject.activeSelf && _keycapPartDefinitions.TryGetValue(keycap, out var part) && part.IsArtisan) count++;
        }
        if (count < 3) return true;
        KeycapPlacementError = "아티산은 같은 종류를 포함해 최대 3개까지 장착할 수 있습니다. 기존 아티산을 교체하세요.";
        return false;
    }

    /// <summary>keycap 루트에 장착된 SO를 반환한다. 사진용 재질과 무관하게 실제 부품 정의를 읽으며 구성은 변경하지 않는다.</summary>
    public Game.Tournament.KeycapDefinition GetKeycapDefinition(Transform keycap)
    {
        return _keycapPartDefinitions.TryGetValue(keycap, out var part) ? part : null;
    }

    /// <summary>
    /// 등록된 렌더러 배열의 PBT_SculptedShell로 키캡 루트와 대응하는 축 하우징을 캐시한다.
    /// 입력과 반환값 없이 슬롯 인덱스를 고정해 파괴 이후에도 다른 키의 인덱스를 유지한다.
    /// </summary>
    private void CacheKeycaps()
    {
        var keycaps = new List<Transform>();
        var shells = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in _keycapRenderers)
        {
            if (renderer != null && renderer.name == "PBT_SculptedShell")
            {
                keycaps.Add(renderer.transform.parent);
                shells.Add(renderer);
                if (!_keycapModels.ContainsKey(renderer.transform.parent))
                    _keycapModels.Add(renderer.transform.parent, new KeycapModelAppearance(renderer));
            }
        }

        _keycaps = keycaps.ToArray();
        _keycapSlotShells = shells.ToArray();
        _switchSlotShells = new MeshRenderer[_switches.Length];
        for (int index = 0; index < _switches.Length; index++)
            _switchSlotShells[index] = _switches[index].transform.Find("Switch_BottomHousing").GetComponent<MeshRenderer>();
    }

    /// <summary>keycap 외피에 definition의 모델을 적용하거나 원본으로 복원한다. 1u 제한과 재질 슬롯을 검증하고 현재 모델 연결을 갱신한다.</summary>
    private void SetKeycapModel(Transform keycap, Game.Tournament.KeycapDefinition definition)
    {
        KeycapModelAppearance appearance = _keycapModels[keycap];
        if (definition != null && definition.ShellMesh != null)
        {
            if (definition.OneUnitOnly && !appearance.IsOneUnit)
                throw new InvalidOperationException("The keycap model requires a 1u slot.");
            if (definition.ModelMaterials.Length != definition.ShellMesh.subMeshCount ||
                definition.ModelMaterials.Length == 0 || definition.ModelMaterials[0] != definition.Material)
                throw new InvalidOperationException("Keycap model material slots must match the mesh and primary material.");
            _keycapModelDefinitions[keycap] = definition;
        }
        else _keycapModelDefinitions.Remove(keycap);
        if (definition != null) _keycapPartDefinitions[keycap] = definition;
        else _keycapPartDefinitions.Remove(keycap);
        appearance.Apply(definition);
        KeycapDefinitionChanged?.Invoke();
    }

    /// <summary>캐시된 키캡의 원본 메시와 표시 범위를 복원한다. 현재 모델 연결을 비우며 입력과 체력 컴포넌트는 유지한다.</summary>
    private void ResetKeycapModels()
    {
        _keycapPartDefinitions.Clear();
        _keycapModelDefinitions.Clear();
        foreach (KeycapModelAppearance appearance in _keycapModels.Values) appearance.Apply(null);
    }

    /// <summary>이전 저장 형식이나 Material UI의 material을 definitions에서 한 번 해석한다. 여러 부품이 같은 재질을 쓰면 임의 선택하지 않고 오류를 알린다.</summary>
    private Game.Tournament.KeycapDefinition FindLegacyKeycapDefinition(Material material,
        IReadOnlyList<Game.Tournament.KeycapDefinition> definitions)
    {
        if (definitions == null) return null;
        Game.Tournament.KeycapDefinition found = null;
        foreach (Game.Tournament.KeycapDefinition definition in definitions)
            if (definition.Material == material)
            {
                if (found != null && found != definition)
                    throw new InvalidOperationException("Material matches multiple keycaps. Select the keycap by definition ID.");
                found = definition;
            }
        return found;
    }

    /// <summary>id에 해당하는 SO를 definitions에서 반환한다. 빈 ID는 외형 전용 키캡이며 누락되거나 중복된 ID는 오류로 알린다.</summary>
    private Game.Tournament.KeycapDefinition FindKeycapById(string id,
        IReadOnlyList<Game.Tournament.KeycapDefinition> definitions)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Game.Tournament.KeycapDefinition found = null;
        if (definitions != null)
            foreach (Game.Tournament.KeycapDefinition definition in definitions)
                if (definition.Id == id)
                {
                    if (found != null && found != definition) throw new InvalidOperationException("Duplicate keycap definition ID: " + id);
                    found = definition;
                }
        if (found == null) throw new InvalidOperationException("Saved keycap definition is unavailable: " + id);
        return found;
    }

    /// <summary>state의 실제 키와 SO ID를 검증해 키캡 루트별 정의를 반환한다. 키캡 SO ID가 없는 이전 저장만 원본 재질로 해석하며 현재 키보드는 변경하지 않는다.</summary>
    private Dictionary<Transform, Game.Tournament.KeycapDefinition> ResolveSavedKeycaps(KeyboardCustomizationState state,
        MeshRenderer[] renderers, Material[] catalog, int[] remap,
        IReadOnlyList<Game.Tournament.KeycapDefinition> definitions)
    {
        var result = new Dictionary<Transform, Game.Tournament.KeycapDefinition>();
        if (state.HasKeycapDefinitions)
        {
            var layout = GetComponent<KeyboardInputController>().GetKeycapLayout();
            if (state.KeycapKeys == null || state.KeycapDefinitionIds == null || state.KeycapKeys.Length != layout.Count
                || state.KeycapDefinitionIds.Length != state.KeycapKeys.Length)
                throw new InvalidOperationException("Saved keycap identity layout is incomplete.");
            for (int index = 0; index < state.KeycapKeys.Length; index++)
            {
                if (!layout.TryGetValue(state.KeycapKeys[index], out KeycapHealth health) || result.ContainsKey(health.transform))
                    throw new InvalidOperationException("Saved keycap key is missing or duplicated.");
                result.Add(health.transform, FindKeycapById(state.KeycapDefinitionIds[index], definitions));
            }
            return result;
        }
        int slot = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            int count = state.SlotCounts[index];
            if (count < 1 || slot > state.MaterialIndices.Length - count)
                throw new InvalidOperationException("Saved material slots are incomplete.");
            if (renderers[index] != null && renderers[index].name == "PBT_SculptedShell")
            {
                int material = state.MaterialIndices[slot];
                result.Add(renderers[index].transform.parent, material < 0 ? null
                    : FindLegacyKeycapDefinition(catalog[remap[material]], definitions));
            }
            slot += count;
        }
        return result;
    }

    /// <summary>
    /// material을 전체 키캡의 공통 재질로 저장하고 축이 설치된 키캡에 적용한다.
    /// 축이 없는 키캡은 건너뛰고 설치된 축이 없거나 material이 null이면 상태를 유지한다.
    /// </summary>
    public void SetKeycapMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (_keycaps == null) CacheKeycaps();
        if (!HasAnySwitchForKeycaps())
            return;

        bool[] playSounds = new bool[_keycaps.Length];
        for (int index = 0; index < _keycaps.Length; index++)
            playSounds[index] = !IsKeycapInstalled(index) || GetKeycapMaterial(index) != material
                || GetKeycapDefinition(index) != null;
        _keycapMaterial = material;
        _keycapMaterialIndex = Array.IndexOf(_materials, material);
        _keycapMaterialOverrides.Clear();
        for (int index = 0; index < _keycaps.Length; index++)
        {
            ApplyKeycapMaterialToKey(index, material, true, playSoundOverride: playSounds[index]);
        }
    }

    /// <summary>
    /// 축이 설치된 적용 대상에만 키캡 재질 미리보기를 표시하고 이전 상태를 보관한다.
    /// material, allKeycaps, keycapIndex와 선택적 definition을 사용하며 축과 모델 크기 조건을 만족하는 대상이 없으면 false를 반환한다.
    /// </summary>
    public bool PreviewKeycapMaterial(Material material, bool allKeycaps, int keycapIndex,
        Game.Tournament.KeycapDefinition definition = null)
    {
        KeycapPlacementError = "";
        if (_keycapMaterialPreviewSnapshot != null)
            RestoreKeycapMaterialPreviewSnapshot(_keycapMaterialPreviewSnapshot);
        if (!CanApplyArtisan(definition, allKeycaps, keycapIndex))
        {
            CancelKeycapMaterialPreview();
            return false;
        }
        if (material == null)
            return false;

        bool targetHasSwitch = allKeycaps
            ? HasAnySwitchForKeycaps()
            : HasSwitchForKeycap(keycapIndex);
        if (!targetHasSwitch)
        {
            CancelKeycapMaterialPreview();
            return false;
        }

        if (_keycapMaterialPreviewSnapshot == null)
        {
            _keycapMaterialPreviewSnapshot = CaptureKeycapMaterialPreviewSnapshot();
        }
        bool applied = false;
        if (allKeycaps)
        {
            if (definition == null || !definition.OneUnitOnly)
            {
                _keycapMaterial = material;
                _keycapMaterialIndex = Array.IndexOf(_materials, material);
            }
            if (_keycaps == null) CacheKeycaps();
            for (int index = 0; index < _keycaps.Length; index++)
            {
                applied |= ApplyKeycapMaterialToKey(index, material, false, definition);
            }
        }
        else
        {
            applied = ApplyKeycapMaterialToKey(keycapIndex, material, false, definition);
        }

        if (applied)
            return true;

        CancelKeycapMaterialPreview();
        return false;
    }

    /// <summary>
    /// 진행 중인 키캡 재질 미리보기를 취소하고 저장한 재질 및 이미지 상태를 복원한다.
    /// 입력과 반환값 없이 미리보기 전의 공통 재질, 개별 재질, 장착 SO와 모델 및 키캡 사진을 되돌린다.
    /// </summary>
    public void CancelKeycapMaterialPreview()
    {
        if (_keycapMaterialPreviewSnapshot == null)
            return;

        RestoreKeycapMaterialPreviewSnapshot(_keycapMaterialPreviewSnapshot);
        _keycapMaterialPreviewSnapshot = null;
    }

    /// <summary>
    /// 축이 설치된 미리보기 대상을 기존 전체 또는 개별 적용 함수로 확정한다.
    /// material, allKeycaps, keycapIndex와 선택적 definition을 사용하며 축과 모델 크기 조건을 만족하는 대상이 없으면 false를 반환한다.
    /// </summary>
    public bool CommitKeycapMaterialPreview(Material material, bool allKeycaps, int keycapIndex,
        Game.Tournament.KeycapDefinition definition = null)
    {
        KeycapPlacementError = "";
        if (_keycapMaterialPreviewSnapshot == null || material == null)
            return false;

        RestoreKeycapMaterialPreviewSnapshot(_keycapMaterialPreviewSnapshot);
        if (!CanApplyArtisan(definition, allKeycaps, keycapIndex))
        {
            CancelKeycapMaterialPreview();
            return false;
        }

        bool targetHasSwitch = allKeycaps
            ? HasAnySwitchForKeycaps()
            : HasSwitchForKeycap(keycapIndex);
        if (!targetHasSwitch)
        {
            CancelKeycapMaterialPreview();
            return false;
        }

        bool applied;
        if (allKeycaps)
        {
            bool[] playSounds = new bool[_keycaps.Length];
            for (int index = 0; index < _keycaps.Length; index++)
                playSounds[index] = !IsKeycapInstalled(index) || GetKeycapMaterial(index) != material
                    || GetKeycapDefinition(index) != definition;
            if (definition == null || !definition.OneUnitOnly)
            {
                _keycapMaterial = material;
                _keycapMaterialIndex = Array.IndexOf(_materials, material);
            }
            applied = false;
            for (int index = 0; index < _keycaps.Length; index++)
                applied |= ApplyKeycapMaterialToKey(index, material, true, definition, playSounds[index]);
        }
        else
        {
            applied = ApplyKeycapMaterialToKey(keycapIndex, material, true, definition);
        }

        if (!applied)
            return false;

        _keycapMaterialPreviewSnapshot = null;
        return true;
    }

    /// <summary>
    /// 공통 재질, 개별 재질, 장착 SO, 렌더러 슬롯과 이미지 상태를 미리보기 스냅샷으로 수집한다.
    /// 반환된 스냅샷은 미리보기 취소 때 현재 키보드 외형을 정확히 복구하는 데 사용한다.
    /// </summary>
    private KeycapMaterialPreviewSnapshot CaptureKeycapMaterialPreviewSnapshot()
    {
        if (_keycaps == null) CacheKeycaps();

        Transform[] keycaps = (Transform[])_keycaps.Clone();
        bool[] keycapActiveStates = new bool[keycaps.Length];
        for (int index = 0; index < keycaps.Length; index++)
        {
            keycapActiveStates[index] = keycaps[index] != null && keycaps[index].gameObject.activeSelf;
        }

        var rendererMaterials = new Material[_keycapRenderers.Length][];
        bool[] rendererActiveStates = new bool[_keycapRenderers.Length];
        bool[] rendererEnabledStates = new bool[_keycapRenderers.Length];
        for (int index = 0; index < _keycapRenderers.Length; index++)
        {
            MeshRenderer renderer = _keycapRenderers[index];
            rendererMaterials[index] = renderer != null ? renderer.sharedMaterials : Array.Empty<Material>();
            rendererActiveStates[index] = renderer != null && renderer.gameObject.activeSelf;
            rendererEnabledStates[index] = renderer != null && renderer.enabled;
        }

        return new KeycapMaterialPreviewSnapshot(
            _keycapMaterial,
            _keycapMaterialIndex,
            new Dictionary<Transform, Material>(_keycapMaterialOverrides),
            new Dictionary<Transform, Game.Tournament.KeycapDefinition>(_keycapPartDefinitions),
            keycaps,
            keycapActiveStates,
            rendererMaterials,
            rendererActiveStates,
            rendererEnabledStates,
            GetImageCustomization().CaptureState());
    }

    /// <summary>
    /// snapshot의 장착 SO와 모델, 키캡 재질, 렌더러 슬롯과 사진 배치를 현재 키보드에 복원한다.
    /// snapshot의 참조 상태를 사용하며 반환값 없이 기존 외형과 사용자 지정 데이터를 되돌린다.
    /// </summary>
    private void RestoreKeycapMaterialPreviewSnapshot(KeycapMaterialPreviewSnapshot snapshot)
    {
        KeyboardImageCustomization images = GetImageCustomization();
        images.ClearKeycapImage();
        ResetKeycapModels();
        foreach (var part in snapshot.PartDefinitions)
            SetKeycapModel(part.Key, part.Value);
        _keycapMaterial = snapshot.KeycapMaterial;
        _keycapMaterialIndex = snapshot.KeycapMaterialIndex;
        _keycapMaterialOverrides.Clear();
        foreach (KeyValuePair<Transform, Material> materialOverride in snapshot.MaterialOverrides)
            _keycapMaterialOverrides[materialOverride.Key] = materialOverride.Value;

        for (int index = 0; index < _keycapRenderers.Length; index++)
        {
            MeshRenderer renderer = _keycapRenderers[index];
            if (renderer == null) continue;
            renderer.gameObject.SetActive(snapshot.RendererActiveStates[index]);
            renderer.enabled = snapshot.RendererEnabledStates[index];
            renderer.sharedMaterials = (Material[])snapshot.RendererMaterials[index].Clone();
            KeyboardToonSurface.ApplyBounds(renderer);
        }

        Texture2D[] textures = images.PrepareState(snapshot.ImageState);
        images.RestoreState(snapshot.ImageState, textures);

        for (int index = 0; index < snapshot.Keycaps.Length; index++)
        {
            Transform keycap = snapshot.Keycaps[index];
            if (keycap != null)
                keycap.gameObject.SetActive(snapshot.KeycapActiveStates[index]);
        }
        KeycapDefinitionChanged?.Invoke();
    }

    /// <summary>
    /// 축이 설치된 keycapIndex 키캡에 material을 반영하고 필요하면 설치 연출을 재생한다.
    /// playInstallation이 true면 설치 연출과 파손 복구를 실행한다. definition의 SO와 모델을 적용하며 축이나 모델 크기가 맞지 않으면 false를 반환한다.
    /// 유효한 입력은 개별 재질과 사진을 갱신하며 playSoundOverride로 전체 변경 전 판단한 설치음 여부를 유지한다.
    /// </summary>
    private bool ApplyKeycapMaterialToKey(int keycapIndex, Material material, bool playInstallation,
        Game.Tournament.KeycapDefinition definition = null, bool? playSoundOverride = null)
    {
        if (_keycaps == null) CacheKeycaps();
        if (material == null
            || keycapIndex < 0
            || keycapIndex >= _keycaps.Length
            || !HasSwitchForKeycap(keycapIndex))
            return false;

        Transform keycap = _keycaps[keycapIndex];
        if (keycap == null) return false;

        if (definition != null && definition.OneUnitOnly && !_keycapModels[keycap].IsOneUnit)
            return false;

        bool playSound = playSoundOverride ?? (!IsKeycapInstalled(keycapIndex) || GetKeycapMaterial(keycapIndex) != material
            || GetKeycapDefinition(keycapIndex) != definition);
        ClearKeycapImage(keycapIndex);
        SetKeycapModel(keycap, definition);
        _keycapMaterialOverrides[keycap] = material;

        bool applied = false;
        foreach (MeshRenderer renderer in _keycapRenderers)
        {
            if (renderer == null || !renderer.transform.IsChildOf(keycap)) continue;
            if (!playInstallation)
            {
                renderer.gameObject.SetActive(true);
                renderer.enabled = !(_keycapModelDefinitions.ContainsKey(keycap) && KeycapModelAppearance.IsOriginalDetail(renderer));
            }
            if (renderer.name != "PBT_SculptedShell" || definition == null || definition.ShellMesh == null)
                renderer.sharedMaterial = material;
            KeyboardToonSurface.ApplyBounds(renderer);
            applied = true;
        }

        if (applied && !playInstallation)
        {
            keycap.gameObject.SetActive(true);
        }
        else if (applied)
        {
            EnableKeycaps(keycapIndex, playSound);
            if (Application.IsPlaying(gameObject))
            {
                KeycapHealth keycapHealth = keycap.GetComponent<KeycapHealth>();
                if (keycapHealth != null && keycapHealth.CurrentHP <= 0)
                    keycapHealth.RestoreHealth();
            }
        }

        return applied;
    }

    private sealed class KeycapMaterialPreviewSnapshot
    {
        public Material KeycapMaterial { get; }
        public int KeycapMaterialIndex { get; }
        public Dictionary<Transform, Material> MaterialOverrides { get; }
        public Dictionary<Transform, Game.Tournament.KeycapDefinition> PartDefinitions { get; }
        public Transform[] Keycaps { get; }
        public bool[] KeycapActiveStates { get; }
        public Material[][] RendererMaterials { get; }
        public bool[] RendererActiveStates { get; }
        public bool[] RendererEnabledStates { get; }
        public KeyboardImageState ImageState { get; }

        /// <summary>
        /// 미리보기 전 키캡 루트, 재질, 장착 SO, 렌더러와 이미지 상태를 보관한다.
        /// partDefinitions를 포함한 생성 인수는 미리보기 취소와 확정 전 모델 및 외형 복원에 사용한다.
        /// </summary>
        public KeycapMaterialPreviewSnapshot(
            Material keycapMaterial,
            int keycapMaterialIndex,
            Dictionary<Transform, Material> materialOverrides,
            Dictionary<Transform, Game.Tournament.KeycapDefinition> partDefinitions,
            Transform[] keycaps,
            bool[] keycapActiveStates,
            Material[][] rendererMaterials,
            bool[] rendererActiveStates,
            bool[] rendererEnabledStates,
            KeyboardImageState imageState)
        {
            KeycapMaterial = keycapMaterial;
            KeycapMaterialIndex = keycapMaterialIndex;
            MaterialOverrides = materialOverrides;
            PartDefinitions = partDefinitions;
            Keycaps = keycaps;
            KeycapActiveStates = keycapActiveStates;
            RendererMaterials = rendererMaterials;
            RendererActiveStates = rendererActiveStates;
            RendererEnabledStates = rendererEnabledStates;
            ImageState = imageState;
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
                    if (renderer.name == "PBT_SculptedShell" && _keycapModelDefinitions.TryGetValue(keycap, out Game.Tournament.KeycapDefinition model))
                        renderer.sharedMaterials = model.ModelMaterials;
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


