using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling
{
    public sealed class KeyboardSpawnProfile
    {
        private readonly ReadOnlyCollection<KeycapData> _keycaps;
        private readonly KeycapRarityDistribution _distribution;

        public int ProgressionStage { get; }
        public int Seed { get; }
        public IReadOnlyList<KeycapData> Keycaps => _keycaps;
        public KeycapRarityDistribution Distribution => _distribution.Copy();

        /// <summary>
        /// progressionStage, seed, distribution과 keycaps로 배치 결과를 보관한다.
        /// 전달받은 확률표와 키캡 목록의 복사본을 저장한다.
        /// </summary>
        public KeyboardSpawnProfile(
            int progressionStage,
            int seed,
            KeycapRarityDistribution distribution,
            List<KeycapData> keycaps)
        {
            ProgressionStage = progressionStage;
            Seed = seed;
            _distribution = distribution.Copy();
            _keycaps = new List<KeycapData>(keycaps).AsReadOnly();
        }

        public sealed class KeycapData
        {
            public Key Key { get; }
            public KeycapRarity Rarity { get; }
            public int MaxHP { get; }
            public long Reward { get; }
            public Material EffectMaterial { get; }

            /// <summary>
            /// key, rarity, maxHP, reward와 effectMaterial로 키캡 생성 결과를 만든다.
            /// 확정된 등급·체력·보상 및 이펙트 재질 참조를 저장한다.
            /// </summary>
            public KeycapData(
                Key key,
                KeycapRarity rarity,
                int maxHP,
                long reward,
                Material effectMaterial)
            {
                if (maxHP <= 0 || reward <= 0 || !Enum.IsDefined(typeof(KeycapRarity), rarity) || (rarity != KeycapRarity.Common && effectMaterial == null))
                {
                    throw new ArgumentException("Invalid keycap spawn data.");
                }

                Key = key;
                Rarity = rarity;
                MaxHP = maxHP;
                Reward = reward;
                EffectMaterial = effectMaterial;
            }
        }


    }
}
