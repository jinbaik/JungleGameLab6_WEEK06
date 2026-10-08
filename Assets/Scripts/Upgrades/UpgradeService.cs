using System;
using System.Collections.Generic;

using UnityEngine;

using Game.Economy;

namespace Game.Upgrades
{
    public sealed class UpgradeService
    {
        [Header("Dependencies")]
        private readonly Wallet _wallet;
        private readonly Dictionary<UpgradeId, UpgradeDefinition> _definitions = new Dictionary<UpgradeId, UpgradeDefinition>();

        [Header("Runtime State")]
        private readonly Dictionary<UpgradeId, int> _levels = new Dictionary<UpgradeId, int>();

        public event Action<UpgradeId, int> LevelChanged;

        /// <summary>
        /// wallet과 definitions를 사용하여 강화 구매 서비스를 생성한다.
        /// 등록된 모든 강화의 현재 레벨을 0으로 초기화한다.
        /// </summary>
        public UpgradeService(Wallet wallet, IReadOnlyList<UpgradeDefinition> definitions)
        {
            _wallet = wallet;

            for (int i = 0; i < definitions.Count; i++)
            {
                UpgradeDefinition definition = definitions[i];
                _definitions.Add(definition.Id, definition);
                _levels.Add(definition.Id, 0);
            }
        }

        /// <summary>
        /// id로 등록된 강화의 현재 레벨을 조회한다.
        /// 보관 중인 레벨을 반환하며 구매 상태를 변경하지 않는다.
        /// </summary>
        public int GetLevel(UpgradeId id)
        {
            return _levels[id];
        }

        /// <summary>
        /// id의 등록 여부, 최대 레벨, 현재 잔액을 확인한다.
        /// 다음 레벨을 구매할 수 있으면 true를 반환하며 상태는 변경하지 않는다.
        /// </summary>
        public bool CanPurchase(UpgradeId id)
        {
            return EvaluatePurchase(id) == PurchaseResult.Success;
        }

        /// <summary>
        /// id의 구매 조건을 다시 확인한 뒤 비용을 차감하고 레벨을 1 올린다.
        /// 처리 결과를 반환하며 성공한 경우 갱신된 잔액과 레벨을 알린다.
        /// </summary>
        public PurchaseResult TryPurchase(UpgradeId id)
        {
            PurchaseResult result = EvaluatePurchase(id);

            if (result != PurchaseResult.Success)
            {
                return result;
            }

            int currentLevel = _levels[id];
            long cost = _definitions[id].GetCost(currentLevel);

            if (!_wallet.TrySpend(cost))
            {
                return PurchaseResult.InsufficientCurrency;
            }

            int nextLevel = currentLevel + 1;
            _levels[id] = nextLevel;
            _wallet.NotifyBalanceChanged();
            LevelChanged?.Invoke(id, nextLevel);
            return PurchaseResult.Success;
        }

        /// <summary>
        /// id의 등록 여부와 현재 레벨 및 잔액을 사용하여 구매 조건을 판단한다.
        /// 성공 가능 여부 또는 구매할 수 없는 이유를 PurchaseResult로 반환한다.
        /// </summary>
        private PurchaseResult EvaluatePurchase(UpgradeId id)
        {
            if (!_definitions.TryGetValue(id, out UpgradeDefinition definition))
            {
                return PurchaseResult.UnknownUpgrade;
            }

            int currentLevel = _levels[id];

            if (currentLevel >= definition.MaxLevel)
            {
                return PurchaseResult.MaxLevel;
            }

            return _wallet.Balance >= definition.GetCost(currentLevel)
                ? PurchaseResult.Success
                : PurchaseResult.InsufficientCurrency;
        }
    }
}
