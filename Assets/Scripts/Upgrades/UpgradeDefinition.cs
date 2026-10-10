using System;

using UnityEngine;

using KeyboardModeling;

namespace Game.Upgrades
{
    [CreateAssetMenu(fileName = "Upgrade", menuName = "Game/Upgrade Definition")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private UpgradeId _id;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea] private string _description;

        [Header("Progression")]
        [Tooltip("구매 가격의 개수가 최대 레벨입니다. Element 0은 0→1레벨 가격이며, 기본 0레벨용 가격은 넣지 않습니다.")]
        [SerializeField, Min(1)] private long[] _levelCosts = { 10, 25, 60 };

        [Header("Damage")]
        [Tooltip("직접 또는 광역 공격력 강화의 각 레벨에서 해당 기본 공격력에 더하는 누적 추가 피해입니다.")]
        [SerializeField, Min(0)] private int[] _damageBonuses = { 1, 2, 3 };

        [Header("Area Smash")]
        [Tooltip("범위 강화의 각 레벨에서 사용하는 반경입니다. 일반 키 간격을 1로 사용합니다.")]
        [SerializeField, Min(0f)] private float[] _areaRadii = { 1.5f, 2f, 2.5f };

        [Header("Fever Duration")]
        [Tooltip("Element 0은 기본 0레벨 지속시간입니다. 각 항목에 해당 레벨의 최종 초를 입력하며, Level Costs보다 1개 많아야 합니다.")]
        [SerializeField, Min(0.1f)] private float[] _feverDurations = Array.Empty<float>();
        [SerializeField, HideInInspector] private float _baseFeverDuration = 5f;
        [SerializeField, HideInInspector] private float _feverDurationPerLevel = 2f;

        [Header("Fever Damage Multiplier")]
        [Tooltip("Element 0은 기본 0레벨 배율입니다. 각 항목에 해당 레벨의 최종 배율을 입력하며, Level Costs보다 1개 많아야 합니다.")]
        [SerializeField, Min(1f)] private float[] _feverDamageMultipliers = Array.Empty<float>();
        [SerializeField, HideInInspector] private float _baseFeverDamageMultiplier = 2f;
        [SerializeField, HideInInspector] private float _feverDamageMultiplierPerLevel = 0.3f;

        [Header("Keycap Quality: Level 0 through Max Level")]
        [Tooltip("Element 0은 강화 전 기본 분포입니다. 최대 레벨까지 포함하므로 Level Costs보다 1개 많아야 합니다.")]
        [SerializeField] private KeycapRarityDistribution[] _rarityDistributions =
        {
            new KeycapRarityDistribution(90f, 8f, 1.8f, 0.2f),
            new KeycapRarityDistribution(85f, 10.5f, 3.75f, 0.75f),
            new KeycapRarityDistribution(80f, 12f, 6f, 2f),
            new KeycapRarityDistribution(70f, 15f, 10f, 5f)
        };

        public UpgradeId Id => _id;
        public string DisplayName => _displayName;
        public string Description => _description;
        public int MaxLevel => _levelCosts.Length;

        void OnValidate()
        {
            for (int i = 0; i < _levelCosts.Length; i++)
            {
                _levelCosts[i] = Math.Max(1, _levelCosts[i]);
            }

            if (_id == UpgradeId.SmashDamage || _id == UpgradeId.AreaDamage)
            {
                Array.Resize(ref _damageBonuses, MaxLevel);
                for (int i = 0; i < _damageBonuses.Length; i++)
                {
                    _damageBonuses[i] = Math.Max(0, _damageBonuses[i]);
                }
            }
            else if (_id == UpgradeId.AreaSmash)
            {
                Array.Resize(ref _areaRadii, MaxLevel);
                for (int i = 0; i < _areaRadii.Length; i++)
                {
                    _areaRadii[i] = Mathf.Max(0f, _areaRadii[i]);
                }
            }
            else if (_id == UpgradeId.FeverDuration)
            {
                ValidateFeverLevels(ref _feverDurations, _baseFeverDuration, _feverDurationPerLevel, 0.1f);
            }
            else if (_id == UpgradeId.FeverDamageMultiplier)
            {
                ValidateFeverLevels(ref _feverDamageMultipliers, _baseFeverDamageMultiplier, _feverDamageMultiplierPerLevel, 1f);
            }
        }

        /// <summary>
        /// values를 0레벨부터 최대 레벨까지의 효과 배열로 맞추고 minimum 이상의 값으로 보정한다.
        /// 빈 배열은 기존 baseValue와 legacyIncrement로 변환하며, 레벨 추가 시 마지막 효과를 복사하여 기존 설정을 보존한다.
        /// </summary>
        private void ValidateFeverLevels(ref float[] values, float baseValue, float legacyIncrement, float minimum)
        {
            int previousLength = values.Length;
            Array.Resize(ref values, MaxLevel + 1);
            for (int level = 0; level < values.Length; level++)
            {
                if (previousLength == 0)
                {
                    values[level] = baseValue + legacyIncrement * level;
                }
                else if (level >= previousLength)
                {
                    values[level] = values[level - 1];
                }

                values[level] = Mathf.Max(minimum, values[level]);
            }
        }

