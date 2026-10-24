using UnityEngine;

namespace Game.Tournament
{
    public enum KeycapStatusReapply { Refresh, Stack, Ignore }
    public enum KeycapHitKind { Direct, Ability }

    [CreateAssetMenu(menuName = "Keyboard/Abilities/Status Effect")]
    public sealed class KeycapStatusEffectDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _displayName;

        [Header("Lifetime")]
        [Tooltip("끄면 지속 시간은 null이며, 적용 가능한 타격을 받을 때 Hit Count를 소모합니다.")]
        [SerializeField] private bool _usesDuration = true;
        [SerializeField, Min(0.01f)] private float _duration = 5f;
        [Tooltip("시간 제한을 사용하지 않을 때 상태를 유지할 타격 횟수입니다.")]
        [SerializeField, Min(1)] private int _hitCount = 1;
        [SerializeField] private KeycapStatusReapply _reapply;
        [SerializeField, Min(1)] private int _maxStacks = 5;

        [Header("Damage")]
        [SerializeField, Min(0)] private int _bonusDamage = 2;
        [SerializeField, Min(0f)] private float _damageMultiplier = 1f;
        [SerializeField] private bool _directHitsOnly;

        [Header("Marker")]
        [SerializeField] private GameObject _markerPrefab;
        [SerializeField] private bool _attachToSurface = true;
        [SerializeField] private Vector3 _markerOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField, Min(0.01f)] private float _markerScale = 1f;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public float? Duration => _usesDuration ? Mathf.Max(0.01f, _duration) : (float?)null;
        public int HitCount => Mathf.Max(1, _hitCount);
        public KeycapStatusReapply Reapply => _reapply;
        public int MaxStacks => Mathf.Max(1, _maxStacks);
        public int BonusDamage => _bonusDamage;
        public float DamageMultiplier => _damageMultiplier;
        public bool DirectHitsOnly => _directHitsOnly;
        public bool AttachToSurface => _attachToSurface;
        public GameObject MarkerPrefab => _markerPrefab;
        public Vector3 MarkerOffset => _markerOffset;
        public float MarkerScale => _markerScale;
    }
}
