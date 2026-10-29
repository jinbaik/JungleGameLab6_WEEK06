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
            new Tier(KeycapRarity.Tier1, 1f, 1f),
            new Tier(KeycapRarity.Tier2, 1.2f, 2f),
            new Tier(KeycapRarity.Tier3, 1.6f, 3f),
            new Tier(KeycapRarity.Tier4, 2f, 4.5f),
            new Tier(KeycapRarity.Tier5, 2.6f, 6.5f),
            new Tier(KeycapRarity.Tier6, 3.4f, 9f),
            new Tier(KeycapRarity.Tier7, 4.4f, 13f),
            new Tier(KeycapRarity.Tier8, 6f, 20f)
        };

        /// <summary>
        /// rarity에 대응하는 체력·보상 배율과 외피 머티리얼 설정을 반환한다.
        /// 설정은 변경하지 않는다.
        /// </summary>
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
            [SerializeField] private Material _shellMaterial;
            [Header("Neon Appearance")]
            [SerializeField] private Mesh _shellMesh;
            [SerializeField] private Material _bandMaterial;


            [Header("Audio")]
            [SerializeField] private AudioClip _pressSound;
            [SerializeField] private AudioClip _breakSound;

            public AudioClip PressSound => _pressSound;
            public AudioClip BreakSound => _breakSound;
            public KeycapRarity Rarity => _rarity;
            public float HealthMultiplier => _healthMultiplier;
            public float RewardMultiplier => _rewardMultiplier;
            public Material ShellMaterial => _shellMaterial;
            public Mesh ShellMesh => _shellMesh;
            public Material BandMaterial => _bandMaterial;


            /// <summary>
            /// rarity, healthMultiplier, rewardMultiplier로 등급 기본 설정을 만든다.
            /// 등급과 배율을 저장하며 외피 머티리얼은 Inspector에서 지정한다.
            /// </summary>
            public Tier(KeycapRarity rarity, float healthMultiplier, float rewardMultiplier)
            {
                _rarity = rarity;
                _healthMultiplier = healthMultiplier;
                _rewardMultiplier = rewardMultiplier;
            }

            /// <summary>
            /// 체력·보상 배율과 외피 재질을 검사한다.
            /// 현재 등급의 설정이 잘못되면 예외를 발생시킨다.
            /// </summary>
            public void Validate()
            {
                if (float.IsNaN(_healthMultiplier) || float.IsInfinity(_healthMultiplier) || _healthMultiplier < 1f ||
                    float.IsNaN(_rewardMultiplier) || float.IsInfinity(_rewardMultiplier) || _rewardMultiplier < 1f)
                {
                    throw new InvalidOperationException("Keycap rarity multipliers must be finite and at least one.");
                }

                if (_rarity == KeycapRarity.Tier7 && (_shellMesh == null || _bandMaterial == null))
                {
                    throw new InvalidOperationException("Tier7 requires a Neon shell mesh and band material.");
                }

                if (_shellMaterial == null)
                {
                    throw new InvalidOperationException($"Missing keycap shell material: {_rarity}.");
                }
            }
        }
    }
}
