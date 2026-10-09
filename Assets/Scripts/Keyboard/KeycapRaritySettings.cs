using System;

using UnityEngine;

namespace KeyboardModeling
{
    [CreateAssetMenu(fileName = "KeycapRaritySettings", menuName = "Game/Keycap Rarity Settings")]
    public sealed class KeycapRaritySettings : ScriptableObject
    {
        [Header("Tier Stats and Appearance")]
        [SerializeField] private Tier[] _tiers =
        {
            new Tier(KeycapRarity.Common, 1f, 1f, Color.white),
            new Tier(KeycapRarity.Rare, 1.5f, 2f, new Color(0.35f, 1f, 0.45f)),
            new Tier(KeycapRarity.Epic, 2.5f, 4f, new Color(0.8f, 0.4f, 1f)),
            new Tier(KeycapRarity.Legendary, 4f, 8f, new Color(1f, 0.7f, 0.15f))
        };

        /// <summary>rarity에 대응하는 체력·보상 배율과 표시 색상을 반환하며 설정을 변경하지 않는다.</summary>
        public Tier GetTier(KeycapRarity rarity)
        {
            for (int index = 0; index < _tiers.Length; index++)
            {
                if (_tiers[index].Rarity == rarity)
                {
                    return _tiers[index];
                }
            }

            throw new InvalidOperationException($"Missing keycap rarity settings: {rarity}.");
        }

        /// <summary>등급 정의의 중복·누락과 배율을 검사하고 잘못된 설정이면 생성 전에 예외를 발생시킨다.</summary>
        public void Validate()
        {
            int count = Enum.GetValues(typeof(KeycapRarity)).Length;
            bool[] found = new bool[count];
            foreach (Tier tier in _tiers)
            {
                int index = (int)tier.Rarity;
                if (index < 0 || index >= count || found[index])
                {
                    throw new InvalidOperationException("Keycap rarity settings contain an invalid or duplicate tier.");
                }

                found[index] = true;
                tier.Validate();
            }

            foreach (bool exists in found)
            {
                if (!exists)
                {
                    throw new InvalidOperationException("Keycap rarity settings must define every tier.");
                }
            }
        }

        [Serializable]
        public sealed class Tier
        {
            [Header("Rarity")]
            [SerializeField] private KeycapRarity _rarity;
            [SerializeField, Min(1f)] private float _healthMultiplier;
            [SerializeField, Min(1f)] private float _rewardMultiplier;
            [SerializeField] private Color _color;

            public KeycapRarity Rarity => _rarity;
            public float HealthMultiplier => _healthMultiplier;
            public float RewardMultiplier => _rewardMultiplier;
            public Color Color => _color;

            /// <summary>rarity, healthMultiplier, rewardMultiplier와 color를 저장하여 등급 설정을 구성한다.</summary>
            public Tier(KeycapRarity rarity, float healthMultiplier, float rewardMultiplier, Color color)
            {
                _rarity = rarity;
                _healthMultiplier = healthMultiplier;
                _rewardMultiplier = rewardMultiplier;
                _color = color;
            }

            /// <summary>체력과 보상 배율이 유한한 1 이상의 수인지 검사하며 설정 오류를 거부한다.</summary>
            public void Validate()
            {
                if (float.IsNaN(_healthMultiplier) || float.IsInfinity(_healthMultiplier) || _healthMultiplier < 1f ||
                    float.IsNaN(_rewardMultiplier) || float.IsInfinity(_rewardMultiplier) || _rewardMultiplier < 1f)
                {
                    throw new InvalidOperationException("Keycap rarity multipliers must be finite and at least one.");
                }
            }
        }
    }
}
