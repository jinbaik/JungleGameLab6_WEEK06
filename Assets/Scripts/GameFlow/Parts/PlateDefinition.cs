using UnityEngine;

namespace Game.Tournament
{
    [CreateAssetMenu(menuName = "Keyboard/Parts/Plate")]
    public sealed class PlateDefinition : KeyboardPartDefinition
    {
        [Header("Part")]
        [SerializeField] private PlateData _data;
        [SerializeField] private string _materialId;
        [SerializeField, Min(0)] private int _damageBonus = 1;
        public PlateData Data => _data;
        public string MaterialId => _materialId;
        public int DamageBonus => _damageBonus;
        public override string Description => $"직접 타격 대미지 +{_damageBonus} × 단계";
    }
}
