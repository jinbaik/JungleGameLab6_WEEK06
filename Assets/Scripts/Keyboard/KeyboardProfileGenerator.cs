using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling
{
    public static class KeyboardProfileGenerator
    {
        /// <summary>
        /// layout의 키캡 중 등급을 지원하고 eligibleKeys에 포함된 키의 등급을 distribution으로 추첨한다.
        /// settings와 baseReward로 체력·보상·외피 재질을 계산하고 progressionStage와 seed가 담긴 프로필을 반환한다.
        /// </summary>
        public static KeyboardSpawnProfile GenerateProfile(
            IReadOnlyDictionary<Key, KeycapHealth> layout,
            HashSet<KeycapHealth> eligibleKeys,
            KeycapRarityDistribution distribution,
            KeycapRaritySettings settings,
            long baseReward,
            int progressionStage,
            int seed
            )
        {
            distribution.Validate();
            settings.Validate();
            if (baseReward <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseReward));
            }            

            System.Random random = new System.Random(seed);
            List<Key> keys = new List<Key>(layout.Keys);
            keys.Sort();
            List<KeyboardSpawnProfile.KeycapData> data = new List<KeyboardSpawnProfile.KeycapData>(keys.Count);
            foreach (Key key in keys)
            {
                KeycapHealth health = layout[key];
                bool canRollRarity = health.SupportsRarity && eligibleKeys.Contains(health);
                KeycapRarity rarity = canRollRarity ? distribution.Roll(random) : KeycapRarity.Common;
                KeycapRaritySettings.Tier tier = settings.GetTier(rarity);
                int maxHP = checked((int)Math.Ceiling(health.MaxHP * (double)tier.HealthMultiplier));
                long reward = checked((long)Math.Ceiling(baseReward * (double)tier.RewardMultiplier));
                data.Add(new KeyboardSpawnProfile.KeycapData(key, rarity, maxHP, reward, tier.ShellMaterial));

            }
            return new KeyboardSpawnProfile(progressionStage, seed, distribution, data);
        }
    }
}
