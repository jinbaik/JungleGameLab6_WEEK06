using System;
using System.Collections.Generic;

using UnityEngine;

namespace Game.Tournament
{
    public enum ArtisanPassiveKind { None, Rook, Bishop, BongoCat, Cheese, Takodachi }

    public sealed class ArtisanPassivePlacement
    {
        public ArtisanPassiveKind Kind { get; }
        public int SourceIndex { get; internal set; } = -1;
        public int[] Origins { get; }
        public int[] Targets { get; }
        public IReadOnlyList<int[]> Rays { get; }
        public IReadOnlyList<Vector2> RayDirections { get; }

        /// <summary>kind의 origins와 targets 및 rays를 보관한다. 하나의 아티산이 확정한 범위를 판정과 표시에서 공유한다.</summary>
        public ArtisanPassivePlacement(ArtisanPassiveKind kind, int[] origins, int[] targets, List<int[]> rays, List<Vector2> directions)
        { Kind = kind; Origins = origins; Targets = targets; Rays = rays; RayDirections = directions; }
    }

    public sealed class ArtisanPassiveLayout
    {
        private const int KIND_COUNT = 5;
        private const int MAX_SEARCH_NODES = 30000;
        private static readonly Vector2Int[] _directions = { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(0, 1), new Vector2Int(1, 0) };
        private readonly int[,] _markerLevels;
        private readonly List<ArtisanPassivePlacement> _placements = new List<ArtisanPassivePlacement>();
        public int[] Bonuses { get; }
        public IReadOnlyList<ArtisanPassivePlacement> Placements => _placements;

        /// <summary>count개 위치의 패시브 단계와 피해 합계를 0으로 초기화한다. 외부 SO와 키캡 상태는 변경하지 않는다.</summary>
        private ArtisanPassiveLayout(int count) { Bonuses = new int[count]; _markerLevels = new int[KIND_COUNT, count]; }

        /// <summary>kind와 index에 적용된 최고 단계를 반환한다. 같은 종류는 하나의 표식과 최고 단계만 사용한다.</summary>
        public int GetLevel(ArtisanPassiveKind kind, int index) { return _markerLevels[(int)kind - 1, index]; }

        /// <summary>parts와 levels 및 실제 좌표로 배치를 확정해 반환한다. installed 위치만 선택하며 같은 종류는 최대값, 다른 종류는 합산한다.</summary>
        public static ArtisanPassiveLayout Build(KeycapDefinition[] parts, int[] levels, Vector2Int[] coordinates, Vector2[] positions, bool[] installed, Vector2[] halfSizes = null)
        {
            var result = new ArtisanPassiveLayout(parts.Length);
            var available = new List<int>();
            for (int index = 0; index < installed.Length; index++) if (installed[index]) available.Add(index);
            if (available.Count == 0) return result;
            var amounts = new int[KIND_COUNT, parts.Length];
            for (int source = 0; source < parts.Length; source++)
            {
                KeycapDefinition part = parts[source];
                if (!installed[source] || part == null || !part.IsArtisan || part.Ability == null || levels[source] <= 0) continue;
                KeycapAbilityDefinition ability = part.Ability;
                if (ability.ArtisanKind == ArtisanPassiveKind.None) continue;
                ArtisanPassivePlacement placement = Select(ability, available, coordinates, positions, halfSizes);
                placement.SourceIndex = source;
                result._placements.Add(placement);
                int kind = (int)ability.ArtisanKind - 1;
                foreach (int target in placement.Targets)
                {
                    result._markerLevels[kind, target] = Mathf.Max(result._markerLevels[kind, target], levels[source]);
                    amounts[kind, target] = Mathf.Max(amounts[kind, target], ability.PassiveDamageBonus * levels[source]);
                }
            }
            for (int index = 0; index < parts.Length; index++)
                for (int kind = 0; kind < KIND_COUNT; kind++) result.Bonuses[index] += amounts[kind, index];
            return result;
        }

