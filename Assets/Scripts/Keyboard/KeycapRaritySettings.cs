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
            [SerializeField] private PrefabVariant[] _prefabVariants = Array.Empty<PrefabVariant>();

            public KeycapRarity Rarity => _rarity;
            public float HealthMultiplier => _healthMultiplier;
            public float RewardMultiplier => _rewardMultiplier;
            public Material EffectMaterial => _effectMaterial;

            /// <summary>
            /// sourceMesh와 같은 크기 및 형상을 사용하는 등급별 키캡 프리팹을 조회한다.
            /// 교체 설정이 없는 등급은 null을 반환하고 설정된 등급의 형상이 누락되면 오류를 알린다.
            /// </summary>
            public GameObject GetPrefab(Mesh sourceMesh)
            {
                if (_prefabVariants.Length == 0)
                    return null;

                foreach (PrefabVariant variant in _prefabVariants)
                {
                    if (variant.SourceMesh == sourceMesh)
                        return variant.Prefab;
                }
                throw new InvalidOperationException($"Missing {_rarity} keycap prefab for {sourceMesh.name}.");
            }

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
                foreach (PrefabVariant variant in _prefabVariants)
                    variant.Validate();
            }
        }

        [Serializable]
        private sealed class PrefabVariant
        {
            [Header("Matching Shape")]
            [SerializeField] private Mesh _sourceMesh;
            [SerializeField] private GameObject _prefab;
            public Mesh SourceMesh => _sourceMesh;
            public GameObject Prefab => _prefab;

            /// <summary>
            /// 원본 메시와 교체 프리팹의 체력 및 버튼 설정을 확인한다.
            /// 참조 또는 필수 컴포넌트가 없으면 배치 전에 설정 오류를 발생시킨다.
            /// </summary>
            public void Validate()
            {
                if (_sourceMesh == null || _prefab == null || _prefab.GetComponent<KeycapHealth>() == null
                    || _prefab.GetComponent<KeycapButton>() == null)
                    throw new InvalidOperationException("Keycap prefab variants require a source mesh, health and button.");
            }
        }
    }
}
