using System;
using System.Text;

using Game.Upgrades;
using KeyboardModeling;

namespace Game.Shop
{
    public static class UpgradeDescriptionFormatter
    {
        /// <summary>
        /// definition의 level과 다음 구매 레벨 효과를 한글 설명으로 조합한다.
        /// 최대 레벨에서는 현재 효과만 반환하며 강화 데이터와 구매 상태는 변경하지 않는다.
        /// </summary>
        public static string Format(UpgradeDefinition definition, int level)
        {
            bool hasNext = level < definition.MaxLevel;
            int nextLevel = hasNext ? level + 1 : level;
            StringBuilder description = new StringBuilder();

            switch (definition.Id)
            {
                case UpgradeId.SmashDamage:
                case UpgradeId.AreaDamage:
                    AppendComparison(description, "추가 피해", $"+{definition.GetDamageBonus(level)}",
                        $"+{definition.GetDamageBonus(nextLevel)}", hasNext);
                    break;
                case UpgradeId.AreaSmash:
                    AppendComparison(description, "공격 반경", $"{definition.GetAreaRadius(level):0.##}칸",
                        $"{definition.GetAreaRadius(nextLevel):0.##}칸", hasNext);
                    break;
                case UpgradeId.HandPower:
                    AppendHandEffects(description, definition, level, nextLevel, hasNext);
                    break;
                case UpgradeId.AutoClick:
                    AppendAutoEffects(description, definition, level, nextLevel, hasNext);
                    break;
                case UpgradeId.LuckyReward:
                    AppendComparison(description, "두 배 보상 확률", $"{definition.GetDoubleRewardChance(level):P0}",
                        $"{definition.GetDoubleRewardChance(nextLevel):P0}", hasNext);
                    break;
                case UpgradeId.BoxCapacity:
                    AppendComparison(description, "상자 용량", $"{definition.GetBoxCapacity(level)}개",
                        $"{definition.GetBoxCapacity(nextLevel)}개", hasNext);
                    break;
                case UpgradeId.RareKeycapQuality:
                    AppendRarityEffects(description, definition, level, nextLevel, hasNext);
                    break;
                case UpgradeId.FeverDuration:
                    AppendComparison(description, "피버 지속 시간", $"{definition.GetFeverDuration(level):0.#}초",
                        $"{definition.GetFeverDuration(nextLevel):0.#}초", hasNext);
                    description.Append("\n다음 피버부터 적용");
                    break;
                case UpgradeId.FeverDamageMultiplier:
                    AppendComparison(description, "피버 피해 배율", $"{definition.GetFeverDamageMultiplier(level):0.##}배",
                        $"{definition.GetFeverDamageMultiplier(nextLevel):0.##}배", hasNext);
                    description.Append("\n다음 피버부터 적용");
                    break;
                default:
                    description.Append(definition.Description);
                    break;
            }

            return description.ToString();
        }

        /// <summary>
        /// label과 현재·다음 효과 문구를 description에 한 줄로 추가한다.
        /// hasNext가 false이면 현재 효과만 기록하며 기존 줄과 줄바꿈으로 구분한다.
        /// </summary>
        private static void AppendComparison(StringBuilder description, string label, string current, string next, bool hasNext)
        {
            if (description.Length > 0)
                description.Append('\n');

            description.Append(label).Append(": 현재 ").Append(current);
            if (hasNext)
                description.Append(" → 다음 ").Append(next);
        }

        /// <summary>
        /// definition의 현재 level과 nextLevel로 손·주먹·망치 효과와 승급 설명을 추가한다.
        /// 해금된 효과와 다음 구매로 해금되는 효과를 description에 기록한다.
        /// </summary>
        private static void AppendHandEffects(StringBuilder description, UpgradeDefinition definition, int level, int nextLevel, bool hasNext)
        {
            AppendComparison(description, "기본 추가 피해", $"+{definition.GetHandDamageBonus(level)}",
                $"+{definition.GetHandDamageBonus(nextLevel)}", hasNext);

            if (definition.GetFistDamage(nextLevel) > 0)
            {
                AppendComparison(description, "주먹 추가 타격",
                    FormatBonusAttack(definition.GetFistDamage(level), definition.GetFistChance(level)),
                    FormatBonusAttack(definition.GetFistDamage(nextLevel), definition.GetFistChance(nextLevel)), hasNext);
            }

            if (definition.IsHammerUnlocked(nextLevel))
            {
                AppendComparison(description, "망치 추가 타격",
                    FormatBonusAttack(definition.GetHammerDamage(level), definition.GetHammerChance(level)),
                    FormatBonusAttack(definition.GetHammerDamage(nextLevel), definition.GetHammerChance(nextLevel)), hasNext);
            }

            if (definition.GetHandRadius(nextLevel) > 0f)
            {
                AppendComparison(description, "공격 반경", $"{definition.GetHandRadius(level):0.##}칸",
                    $"{definition.GetHandRadius(nextLevel):0.##}칸", hasNext);
            }

            if (hasNext && definition.IsHandPromotion(nextLevel))
                AppendComparison(description, "승급", definition.GetHandName(level), definition.GetHandName(nextLevel), true);
        }

