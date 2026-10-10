using System;

using UnityEngine;

namespace KeyboardModeling
{
    [CreateAssetMenu(fileName = "KeycapRaritySettings", menuName = "Game/Keycap Rarity Settings")]
    public sealed class KeycapRaritySettings : ScriptableObject
    {
        [Header("Tier Stats and Appearance")]
        [SerializeField]
        private Tier[] _tiers =
        {
            new Tier(KeycapRarity.Common, 1f, 1f),
            new Tier(KeycapRarity.Rare, 1.5f, 2f),
            new Tier(KeycapRarity.Epic, 2.5f, 4f),
            new Tier(KeycapRarity.Legendary, 4f, 8f)
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
            [SerializeField] private Material _effectMaterial;

            public KeycapRarity Rarity => _rarity;
            public float HealthMultiplier => _healthMultiplier;
            public float RewardMultiplier => _rewardMultiplier;
            public Material EffectMaterial => _effectMaterial;

            /// <summary>
            /// rarity, healthMultiplier, rewardMultiplier로 등급 기본 설정을 만든다.
            /// 등급과 체력·보상 배율을 저장하며 이펙트 재질은 Inspector에서 지정한다.
            /// </summary>
            public Tier(KeycapRarity rarity, float healthMultiplier, float rewardMultiplier)
            {
                _rarity = rarity;
                _healthMultiplier = healthMultiplier;
                _rewardMultiplier = rewardMultiplier;
            }

            /// <summary>체력과 보상 배율이 유한한 1 이상의 수인지 검사하며 설정 오류를 거부한다.</summary>
            public void Validate()
            {
                if (float.IsNaN(_healthMultiplier) || float.IsInfinity(_healthMultiplier) || _healthMultiplier < 1f ||
                    float.IsNaN(_rewardMultiplier) || float.IsInfinity(_rewardMultiplier) || _rewardMultiplier < 1f)
                {
                    throw new InvalidOperationException("Keycap rarity multipliers must be finite and at least one.");
                }
                if (_rarity != KeycapRarity.Common && _effectMaterial == null)
                {
                    throw new InvalidOperationException($"Missing rarity effect material: {_rarity}.");
                }
            }
        }
    }
}
