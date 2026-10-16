using UnityEngine;

[CreateAssetMenu(fileName = "PlateData", menuName = "Keyboard/Plate Data")]
public sealed class PlateData : ScriptableObject
{
    [Header("Type")]
    [SerializeField] private PlateType _type;

    [Header("Appearance")]
    [SerializeField] private Material _material;

    public PlateType Type => _type;
    public Material Material => _material;
}
