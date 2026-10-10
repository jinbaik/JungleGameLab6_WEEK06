using System.Collections.Generic;
using Game.Economy;
using Game.Upgrades;
using KeyboardModeling;
using UnityEngine;

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

        [Header("Rarity Progression")]
        [SerializeField] private KeycapRarityProgression _rarityProgression;

        [Header("Runtime State")]
        private int _completedKeyboardCount;

        public Wallet Wallet => _wallet;
        public UpgradeService Upgrades => _upgrades;
        public IReadOnlyList<UpgradeDefinition> UpgradeDefinitions => _upgradeDefinitions;
        public int CompletedKeyboardCount => _completedKeyboardCount;
        public int CurrentRarityStage => _rarityProgression.GetStage(_completedKeyboardCount);

        void Awake()
        {
            _rarityProgression.Validate();

            _wallet = new Wallet(_initialBalance);
            _upgrades = new UpgradeService(_wallet, _upgradeDefinitions);
        }

        /// <summary>
        /// 현재 완료 대수에 대응하는 키캡 등급 확률표를 반환한다.
        /// _completedKeyboardCount를 사용하며 진행 상태를 변경하지 않는다.
        /// </summary>
        public KeycapRarityDistribution GetCurrentRarityDistribution()
        {
            return _rarityProgression.GetDistribution(_completedKeyboardCount);
        }

        /// <summary>
        /// 키보드 한 대의 파괴 연출 완료를 기록한다.
        /// 완료 대수를 증가시켜 다음 배치부터 새로운 등급 구간을 사용하게 한다.
        /// </summary>
        public void CompleteKeyboard()
        {
            _completedKeyboardCount++;
        }
    }
}
