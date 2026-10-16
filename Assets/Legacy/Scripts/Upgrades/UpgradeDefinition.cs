using System;

using UnityEngine;

using KeyboardModeling;

namespace Game.Upgrades
{
    [CreateAssetMenu(fileName = "Upgrade", menuName = "Game/Upgrade Definition")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        private const float MAX_FIST_CHANCE = 0.3f;

        [Header("Identity")]
        [SerializeField] private UpgradeId _id;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea] private string _description;

        [Header("Progression")]
        [Tooltip("구매 가격의 개수가 최대 레벨입니다. Element 0은 0→1레벨 가격이며, 기본 0레벨용 가격은 넣지 않습니다.")]
        [SerializeField, Min(1)] private long[] _levelCosts = { 10, 25, 60 };

        [Header("Box Capacity")]
        [Tooltip("Element 0은 기본 용량입니다. 최대 레벨까지의 최종 보관 개수를 입력하며 가격보다 한 항목 많아야 합니다.")]
        [SerializeField, Min(1)] private int[] _boxCapacities = { 5, 10, 15, 20, 25, 30 };

        [Header("Lucky Reward")]
        [Tooltip("강화 1레벨마다 더해지는 두 배 보상 확률입니다. 0.1이면 1레벨 10%, 2레벨 20%이며 최대 100%입니다.")]
        [SerializeField, Range(0f, 1f)] private float _doubleRewardChance = 0.1f;

        [Header("Hand Power")]
        [Tooltip("주먹 승급 전 손 강화 한 번마다 기본 손 공격에 더하는 피해입니다.")]
        [SerializeField, Min(0)] private int _handDamagePerLevel = 1;
        [Tooltip("범위 증가와 이름 변경이 발생하는 구매 레벨입니다.")]
        [SerializeField, Min(1)] private int[] _handPromotionLevels = { 6, 11 };
        [Tooltip("첫 이름과 승급 후 이름입니다. 승급 레벨 수보다 하나 많아야 합니다.")]
        [SerializeField] private string[] _handStageNames = { "손 강화", "주먹 강화", "망치 강화" };
        [Tooltip("첫 승급의 반경입니다. 기존 범위 공격의 기본 반경 1.5를 사용합니다.")]
        [SerializeField, Min(0f)] private float _handFirstPromotionRadius = 1.5f;
        [SerializeField, Min(0f)] private float _handRadiusPerPromotion = 1f;

        [Header("Fist Bonus Attack")]
        [Tooltip("주먹 승급 시 해금되는 추가 타격의 기본 피해입니다. 기본 손 피해와 별개입니다.")]
        [SerializeField, Min(1)] private int _fistBaseDamage = 5;
        [Tooltip("주먹 승급 이후 일반 강화 한 번마다 주먹 피해에 더하는 값입니다.")]
        [SerializeField, Min(0)] private int _fistDamagePerLevel = 1;
        [Tooltip("주먹 승급부터 단계마다 더하는 발동 확률입니다. 0.06이면 6%, 12%, 18%, 24%, 30%이며 최대 30%입니다.")]
        [SerializeField, Range(0f, 0.3f)] private float _fistChancePerLevel = 0.06f;

        [Header("Hammer Bonus Attack")]
        [Tooltip("망치 승급 시 해금되는 별도 추가 타격의 기본 피해입니다.")]
        [SerializeField, Min(1)] private int _hammerBaseDamage = 10;
        [SerializeField, Min(0)] private int _hammerDamagePerLevel = 2;
        [Tooltip("망치 승급부터 단계마다 더하는 독립 발동 확률입니다. 최대 30%입니다.")]
        [SerializeField, Range(0f, 0.3f)] private float _hammerChancePerLevel = 0.06f;

        [Header("Damage")]
        [Tooltip("직접·광역·자동 공격력 강화의 각 레벨에서 기본 공격력에 더하는 누적 추가 피해입니다. Element 0은 1레벨입니다.")]
        [SerializeField, Min(0)] private int[] _damageBonuses = { 1, 2, 3 };

