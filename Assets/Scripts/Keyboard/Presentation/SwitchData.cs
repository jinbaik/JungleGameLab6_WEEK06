using UnityEngine;

[CreateAssetMenu(fileName = "SwitchData", menuName = "Keyboard/Switch Data")]
public sealed class SwitchData : ScriptableObject
{
    [Header("Type")]
    [SerializeField] private SwitchType _type;

    [Header("Appearance")]
    [SerializeField] private Material _topHousingMaterial;
    [SerializeField] private Material _bottomHousingMaterial;
    [SerializeField] private Material _stemMaterial;

    public SwitchType Type => _type;
    public Material TopHousingMaterial => _topHousingMaterial;
    public Material BottomHousingMaterial => _bottomHousingMaterial;
    public Material StemMaterial => _stemMaterial;
}
