using System.Collections.Generic;

using UnityEngine;

using Game.Economy;
using Game.Settings;
using Game.Upgrades;
using KeyboardModeling;

namespace Game.Session
{
    public sealed class GameSession : MonoBehaviour
    {
        private static Wallet _workshopWallet;

        [Header("Starting State")]
        [SerializeField, Min(0)] private long _initialBalance;
        [SerializeField] private UpgradeDefinition[] _upgradeDefinitions;
        [SerializeField] private bool _keepWalletForRun;

        [Header("Runtime State")]
        private Wallet _wallet;
        private UpgradeService _upgrades;
        private GameSettings _settings;

        [Header("Rarity Progression")]
        [SerializeField] private KeycapRarityProgression _rarityProgression;

        [Header("Runtime State")]
        private int _completedKeyboardCount;

        public Wallet Wallet => _wallet;
        public GameSettings Settings => _settings ??= new GameSettings();
        public UpgradeService Upgrades => _upgrades;
        public IReadOnlyList<UpgradeDefinition> UpgradeDefinitions => _upgradeDefinitions;
        public int CompletedKeyboardCount => _completedKeyboardCount;
        public int CurrentRarityStage => _rarityProgression.GetStage(_completedKeyboardCount);

        void Awake()
        {
            _rarityProgression.Validate();

            _wallet = _keepWalletForRun ? _workshopWallet ??= new Wallet(_initialBalance) : new Wallet(_initialBalance);
            _upgrades = new UpgradeService(_wallet, _upgradeDefinitions);
        }

        /// <summary>
        /// 새 게임 실행 시 작업실에서 공유한 지갑 참조를 초기화한다.
        /// 입력과 반환값 없이 이전 실행의 잔액을 제거하며 원본 세션 설정은 변경하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWorkshopWallet()
        {
            _workshopWallet = null;
        }

        void OnDestroy()
        {
            _settings?.Dispose();
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
