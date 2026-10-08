using System.Collections.Generic;

using UnityEngine;

using Game.Economy;
using Game.Upgrades;

namespace Game.Session
{
    public sealed class GameSession : MonoBehaviour
    {
        [Header("Starting State")]
        [SerializeField, Min(0)] private long _initialBalance;
        [SerializeField] private UpgradeDefinition[] _upgradeDefinitions;

        [Header("Runtime State")]
        private Wallet _wallet;
        private UpgradeService _upgrades;

        public Wallet Wallet => _wallet;
        public UpgradeService Upgrades => _upgrades;
        public IReadOnlyList<UpgradeDefinition> UpgradeDefinitions => _upgradeDefinitions;

        void Awake()
        {
            _wallet = new Wallet(_initialBalance);
            _upgrades = new UpgradeService(_wallet, _upgradeDefinitions);
        }
    }
}
