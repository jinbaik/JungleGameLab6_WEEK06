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

        public int QualityLevel { get; }
        public int Seed { get; }
        public IReadOnlyList<KeycapData> Keycaps => _keycaps;
        public KeycapRarityDistribution Distribution => _distribution.Copy();

        /// <summary>qualityLevel, seed, distribution과 keycaps의 복사본을 저장하여 배치 시 확정된 생성 결과를 구성한다.</summary>
        public KeyboardSpawnProfile(int qualityLevel, int seed, KeycapRarityDistribution distribution, List<KeycapData> keycaps)
        {
            QualityLevel = qualityLevel;
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
            public Color Color { get; }

            /// <summary>key, rarity, maxHP, reward와 color를 저장하여 객체 참조가 없는 키별 생성 결과를 구성한다.</summary>
            public KeycapData(Key key, KeycapRarity rarity, int maxHP, long reward, Color color)
            {
                if (maxHP <= 0 || reward <= 0 || !Enum.IsDefined(typeof(KeycapRarity), rarity))
                {
                    throw new ArgumentException("Keycap spawn data requires a valid rarity and positive health and reward.");
                }

                Key = key;
                Rarity = rarity;
                MaxHP = maxHP;
                Reward = reward;
                Color = color;
            }
        }
    }
}
