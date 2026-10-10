using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling
{
    public static class KeyboardProfileGenerator
    {
        /// <summary>
        /// layout, eligibleKeys, distribution, settings, baseReward와 seed로 키별 등급·체력·보상을 계산한다.
        /// 희귀 등급의 형상별 프리팹을 먼저 검사하고 progressionStage를 포함한 프로필을 반환하며 제외 키는 일반 등급으로 유지한다.
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

            foreach (KeycapHealth health in layout.Values)
            {
                if (!eligibleKeys.Contains(health))
                    continue;

                Mesh shape = health.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>().sharedMesh;
                foreach (KeycapRarity rarity in Enum.GetValues(typeof(KeycapRarity)))
                {
                    if (rarity != KeycapRarity.Common)
                        settings.GetTier(rarity).GetPrefab(shape);
                }
            }

            System.Random random = new System.Random(seed);
            List<Key> keys = new List<Key>(layout.Keys);
            keys.Sort();
            List<KeyboardSpawnProfile.KeycapData> data = new List<KeyboardSpawnProfile.KeycapData>(keys.Count);
            foreach (Key key in keys)
            {
                KeycapHealth health = layout[key];
                KeycapRarity rarity = eligibleKeys.Contains(health) ? distribution.Roll(random) : KeycapRarity.Common;
                KeycapRaritySettings.Tier tier = settings.GetTier(rarity);
                int maxHP = checked((int)Math.Ceiling(health.MaxHP * (double)tier.HealthMultiplier));
                long reward = checked((long)Math.Ceiling(baseReward * (double)tier.RewardMultiplier));
                Mesh shape = health.transform.Find("PBT_SculptedShell").GetComponent<MeshFilter>().sharedMesh;
                data.Add(new KeyboardSpawnProfile.KeycapData(key, rarity, maxHP, reward, tier.GetPrefab(shape)));

            }
            return new KeyboardSpawnProfile(progressionStage, seed, distribution, data);
        }
    }
}
