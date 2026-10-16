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
        /// 상자 강화의 현재 구매 레벨에 대응하는 최종 용량을 반환한다.
        /// 강화 정의가 없는 세션에서는 fallbackCapacity를 반환하며 구매 상태는 변경하지 않는다.
        /// </summary>
        public int GetBoxCapacity(int fallbackCapacity)
        {
            return _definitions.TryGetValue(UpgradeId.BoxCapacity, out UpgradeDefinition definition)
                ? definition.GetBoxCapacity(GetLevel(UpgradeId.BoxCapacity)) : fallbackCapacity;
        }

        /// <summary>
        /// 현재 손 강화 또는 기존 SmashDamage 레벨로 기본 손 공격의 추가 피해를 조회한다.
        /// 주먹 피해를 제외한 보너스를 반환하며 강화 레벨과 지갑 상태는 변경하지 않는다.
        /// </summary>
        public int GetDamageBonus()
        {
            if (_definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand))
                return hand.GetHandDamageBonus(GetLevel(UpgradeId.HandPower));
            return _definitions[UpgradeId.SmashDamage].GetDamageBonus(_levels[UpgradeId.SmashDamage]);
        }

        /// <summary>
        /// HandPower 정의와 구매 레벨로 주먹 추가 타격 확률을 조회한다.
        /// 미등록 또는 주먹 승급 전에는 0을 반환하며 최대 확률은 30%이다.
        /// </summary>
        public float GetFistChance()
        {
            return _definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand)
                ? hand.GetFistChance(GetLevel(UpgradeId.HandPower)) : 0f;
        }

        /// <summary>
        /// HandPower 정의와 구매 레벨로 기본 손 피해와 분리된 주먹 추가 피해를 조회한다.
        /// 미등록 또는 주먹 승급 전에는 0을 반환하며 구매 상태는 변경하지 않는다.
        /// </summary>
        public int GetFistDamage()
        {
            return _definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand)
                ? hand.GetFistDamage(GetLevel(UpgradeId.HandPower)) : 0;
        }

        /// <summary>
        /// 현재 HandPower 정의와 구매 레벨로 망치 승급 여부를 반환한다.
        /// 강화 정의가 없는 세션에서는 false를 반환하며 상태는 변경하지 않는다.
        /// </summary>
        public bool IsHammerUnlocked()
        {
            return _definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand)
                && hand.IsHammerUnlocked(GetLevel(UpgradeId.HandPower));
        }

        /// <summary>
        /// 현재 HandPower 레벨에서 망치의 독립 발동 확률을 조회한다.
        /// 정의가 없거나 망치 승급 전이면 0을 반환하며 주먹 확률은 변경하지 않는다.
        /// </summary>
        public float GetHammerChance()
        {
            return _definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand)
                ? hand.GetHammerChance(GetLevel(UpgradeId.HandPower)) : 0f;
        }

        /// <summary>
        /// 현재 HandPower 레벨에서 손·주먹과 분리된 망치 추가 피해를 조회한다.
        /// 정의가 없거나 망치 승급 전이면 0을 반환하며 구매 상태는 변경하지 않는다.
        /// </summary>
        public int GetHammerDamage()
        {
            return _definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand)
                ? hand.GetHammerDamage(GetLevel(UpgradeId.HandPower)) : 0;
        }

        /// <summary>
        /// LuckyReward 구매 레벨과 레벨당 추가 확률로 두 배 지급의 누적 확률을 조회한다.
        /// 미등록 또는 미구매 상태는 0을 반환하며 재화와 강화 레벨은 변경하지 않는다.
        /// </summary>
        public float GetDoubleRewardChance()
        {
            return _definitions.TryGetValue(UpgradeId.LuckyReward, out UpgradeDefinition definition) && GetLevel(UpgradeId.LuckyReward) > 0
                ? definition.GetDoubleRewardChance(GetLevel(UpgradeId.LuckyReward)) : 0f;
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
        /// 현재 자동공격 구매 레벨에서 승급 반경을 조회한다.
        /// AutoClick 정의와 레벨을 사용하며 미등록 또는 승급 전에는 0을 반환한다.
        /// </summary>
        public float GetAutoAttackRadius()
        {
            return _definitions.TryGetValue(UpgradeId.AutoClick, out UpgradeDefinition definition)
                ? definition.GetAutoAttackRadius(GetLevel(UpgradeId.AutoClick)) : 0f;
        }

        /// <summary>
        /// 현재 Auto Click 레벨에서 자동 보충이 해금되었는지 확인한다.
        /// 등록된 정의와 구매 레벨을 사용하며 해금 여부를 반환한다.
        /// </summary>
        public bool IsAutoRefillUnlocked()
        {
            return _definitions.TryGetValue(UpgradeId.AutoClick, out UpgradeDefinition definition)
                && definition.IsAutoRefillUnlocked(GetLevel(UpgradeId.AutoClick));
        }

        /// <summary>
        /// 현재 Auto Click 레벨에 설정된 자동 보충 대기 시간을 조회한다.
        /// 미해금 상태에서는 0을 반환하며 강화 상태는 변경하지 않는다.
        /// </summary>
        public float GetAutoRefillDelay()
        {
            if (!_definitions.TryGetValue(UpgradeId.AutoClick, out UpgradeDefinition definition))
                return 0f;

            int level = GetLevel(UpgradeId.AutoClick);
            return definition.IsAutoRefillUnlocked(level)
                ? definition.GetAutoRefillDelay(level) : 0f;
        }

        /// <summary>
        /// 현재 AreaDamage 레벨과 강화 데이터로 광역 공격의 최종 피해를 조회한다.
        /// 광역 공격력 정의가 없는 기존 세션에서는 0을 반환하며 강화 레벨과 지갑은 변경하지 않는다.
        /// </summary>
        public int GetAreaDamageBonus()
        {
            if (_definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand))
                return hand.GetHandDamageBonus(GetLevel(UpgradeId.HandPower));
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
            if (_definitions.TryGetValue(UpgradeId.HandPower, out UpgradeDefinition hand))
                return hand.GetHandRadius(GetLevel(UpgradeId.HandPower));
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
                : new KeycapRarityDistribution(100f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
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