        /// <summary>ability 종류와 설정으로 기준점과 대상 및 선을 한 번 선택해 반환한다. 무작위 상태는 배치 시점에만 사용한다.</summary>
        private static ArtisanPassivePlacement Select(KeycapAbilityDefinition ability, List<int> available, Vector2Int[] coordinates, Vector2[] positions, Vector2[] halfSizes)
        {
            var targets = new HashSet<int>();
            var origins = new List<int>();
            var rays = new List<int[]>();
            var directions = new List<Vector2>();
            if (ability.ArtisanKind == ArtisanPassiveKind.Cheese)
            {
                var candidates = new List<int>(available);
                Shuffle(candidates);
                var chosen = new List<int>();
                var best = new List<int>();
                int nodes = 0;
                SelectSpaced(candidates, chosen, best, coordinates, ability.PassiveTargetCount, ability.PassiveSpacing, ref nodes);
                return new ArtisanPassivePlacement(ability.ArtisanKind, new int[0], best.ToArray(), rays, directions);
            }
            if (ability.ArtisanKind == ArtisanPassiveKind.BongoCat)
            {
                var centers = new List<int>();
                foreach (int candidate in available)
                {
                    int left = 0, right = 0;
                    foreach (int index in available)
                    { if (positions[index].x < positions[candidate].x) left++; else if (positions[index].x > positions[candidate].x) right++; }
                    if (left >= ability.PassiveSideCount && right >= ability.PassiveSideCount) centers.Add(candidate);
                }
                int origin = centers.Count > 0 ? centers[UnityEngine.Random.Range(0, centers.Count)] : available[UnityEngine.Random.Range(0, available.Count)];
                origins.Add(origin);
                foreach (int sign in new[] { -1, 1 })
                {
                    var side = available.FindAll(index => (positions[index].x - positions[origin].x) * sign > 0.001f);
                    side.Sort((a, b) => (positions[a] - positions[origin]).sqrMagnitude.CompareTo((positions[b] - positions[origin]).sqrMagnitude));
                    for (int index = 0; index < Mathf.Min(ability.PassiveSideCount, side.Count); index++) targets.Add(side[index]);
                }
            }
            else
            {
                var candidates = new List<int>(available);
                Shuffle(candidates);
                int count = ability.ArtisanKind == ArtisanPassiveKind.Takodachi ? Mathf.Min(ability.PassiveOriginCount, candidates.Count) : 1;
                for (int index = 0; index < count; index++)
                {
                    int origin = candidates[index];
                    origins.Add(origin);
                    if (ability.ArtisanKind == ArtisanPassiveKind.Rook)
                    { AddRay(origin, _directions[2], available, coordinates, targets, rays, directions); AddRay(origin, _directions[3], available, coordinates, targets, rays, directions); }
                    else if (ability.ArtisanKind == ArtisanPassiveKind.Bishop)
                    { AddRay(origin, _directions[0], available, coordinates, targets, rays, directions); AddRay(origin, _directions[1], available, coordinates, targets, rays, directions); }
                    else AddPhysicalRay(origin, _directions[UnityEngine.Random.Range(0, _directions.Length)], available, positions, halfSizes, targets, rays, directions);
                }
            }
            var ordered = new List<int>(targets); ordered.Sort();
            return new ArtisanPassivePlacement(ability.ArtisanKind, origins.ToArray(), ordered.ToArray(), rays, directions);
        }

        /// <summary>origin을 지나는 direction 직선상의 실제 키를 targets와 rays에 추가한다. 행열의 외적이 0인 위치만 포함한다.</summary>
        private static void AddRay(int origin, Vector2Int direction, List<int> available, Vector2Int[] coordinates, HashSet<int> targets, List<int[]> rays, List<Vector2> directions)
        {
            var ray = new List<int>();
            foreach (int index in available)
            {
                Vector2Int delta = coordinates[index] - coordinates[origin];
                if (delta.x * direction.y != delta.y * direction.x) continue;
                ray.Add(index); targets.Add(index);
            }
            ray.Sort((a, b) => (coordinates[a].x * direction.x + coordinates[a].y * direction.y).CompareTo(coordinates[b].x * direction.x + coordinates[b].y * direction.y));
            rays.Add(ray.ToArray());
            directions.Add(new Vector2(direction.y, -direction.x).normalized);
        }

        /// <summary>origin을 지나는 실제 평면 직선과 키캡 사각 영역이 교차하는 대상을 반환 목록에 추가한다. 큰 키와 엇갈린 자판 간격도 반영한다.</summary>
        private static void AddPhysicalRay(int origin, Vector2Int direction, List<int> available, Vector2[] positions,
            Vector2[] halfSizes, HashSet<int> targets, List<int[]> rays, List<Vector2> directions)
        {
            Vector2 along = new Vector2(direction.y, -direction.x).normalized;
            Vector2 normal = new Vector2(-along.y, along.x);
            var ray = new List<int>();
            foreach (int index in available)
            {
                Vector2 size = halfSizes != null ? halfSizes[index] : Vector2.one * 0.4625f;
                float extent = Mathf.Abs(normal.x) * size.x + Mathf.Abs(normal.y) * size.y;
                if (Mathf.Abs(Vector2.Dot(positions[index] - positions[origin], normal)) > extent + 0.001f) continue;
                ray.Add(index); targets.Add(index);
            }
            ray.Sort((a, b) => Vector2.Dot(positions[a], along).CompareTo(Vector2.Dot(positions[b], along)));
            rays.Add(ray.ToArray()); directions.Add(along);
        }

        /// <summary>candidates에서 spacing 이상 떨어진 count개 위치를 탐색한다. 대각선을 포함한 행열 최대 거리를 사용하며 선택 가능한 최선의 결과를 best에 보관한다.</summary>
        private static bool SelectSpaced(List<int> candidates, List<int> chosen, List<int> best, Vector2Int[] coordinates, int count, int spacing, ref int nodes)
        {
            if (chosen.Count > best.Count) { best.Clear(); best.AddRange(chosen); }
            if (chosen.Count >= count) return true;
            if (++nodes > MAX_SEARCH_NODES || chosen.Count + candidates.Count < count) return false;
            for (int index = 0; index < candidates.Count; index++)
            {
                int selected = candidates[index];
                var next = new List<int>();
                for (int other = index + 1; other < candidates.Count; other++)
                {
                    Vector2Int delta = coordinates[candidates[other]] - coordinates[selected];
                    if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) >= spacing) next.Add(candidates[other]);
                }
                chosen.Add(selected);
                if (SelectSpaced(next, chosen, best, coordinates, count, spacing, ref nodes)) return true;
                chosen.RemoveAt(chosen.Count - 1);
                if (nodes > MAX_SEARCH_NODES) break;
            }
            return false;
        }

        /// <summary>values를 제자리에서 무작위 섞는다. 반환값 없이 배치 후보 순서만 변경한다.</summary>
        private static void Shuffle(List<int> values)
        {
            for (int index = values.Count - 1; index > 0; index--)
            { int other = UnityEngine.Random.Range(0, index + 1); int value = values[index]; values[index] = values[other]; values[other] = value; }
        }
    }
}
