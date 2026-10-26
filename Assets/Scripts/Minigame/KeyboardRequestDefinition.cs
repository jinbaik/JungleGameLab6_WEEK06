using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(fileName = "KeyboardRequest", menuName = "Keyboard/Monitor/Keyboard Request")]
    public sealed class KeyboardRequestDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _requestId;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea(3, 8)] private string _description;

        [Header("Requirements")]
        [SerializeField] private SwitchType _requiredSwitchType = SwitchType.Red;
        [SerializeField] private bool _requiresAllRequiredSwitchSlots = true;
        [SerializeField] private bool _requiresCompleteAssembly = true;

        [Header("Glass Surfaces")]
        [SerializeField] private bool _requiresGlassSurfaces;
        [SerializeField] private Material _glassMaterial;
        [SerializeField] private KeyboardMaterialCatalog _materialCatalog;

        [Header("Reward")]
        [SerializeField, Min(0)] private int _rewardGold = 100;

        [Header("Mail")]
        [SerializeField] private MonitorMailMessage _initialMail;
        [SerializeField] private MonitorMailMessage _successMail;
        [SerializeField] private MonitorMailMessage _failureMail;
        [SerializeField] private KeyboardRequestDefinition _nextRequest;

        public string RequestId => _requestId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public SwitchType RequiredSwitchType => _requiredSwitchType;
        public bool RequiresAllRequiredSwitchSlots => _requiresAllRequiredSwitchSlots;
        public bool RequiresCompleteAssembly => _requiresCompleteAssembly;
        public bool RequiresGlassSurfaces => _requiresGlassSurfaces;
        public Material GlassMaterial => _glassMaterial;
        public KeyboardMaterialCatalog MaterialCatalog => _materialCatalog;
        public int RewardGold => _rewardGold;
        public MonitorMailMessage InitialMail => _initialMail;
        public MonitorMailMessage SuccessMail => _successMail;
        public MonitorMailMessage FailureMail => _failureMail;
        public KeyboardRequestDefinition NextRequest => _nextRequest;
    }
}