        [Header("Auto Click: Final Values Per Level")]
        [Tooltip("자동클릭의 최종 공격력입니다. Element 0은 1레벨이며 다른 기본값이나 보너스를 더하지 않습니다.")]
        [SerializeField, Min(1)] private int[] _autoAttackDamages = { 1, 2, 3 };
        [Tooltip("기계팔 하나의 최종 초당 공격 횟수입니다. Element 0은 1레벨입니다.")]
        [SerializeField, Min(0.01f)] private float[] _autoAttackSpeeds = { 1f, 2f, 3f };
        [Tooltip("자동공격이 범위 공격으로 승급하는 구매 레벨입니다.")]
        [SerializeField, Min(1)] private int _autoPromotionLevel = 6;
        [SerializeField] private string _autoPromotedName = "자동 범위 공격";
        [Tooltip("승급 후 기존 원형 범위 공격에 적용할 첫 단계 반경입니다.")]
        [SerializeField, Min(0f)] private float _autoPromotionRadius = 1.5f;
        [SerializeField, Min(1)] private int _autoRefillStartLevel = 11;
        [SerializeField] private string _autoRefillName = "자동 보충";
        [SerializeField, Min(0.1f)] private float[] _autoRefillDelays = { 8f, 6f, 4f, 2f };

        public int AutoPromotionLevel => _autoPromotionLevel;
        public int AutoRefillStartLevel => _autoRefillStartLevel;

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
        [SerializeField]
        private KeycapRarityDistribution[] _rarityDistributions =
        {
            new KeycapRarityDistribution(90f, 8f, 1.8f, 0.2f, 0f, 0f, 0f, 0f),
            new KeycapRarityDistribution(85f, 10.5f, 3.75f, 0.75f, 0f, 0f, 0f, 0f),
            new KeycapRarityDistribution(80f, 12f, 6f, 2f, 0f, 0f, 0f, 0f),
            new KeycapRarityDistribution(70f, 15f, 10f, 5f, 0f, 0f, 0f, 0f)
        };

        public UpgradeId Id => _id;
        public string DisplayName => _displayName;
        public string Description => _description;
        public int MaxLevel => _levelCosts.Length;

