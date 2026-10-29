using UnityEngine;

[CreateAssetMenu(menuName = "Keyboard/Material Catalog")]
public sealed class KeyboardMaterialCatalog : ScriptableObject
{
    [Header("Materials")]
    [SerializeField] private Material[] _materials = new Material[0];
    [SerializeField] private string[] _ids = new string[0];
    [SerializeField] private Game.Tournament.KeycapDefinition[] _keycapDefinitions = new Game.Tournament.KeycapDefinition[0];
    [SerializeField, HideInInspector] private string _defaultStateJson;
    public System.Collections.Generic.IReadOnlyList<Material> Materials => _materials;
    public System.Collections.Generic.IReadOnlyList<string> Ids => _ids;

    /// <summary>
    /// 기본 프리팹에서 수집한 완성 구성을 독립된 객체로 반환한다.
    /// 입력 없이 로봇과 미선택 플레이어의 기본 복원 상태를 제공한다.
    /// </summary>
    public KeyboardCustomizationState GetDefaultState()
    {
        return JsonUtility.FromJson<KeyboardCustomizationState>(_defaultStateJson);
    }

    /// <summary>
    /// keyboard의 구성을 수집하고 빈 키캡과 축을 등록된 기본 구성으로 채운다.
    /// 완성된 전투용 상태를 반환하며 작업실 인스턴스는 변경하지 않는다.
    /// </summary>
    public KeyboardCustomizationState CaptureForBattle(KeyboardCustomization keyboard)
    {
        return keyboard.CaptureBattleState(_materials, _ids,
            GetDefaultState(), _keycapDefinitions);
    }

    /// <summary>
    /// keyboard의 재질과 부품 설정을 등록 ID로 수집한다.
    /// 원본 인스턴스와 독립된 저장 상태를 반환한다.
    /// </summary>
    public KeyboardCustomizationState Capture(KeyboardCustomization keyboard)
    {
        return keyboard.CaptureState(_materials, _ids, _keycapDefinitions);
    }

    /// <summary>
    /// state의 ID를 등록 재질로 해석하여 keyboard에 적용한다.
    /// 대상 인스턴스의 구성만 변경하고 원본 데이터는 유지한다.
    /// </summary>
    public void Apply(KeyboardCustomizationState state, KeyboardCustomization keyboard)
    {
        keyboard.ApplyState(state, _materials, _ids, _keycapDefinitions);
    }
}