        /// <summary>
        /// currentLevel에서 다음 레벨로 강화하는 데 필요한 가격을 반환한다.
        /// currentLevel은 0 이상이며 최대 레벨보다 작아야 한다.
        /// </summary>
        public long GetCost(int currentLevel)
        {
            if (currentLevel < 0 || currentLevel >= MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(currentLevel));
            }

            return _levelCosts[currentLevel];
        }

        /// <summary>
        /// level에 설정된 누적 추가 피해를 반환한다.
        /// level은 0부터 최대 레벨까지 사용하며 미구매 상태인 0레벨에서는 0을 반환한다.
        /// </summary>
        public int GetDamageBonus(int level)
        {
            if (level < 0 || level > MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            return level == 0 ? 0 : _damageBonuses[level - 1];
        }

        /// <summary>
        /// level에 설정된 키 간격 단위의 타격 반경을 반환한다.
        /// level은 0부터 최대 레벨까지 사용하며 미구매 상태인 0레벨에서는 0을 반환한다.
        /// </summary>
        public float GetAreaRadius(int level)
        {
            if (level < 0 || level > MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            return level == 0 ? 0f : _areaRadii[level - 1];
        }

        /// <summary>
        /// level에 설정된 피버의 최종 지속시간을 초 단위로 반환한다.
        /// 0부터 최대 레벨까지의 배열을 조회하며, 변환 전 에셋은 기존 기본값과 증가량을 사용하고 상태는 변경하지 않는다.
        /// </summary>
        public float GetFeverDuration(int level)
        {
            return GetFeverValue(level, _feverDurations, _baseFeverDuration, _feverDurationPerLevel);
        }

        /// <summary>
        /// level에 설정된 피버의 최종 피해 배율을 반환한다.
        /// 0부터 최대 레벨까지의 배열을 조회하며, 변환 전 에셋은 기존 기본값과 증가량을 사용하고 상태는 변경하지 않는다.
        /// </summary>
        public float GetFeverDamageMultiplier(int level)
        {
            return GetFeverValue(level, _feverDamageMultipliers, _baseFeverDamageMultiplier, _feverDamageMultiplierPerLevel);
        }

        /// <summary>
        /// level과 values의 길이를 검증하고 해당 레벨의 최종 피버 효과를 반환한다.
        /// 빈 배열은 변환 전 baseValue와 legacyIncrement로 계산하며 잘못된 레벨표는 예외로 알린다.
        /// </summary>
        private float GetFeverValue(int level, float[] values, float baseValue, float legacyIncrement)
        {
            if (level < 0 || level > MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            if (values.Length == 0)
            {
                return baseValue + legacyIncrement * level;
            }

            if (values.Length != MaxLevel + 1)
            {
                throw new InvalidOperationException(
                    $"Upgrade '{name}' requires {MaxLevel + 1} fever values for levels 0 through {MaxLevel}, but has {values.Length}.");
            }

            return values[level];
        }

        /// <summary>
        /// level에 해당하는 일반부터 전설까지의 분포 복사본을 반환한다.
        /// 0레벨 기본 분포와 최대 레벨까지의 설정을 검증하고 원본 데이터는 변경하지 않는다.
        /// </summary>
        public KeycapRarityDistribution GetRarityDistribution(int level)
        {
            if (level < 0 || level > MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            if (_rarityDistributions.Length != MaxLevel + 1)
            {
                throw new InvalidOperationException(
                    $"Upgrade '{name}' has {_levelCosts.Length} level costs (max level {MaxLevel}) and " +
                    $"{_rarityDistributions.Length} quality distributions. Expected {MaxLevel + 1} distributions " +
                    $"for levels 0 through {MaxLevel}; Level Costs contains purchase prices, not a level-zero entry.");
            }

            return _rarityDistributions[level].Copy();
        }

        /// <summary>
        /// 모든 품질 레벨의 분포를 검증하고 상위 등급 이상 확률이 강화 후 감소하지 않는지 검사한다.
        /// 잘못된 레벨표나 확률 설정이면 게임 초기화 단계에서 예외를 발생시킨다.
        /// </summary>
        public void ValidateRarityProgression()
        {
            int tierCount = Enum.GetValues(typeof(KeycapRarity)).Length;
            double[] previous = new double[tierCount];
            for (int level = 0; level <= MaxLevel; level++)
            {
                KeycapRarityDistribution distribution = GetRarityDistribution(level);
                double cumulative = 0d;
                for (int tier = tierCount - 1; tier > 0; tier--)
                {
                    cumulative += distribution.GetProbability((KeycapRarity)tier);
                    if (level > 0 && cumulative + 0.000001d < previous[tier])
                    {
                        throw new InvalidOperationException($"Quality level {level} reduces the chance of tier {tier} or higher.");
                    }

                    previous[tier] = cumulative;
                }
            }
        }
    }
}
