using UnityEngine;

namespace Game.Tournament
{
    [CreateAssetMenu(menuName = "Keyboard/Parts/Keycap")]
    public sealed class KeycapDefinition : KeyboardPartDefinition
    {
        [Header("Appearance")]
        [SerializeField] private Material _material;
        [SerializeField] private string _materialId;
        [SerializeField] private Mesh _shellMesh;
        [SerializeField] private Material[] _modelMaterials = new Material[0];
        [SerializeField] private bool _oneUnitOnly;
        [Header("Ability")]
        [SerializeField] private KeycapAbilityDefinition _ability;
        public Material Material => _material;
        public string MaterialId => _materialId;
        public Mesh ShellMesh => _shellMesh;
        public Material[] ModelMaterials => _modelMaterials;
        public bool IsArtisan => _shellMesh != null;
        public bool OneUnitOnly => _oneUnitOnly;
        public KeycapAbilityDefinition Ability => _ability;
        public override string Description => IsArtisan && _ability != null
            ? _ability.PassiveDescription
            : _ability != null ? _ability.Description + " · 효과 수치 × 단계" : "추가 능력 없음";
    }
}
