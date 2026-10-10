using System;
using System.Collections.Generic;

using UnityEngine;

using Game.Economy;
using KeyboardModeling;

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
                if (definition.Id == UpgradeId.RareKeycapQuality)
                {
                    definition.ValidateRarityProgression();
                }

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
        /// 현재 SmashDamage 레벨과 강화 데이터로 최종 피해를 조회한다.
        /// 계산한 추가 피해를 반환하며 강화 레벨과 지갑 상태는 변경하지 않는다.
        /// </summary>
        public int GetDamageBonus()
        {
            return _definitions[UpgradeId.SmashDamage].GetDamageBonus(_levels[UpgradeId.SmashDamage]);
        }

        /// <summary>
        /// 현재 AutoClick 레벨의 최종 피해를 조회한다.
        /// 기존 업그레이드 정의와 레벨을 사용하며 정의가 없으면 0을 반환한다.
        /// </summary>
        public int GetAutoAttackDamage()
        {
            return _definitions.TryGetValue(UpgradeId.AutoClick, out UpgradeDefinition definition)
                ? definition.GetAutoAttackDamage(GetLevel(UpgradeId.AutoClick)) : 0;
        }

        /// <summary>
        /// 현재 AutoClick 레벨의 최종 초당 공격 횟수를 조회한다.
        /// 기존 업그레이드 정의와 레벨을 사용하며 정의가 없으면 0을 반환한다.
        /// </summary>
        public float GetAutoAttackSpeed()
        {
            return _definitions.TryGetValue(UpgradeId.AutoClick, out UpgradeDefinition definition)
                ? definition.GetAutoAttackSpeed(GetLevel(UpgradeId.AutoClick)) : 0f;
        }

        /// <summary>
        /// 현재 AreaDamage 레벨과 강화 데이터로 광역 공격의 최종 피해를 조회한다.
        /// 광역 공격력 정의가 없는 기존 세션에서는 0을 반환하며 강화 레벨과 지갑은 변경하지 않는다.
        /// </summary>
        public int GetAreaDamageBonus()
        {
            if (!_definitions.TryGetValue(UpgradeId.AreaDamage, out UpgradeDefinition definition))
            {
                return 0;
            }

            return definition.GetDamageBonus(_levels[UpgradeId.AreaDamage]);
        }

        /// <summary>
        /// 현재 AreaSmash 레벨과 강화 데이터로 키 간격 단위의 타격 반경을 조회한다.
        /// 미구매 상태에서는 0을 반환하며 강화 레벨과 지갑 상태는 변경하지 않는다.
        /// </summary>
        public float GetAreaRadius()
        {
            return _definitions[UpgradeId.AreaSmash].GetAreaRadius(_levels[UpgradeId.AreaSmash]);
        }

        /// <summary>
        /// 현재 피버 지속시간 강화 레벨과 정의로 다음 피버의 전체 지속시간을 조회한다.
        /// 정의가 없는 기존 세션에서는 기본 5초를 반환하며 레벨과 지갑은 변경하지 않는다.
        /// </summary>
        public float GetFeverDuration()
        {
            return _definitions.TryGetValue(UpgradeId.FeverDuration, out UpgradeDefinition definition)
                ? definition.GetFeverDuration(_levels[UpgradeId.FeverDuration])
                : 5f;
        }

        /// <summary>
        /// 현재 피버 피해 배율 강화 레벨과 정의로 다음 피버의 최종 배율을 조회한다.
        /// 정의가 없는 기존 세션에서는 기본 2배를 반환하며 레벨과 지갑은 변경하지 않는다.
        /// </summary>
        public float GetFeverDamageMultiplier()
        {
            return _definitions.TryGetValue(UpgradeId.FeverDamageMultiplier, out UpgradeDefinition definition)
                ? definition.GetFeverDamageMultiplier(_levels[UpgradeId.FeverDamageMultiplier])
                : 2f;
        }

        /// <summary>현재 품질 강화 레벨을 반환하며 품질 강화가 없는 기존 세션에서는 0을 반환한다.</summary>
        public int GetKeycapQualityLevel()
        {
            return _levels.TryGetValue(UpgradeId.RareKeycapQuality, out int level) ? level : 0;
        }

        /// <summary>
        /// 현재 품질 강화 레벨의 일반부터 전설까지의 분포를 반환한다.
        /// 품질 정의가 없는 기존 세션에서는 일반만 등장하는 분포를 반환하며 구매 상태는 변경하지 않는다.
        /// </summary>
        public KeycapRarityDistribution GetRarityDistribution()
        {
            return _definitions.TryGetValue(UpgradeId.RareKeycapQuality, out UpgradeDefinition definition)
                ? definition.GetRarityDistribution(GetKeycapQualityLevel())
                : new KeycapRarityDistribution(100f, 0f, 0f, 0f);
        }

        /// <summary>
        /// id의 현재 구매 조건을 평가한다.
        /// id와 강화 레벨·잔액을 사용하며 상태를 변경하지 않고 구매 결과를 반환한다.
        /// </summary>
        public PurchaseResult GetPurchaseStatus(UpgradeId id)
        {
            return EvaluatePurchase(id);
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
        /// id의 등록 여부, 최대 레벨, 선행 강화와 잔액을 확인한다.
        /// 현재 상태를 변경하지 않고 구매 가능 여부를 PurchaseResult로 반환한다.
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

            if (id == UpgradeId.AreaDamage &&
                (!_levels.TryGetValue(UpgradeId.AreaSmash, out int areaSmashLevel) || areaSmashLevel < 1))
            {
                return PurchaseResult.RequiresAreaSmash;
            }

            return _wallet.Balance >= definition.GetCost(currentLevel) ? PurchaseResult.Success : PurchaseResult.InsufficientCurrency;
        }
    }
}
