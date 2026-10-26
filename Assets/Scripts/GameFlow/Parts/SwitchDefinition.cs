using UnityEngine;

namespace Game.Tournament
{
    [CreateAssetMenu(menuName = "Keyboard/Parts/Switch")]
    public sealed class SwitchDefinition : KeyboardPartDefinition
    {
        [Header("Part")]
        [SerializeField] private SwitchData _data;
        [SerializeField, Min(0)] private int _damageBonus = 1;
        public SwitchData Data => _data;
        public int DamageBonus => _damageBonus;
        public override string Description => $"직접 타격 대미지 +{_damageBonus} × 단계";
    }
}