        /// <summary>
        /// damage와 chance로 추가 타격의 해금 여부, 피해 및 발동 확률을 표시한다.
        /// 피해가 없는 단계는 미해금 문구를 반환하며 강화 수치는 변경하지 않는다.
        /// </summary>
        private static string FormatBonusAttack(int damage, float chance)
        {
            return damage == 0 ? "미해금" : $"피해 {damage}, 발동 확률 {chance:P0}";
        }

        /// <summary>
        /// definition의 level과 nextLevel로 자동공격·범위·보충 효과를 추가한다.
        /// 보충은 해금된 레벨에서만 시간을 조회하고 승급 시 다음 단계 이름을 description에 기록한다.
        /// </summary>
        private static void AppendAutoEffects(StringBuilder description, UpgradeDefinition definition, int level, int nextLevel, bool hasNext)
        {
            AppendComparison(description, "공격력", $"{definition.GetAutoAttackDamage(level)}",
                $"{definition.GetAutoAttackDamage(nextLevel)}", hasNext);
            AppendComparison(description, "공격 속도", $"초당 {definition.GetAutoAttackSpeed(level):0.##}회",
                $"초당 {definition.GetAutoAttackSpeed(nextLevel):0.##}회", hasNext);

            if (definition.GetAutoAttackRadius(nextLevel) > 0f)
            {
                AppendComparison(description, "공격 반경", $"{definition.GetAutoAttackRadius(level):0.##}칸",
                    $"{definition.GetAutoAttackRadius(nextLevel):0.##}칸", hasNext);
            }

            if (definition.IsAutoRefillUnlocked(nextLevel))
            {
                string current = definition.IsAutoRefillUnlocked(level) ? $"{definition.GetAutoRefillDelay(level):0.#}초" : "미해금";
                AppendComparison(description, "자동 보충 대기", current, $"{definition.GetAutoRefillDelay(nextLevel):0.#}초", hasNext);
            }

            if (hasNext && (nextLevel == definition.AutoPromotionLevel || nextLevel == definition.AutoRefillStartLevel))
                AppendComparison(description, "승급", definition.GetAutoAttackName(level), definition.GetAutoAttackName(nextLevel), true);
        }

        /// <summary>
        /// 현재와 다음 레벨의 8티어 확률 중 등장 가능한 티어를 설명에 추가한다.
        /// definition의 분포와 level을 사용하며 표시 문구를 description에 기록한다.
        /// </summary>
        private static void AppendRarityEffects(StringBuilder description, UpgradeDefinition definition, int level, int nextLevel, bool hasNext)
        {
            KeycapRarityDistribution current = definition.GetRarityDistribution(level);
            KeycapRarityDistribution next = hasNext ? definition.GetRarityDistribution(nextLevel) : current;

            foreach (KeycapRarity rarity in Enum.GetValues(typeof(KeycapRarity)))
            {
                if (current.GetProbability(rarity) <= 0f && next.GetProbability(rarity) <= 0f)
                    continue;

                AppendRarityComparison(description, $"티어 {(int)rarity + 1}", rarity, current, next, hasNext);
            }

            description.Append("\n다음 키보드부터 적용");
        }

        /// <summary>
        /// rarity에 해당하는 current와 next 분포의 확률을 백분율로 표시한다.
        /// label과 hasNext를 사용하여 description에 등급별 한 줄을 추가한다.
        /// </summary>
        private static void AppendRarityComparison(StringBuilder description, string label, KeycapRarity rarity,
            KeycapRarityDistribution current, KeycapRarityDistribution next, bool hasNext)
        {
            AppendComparison(description, $"{label} 확률", $"{current.GetProbability(rarity) * 100f:0.##}%",
                $"{next.GetProbability(rarity) * 100f:0.##}%", hasNext);
        }
    }
}