        void OnValidate()
        {
            if (_id == UpgradeId.BoxCapacity)
            {
                int previousLength = _boxCapacities.Length;
                Array.Resize(ref _boxCapacities, MaxLevel + 1);
                for (int level = 0; level < _boxCapacities.Length; level++)
                {
                    if (level >= previousLength)
                        _boxCapacities[level] = level == 0 ? 5 : _boxCapacities[level - 1] + 5;
                    _boxCapacities[level] = Math.Max(level == 0 ? 1 : _boxCapacities[level - 1], _boxCapacities[level]);
                }
            }
            if (_id == UpgradeId.HandPower)
            {
                Array.Sort(_handPromotionLevels);
                for (int index = 0; index < _handPromotionLevels.Length; index++)
                    _handPromotionLevels[index] = Math.Max(index == 0 ? 1 : _handPromotionLevels[index - 1] + 1, _handPromotionLevels[index]);
                int previousLength = _handStageNames.Length;
                Array.Resize(ref _handStageNames, _handPromotionLevels.Length + 1);
                for (int index = previousLength; index < _handStageNames.Length; index++)
                    _handStageNames[index] = $"{index + 1}단계 강화";
            }
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
            else if (_id == UpgradeId.AutoClick)
            {
                Array.Resize(ref _autoAttackDamages, MaxLevel);
                Array.Resize(ref _autoAttackSpeeds, MaxLevel);
                for (int i = 0; i < MaxLevel; i++)
                {
                    _autoAttackDamages[i] = Math.Max(1, _autoAttackDamages[i]);
                    _autoAttackSpeeds[i] = Mathf.Max(0.01f, _autoAttackSpeeds[i]);
                }

                _autoRefillStartLevel = Mathf.Max(_autoPromotionLevel + 1, _autoRefillStartLevel);

                int refillLevelCount = Mathf.Max(0, MaxLevel - _autoRefillStartLevel + 1);
                int previousLength = _autoRefillDelays?.Length ?? 0;
                Array.Resize(ref _autoRefillDelays, refillLevelCount);

                for (int index = 0; index < refillLevelCount; index++)
                {
                    if (index >= previousLength || _autoRefillDelays[index] <= 0f)
                    {
                        _autoRefillDelays[index] = index == 0 ? 8f : Mathf.Max(0.1f, _autoRefillDelays[index - 1] - 2f);
                    }
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
        /// level에 설정된 상자의 최종 용량을 반환한다.
        /// 기본 0레벨부터 최대 레벨까지의 설정을 조회하며 범위를 벗어난 레벨은 예외로 알린다.
        /// </summary>
        public int GetBoxCapacity(int level)
        {
            if (level < 0 || level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (_boxCapacities.Length != MaxLevel + 1)
                throw new InvalidOperationException($"Upgrade '{name}' requires {MaxLevel + 1} box capacity values.");
            return _boxCapacities[level];
        }

        /// <summary>
        /// level에 레벨당 추가 확률을 곱해 두 배 보상의 누적 확률을 반환한다.
        /// 구매 레벨과 _doubleRewardChance를 사용하며 미구매 상태는 0, 최대 확률은 1로 제한한다.
        /// </summary>
        public float GetDoubleRewardChance(int level)
        {
            if (level < 0 || level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return Mathf.Clamp01(Mathf.Clamp01(_doubleRewardChance) * level);
        }

        /// <summary>
        /// level까지 구매한 승급 횟수를 반환한다.
        /// 설정된 승급 레벨을 사용하며 효과 데이터와 구매 상태는 변경하지 않는다.
        /// </summary>
        private int GetHandPromotionCount(int level)
        {
            int count = 0;
            foreach (int promotion in _handPromotionLevels)
                if (promotion > 0 && level >= promotion) count++;
            return count;
        }

        /// <summary>
        /// level에서 주먹 승급 전까지 구매한 손 강화의 추가 피해를 반환한다.
        /// 첫 승급 레벨과 레벨당 피해 증가량을 사용하며 주먹 강화는 기본 손 피해에 포함하지 않는다.
        /// </summary>
        public int GetHandDamageBonus(int level)
        {
            int handLevel = _handPromotionLevels.Length > 0 ? Mathf.Min(level, _handPromotionLevels[0] - 1) : level;
            return Mathf.Max(0, handLevel) * _handDamagePerLevel;
        }

        /// <summary>
        /// level과 첫 주먹 승급 레벨로 추가 타격 발동 확률을 계산한다.
        /// 승급 전에는 0을 반환하고 승급부터 레벨당 확률을 더하되 최대 30%로 제한한다.
        /// </summary>
        public float GetFistChance(int level)
        {
            if (_handPromotionLevels.Length == 0 || level < _handPromotionLevels[0]) return 0f;
            int effectiveLevel = _handPromotionLevels.Length > 1 ? Mathf.Min(level, _handPromotionLevels[1] - 1) : level;
            int fistLevel = effectiveLevel - _handPromotionLevels[0] + 1;
            return Mathf.Clamp(fistLevel * _fistChancePerLevel, 0f, MAX_FIST_CHANCE);
        }

        /// <summary>
        /// level의 주먹 추가 타격 피해를 기본 피해와 일반 강화 증가량으로 계산한다.
        /// 주먹 승급 전에는 0을 반환하고 망치 단계의 구매는 주먹 피해 증가에 포함하지 않는다.
        /// </summary>
        public int GetFistDamage(int level)
        {
            if (_handPromotionLevels.Length == 0 || level < _handPromotionLevels[0]) return 0;
            int fistLevel = _handPromotionLevels.Length > 1 ? Mathf.Min(level, _handPromotionLevels[1] - 1) : level;
            int upgrades = Mathf.Max(0, fistLevel - _handPromotionLevels[0] - (GetHandPromotionCount(fistLevel) - 1));
            return _fistBaseDamage + upgrades * _fistDamagePerLevel;
        }

        /// <summary>
        /// level이 두 번째 손 강화 승급인 망치 단계에 도달했는지 반환한다.
        /// 승급 레벨 배열을 사용하며 구매 레벨이나 강화 데이터를 변경하지 않는다.
        /// </summary>
        public bool IsHammerUnlocked(int level)
        {
            return _handPromotionLevels.Length > 1 && level >= _handPromotionLevels[1];
        }

        /// <summary>
        /// level과 망치 승급 레벨로 주먹과 독립적인 망치 발동 확률을 계산한다.
        /// 승급 전에는 0을 반환하며 레벨당 확률을 더하되 최대 30%로 제한한다.
        /// </summary>
        public float GetHammerChance(int level)
        {
            return IsHammerUnlocked(level)
                ? Mathf.Clamp((level - _handPromotionLevels[1] + 1) * _hammerChancePerLevel, 0f, MAX_FIST_CHANCE) : 0f;
        }

        /// <summary>
        /// level의 망치 추가 피해를 망치 기본 피해와 레벨당 증가량으로 계산한다.
        /// 승급 전에는 0을 반환하며 손과 주먹 피해에는 영향을 주지 않는다.
        /// </summary>
        public int GetHammerDamage(int level)
        {
            return IsHammerUnlocked(level)
                ? _hammerBaseDamage + (level - _handPromotionLevels[1]) * _hammerDamagePerLevel : 0;
        }

        /// <summary>
        /// level의 승급 횟수에 따른 최종 공격 반경을 반환한다.
        /// 기존 첫 반경과 이후 승급당 증가량을 사용하며 첫 승급 전 반경은 0이다.
        /// </summary>
        public float GetHandRadius(int level)
        {
            int promotions = GetHandPromotionCount(level);
            return promotions == 0 ? 0f : _handFirstPromotionRadius + (promotions - 1) * _handRadiusPerPromotion;
        }

        /// <summary>
        /// level의 승급 단계에 해당하는 상점 이름을 반환한다.
        /// 단계 이름 배열을 사용하며 추가 이름이 없으면 마지막 이름을 유지한다.
        /// </summary>
        public string GetHandName(int level)
        {
            return _handStageNames[Mathf.Min(GetHandPromotionCount(level), _handStageNames.Length - 1)];
        }

        /// <summary>
        /// level이 승급 구매인지 반환한다.
        /// 승급 레벨 배열에서 해당 값을 검색하며 구매 상태는 변경하지 않는다.
        /// </summary>
        public bool IsHandPromotion(int level)
        {
            return Array.IndexOf(_handPromotionLevels, level) >= 0;
        }

        /// <summary>
        /// level의 자동공격 승급 여부에 따른 공격 범위를 반환한다.
        /// 승급 레벨과 반경 설정을 사용하며 승급 전에는 단일 공격을 의미하는 0을 반환한다.
        /// </summary>
        public float GetAutoAttackRadius(int level)
        {
            return level >= _autoPromotionLevel ? _autoPromotionRadius : 0f;
        }

        /// <summary>
        /// level이 자동 보충의 첫 구매 레벨에 도달했는지 확인한다.
        /// 시작 레벨과 최대 레벨을 사용하며 자동 보충 해금 여부를 반환한다.
        /// </summary>
        public bool IsAutoRefillUnlocked(int level)
        {
            return _autoRefillStartLevel <= MaxLevel && level >= _autoRefillStartLevel;
        }

        /// <summary>
        /// level에 설정된 자동 보충 대기 시간을 반환한다.
        /// 시작 레벨을 배열의 첫 항목에 대응시키며 미해금 레벨은 사용할 수 없다.
        /// </summary>
        public float GetAutoRefillDelay(int level)
        {
            if (!IsAutoRefillUnlocked(level) || level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));

            int index = level - _autoRefillStartLevel;
            if (_autoRefillDelays == null || index >= _autoRefillDelays.Length)
                throw new InvalidOperationException("Auto refill delays must match the refill upgrade levels.");

            return Mathf.Max(0.1f, _autoRefillDelays[index]);
        }

        /// <summary>
        /// level의 자동공격 강화 단계에 해당하는 상점 이름을 반환한다.
        /// 자동 보충, 광역 공격, 기본 공격 순서로 현재 단계의 이름을 선택한다.
        /// </summary>
        public string GetAutoAttackName(int level)
        {
            if (IsAutoRefillUnlocked(level))
                return _autoRefillName;

            return level >= _autoPromotionLevel ? _autoPromotedName : _displayName;
        }

        /// <summary>
        /// 전체 구매 level을 현재 손·주먹·망치 단계 안의 표시 레벨로 변환한다.
        /// 가장 최근 승급 레벨을 사용하며 승급 직후 1, 미구매 상태는 0을 반환한다.
        /// </summary>
        public int GetHandStageLevel(int level)
        {
            int start = 1;
            foreach (int promotion in _handPromotionLevels)
                if (level >= promotion) start = promotion;
            return level == 0 ? 0 : level - start + 1;
        }

        /// <summary>
        /// level이 속한 강화 단계의 표시 최대 레벨을 반환한다.
        /// 현재·다음 승급 레벨과 MaxLevel을 사용하여 해당 단계의 구매 구간 길이를 계산한다.
        /// </summary>
        public int GetHandStageMaxLevel(int level)
        {
            int start = 1;
            int end = MaxLevel;
            foreach (int promotion in _handPromotionLevels)
            {
                if (level >= promotion) start = promotion;
                else
                {
                    end = promotion - 1;
                    break;
                }
            }
            return Mathf.Max(1, end - start + 1);
        }

        /// <summary>
        /// level에 설정한 자동클릭의 최종 공격력을 반환한다.
        /// 구매 레벨의 항목을 사용하며 미구매 상태는 0을 반환한다.
        /// </summary>
        public int GetAutoAttackDamage(int level)
        {
            if (level < 0 || level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return level == 0 ? 0 : Mathf.Max(1, _autoAttackDamages[level - 1]);
        }

        /// <summary>
        /// level에 설정한 자동클릭의 최종 초당 공격 횟수를 반환한다.
        /// 구매 레벨의 항목을 사용하며 미구매 상태는 0을 반환한다.
        /// </summary>
        public float GetAutoAttackSpeed(int level)
        {
            if (level < 0 || level > MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return level == 0 ? 0f : Mathf.Max(0.01f, _autoAttackSpeeds[level - 1]);
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
