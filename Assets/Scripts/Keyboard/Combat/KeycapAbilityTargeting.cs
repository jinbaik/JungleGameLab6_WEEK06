using System.Collections.Generic;

using UnityEngine;

namespace Game.Tournament
{
    public static class KeycapAbilityTargeting
    {
        /// <summary>ability의 범위와 center 좌표로 살아 있는 대상 인덱스를 반환한다. 중심을 제외하고 대각선은 행열 차이가 같은 위치로 판정한다.</summary>
        public static List<int> Select(KeycapAbilityDefinition ability, int center, Vector2Int[] coordinates,
            Vector2[] positions, KeycapHealth[] keycaps)
        {
            var targets = new List<int>();
            for (int index = 0; index < keycaps.Length; index++)
            {
                if (!keycaps[index].gameObject.activeInHierarchy || keycaps[index].CurrentHP <= 0) continue;
                if (ability.Target == KeycapTargetPattern.Self)
                {
                    if (index == center) targets.Add(index);
                    continue;
                }
                if (index == center) continue;
                Vector2Int delta = coordinates[index] - coordinates[center];
                bool matches = ability.Target == KeycapTargetPattern.All || ability.Target == KeycapTargetPattern.Neighbors
                    || (ability.Target == KeycapTargetPattern.Row && delta.x == 0)
                    || (ability.Target == KeycapTargetPattern.Column && delta.y == 0)
                    || (ability.Target == KeycapTargetPattern.Cross && (delta.x == 0 || delta.y == 0))
                    || (ability.Target == KeycapTargetPattern.Diagonal && Mathf.Abs(delta.x) == Mathf.Abs(delta.y));
                if (matches) targets.Add(index);
            }
            targets.Sort((left, right) =>
            {
                int comparison = (positions[left] - positions[center]).sqrMagnitude.CompareTo((positions[right] - positions[center]).sqrMagnitude);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });
            if (ability.Target == KeycapTargetPattern.Neighbors && targets.Count > ability.NeighborCount)
                targets.RemoveRange(ability.NeighborCount, targets.Count - ability.NeighborCount);
            if (ability.Propagation == KeycapPropagation.LeftToRight)
                targets.Sort((left, right) => positions[left].x != positions[right].x
                    ? positions[left].x.CompareTo(positions[right].x) : left.CompareTo(right));
            else if (ability.Propagation == KeycapPropagation.NearestChain)
            {
                var route = new List<int>();
                int previous = center;
                while (targets.Count > 0)
                {
                    int closest = 0;
                    for (int index = 1; index < targets.Count; index++)
                        if ((positions[targets[index]] - positions[previous]).sqrMagnitude
                            < (positions[targets[closest]] - positions[previous]).sqrMagnitude) closest = index;
                    previous = targets[closest];
                    route.Add(previous);
                    targets.RemoveAt(closest);
                }
                return route;
            }
            return targets;
        }

        /// <summary>targets의 도달 시각을 ability 전파 시간과 중심 위치로 계산한다. 순차 방식은 균등 간격, 거리 방식은 실제 거리를 사용한다.</summary>
        public static float[] GetArrivalTimes(KeycapAbilityDefinition ability, int center, List<int> targets, Vector2[] positions)
        {
            var times = new float[targets.Count];
            float farthest = 0f;
            if (ability.Propagation == KeycapPropagation.Distance)
                foreach (int target in targets) farthest = Mathf.Max(farthest, Vector2.Distance(positions[center], positions[target]));
            for (int index = 0; index < targets.Count; index++)
                times[index] = ability.Propagation == KeycapPropagation.Distance
                    ? (farthest > 0f ? Vector2.Distance(positions[center], positions[targets[index]]) / farthest * ability.WaveDuration : 0f)
                    : (targets.Count > 1 ? (float)index / (targets.Count - 1) * ability.WaveDuration : 0f);
            return times;
        }
    }
}
