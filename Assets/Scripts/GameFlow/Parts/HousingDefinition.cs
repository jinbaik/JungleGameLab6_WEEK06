using UnityEngine;

namespace Game.Tournament
{
    [CreateAssetMenu(menuName = "Keyboard/Parts/Housing")]
    public sealed class HousingDefinition : KeyboardPartDefinition
    {
        [Header("Part")]
        [SerializeField] private Material _material;
        [SerializeField] private string _materialId;
        [SerializeField, Min(0)] private int _damageBonus = 1;
        public Material Material => _material;
        public string MaterialId => _materialId;
        public int DamageBonus => _damageBonus;
        public override string Description => $"직접 타격 대미지 +{_damageBonus} × 단계";
    }
}
