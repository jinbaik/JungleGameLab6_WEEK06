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

    [Header("Sound")]
    [SerializeField] private AudioClip _keyDownSound;
    [SerializeField] private AudioClip _keyUpSound;
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;

    public SwitchType Type => _type;
    public Material TopHousingMaterial => _topHousingMaterial;
    public Material BottomHousingMaterial => _bottomHousingMaterial;
    public Material StemMaterial => _stemMaterial;
    public AudioClip KeyDownSound => _keyDownSound;
    public AudioClip KeyUpSound => _keyUpSound;
    public float Volume => _volume;
}
