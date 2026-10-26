using System.Collections.Generic;

using UnityEngine.InputSystem;

namespace Game.Tournament
{
    public sealed class TournamentInventory
    {
        private const int MAX_LEVEL = 3;
        private const int KEYCAP_BUNDLE_SIZE = 3;

        private readonly Dictionary<Key, KeycapDefinition> _installedKeycaps = new Dictionary<Key, KeycapDefinition>();
        private readonly Dictionary<Key, KeycapDefinition> _keycaps = new Dictionary<Key, KeycapDefinition>();
        private readonly Dictionary<Key, int> _keycapLevels = new Dictionary<Key, int>();
        private readonly Dictionary<KeyboardPartDefinition, int> _counts = new Dictionary<KeyboardPartDefinition, int>();
        private readonly Dictionary<KeyboardPartDefinition, int> _partLevels = new Dictionary<KeyboardPartDefinition, int>();
        private readonly Dictionary<KeycapDefinition, List<HashSet<Key>>> _batches = new Dictionary<KeycapDefinition, List<HashSet<Key>>>();
        private readonly List<Key> _keycapPlacementOrder = new List<Key>();
        private readonly List<int> _placementBatches = new List<int>();
        private readonly int _capacity;
        private bool _configured;
        public static int MaxLevel => MAX_LEVEL;
        public int Capacity => _capacity;
        public int Total => _keycaps.Count;
        public IReadOnlyDictionary<Key, KeycapDefinition> Keycaps => _keycaps;
        public IReadOnlyDictionary<Key, KeycapDefinition> InstalledKeycaps => _installedKeycaps;
        public IReadOnlyDictionary<KeyboardPartDefinition, int> Counts => _counts;
        public SwitchDefinition Switch { get; private set; }
        public PlateDefinition Plate { get; private set; }
        public HousingDefinition Housing { get; private set; }
        public int DamageBonus => (Switch != null ? Switch.DamageBonus * GetPartLevel(Switch) : 0)
            + (Plate != null ? Plate.DamageBonus * GetPartLevel(Plate) : 0)
            + (Housing != null ? Housing.DamageBonus * GetPartLevel(Housing) : 0);

        /// <summary>capacity를 보관하고 능력이 전부 잠긴 재고를 만든다. 외형 구성은 ConfigureLayout에서 별도로 지정한다.</summary>
        public TournamentInventory(int capacity) { _capacity = capacity; }

        /// <summary>keyboard에 실제 장착된 SO와 parts의 전체 부품을 한 번 보관한다. 외형은 수정하지 않고 모든 해금 단계를 0으로 유지한다.</summary>
        public void ConfigureLayout(KeyboardCustomization keyboard, IReadOnlyList<KeyboardPartDefinition> parts, KeycapDefinition defaultKeycap)
        {
            if (_configured) return;
            foreach (var binding in keyboard.GetComponent<KeyboardModeling.KeyboardInputController>().GetKeycapLayout())
                _installedKeycaps.Add(binding.Key, keyboard.GetKeycapDefinition(binding.Value.transform) ?? defaultKeycap);
            foreach (KeyboardPartDefinition part in parts)
            {
                if (part is SwitchDefinition axis) Switch = axis;
                else if (part is PlateDefinition plate) Plate = plate;
                else if (part is HousingDefinition housing) Housing = housing;
            }
            _configured = true;
        }

        /// <summary>key의 개별 능력 단계를 반환한다. 해금되지 않은 위치는 0이다.</summary>
        public int GetKeycapLevel(Key key) { return _keycapLevels.TryGetValue(key, out int level) ? level : 0; }

        /// <summary>part의 전체 부품 능력 단계를 반환한다. 미구매 부품은 0이다.</summary>
        public int GetPartLevel(KeyboardPartDefinition part) { return _partLevels.TryGetValue(part, out int level) ? level : 0; }

        /// <summary>keycap과 동일한 장착 위치에 추가 구매가 필요한지 검사한다. 미사용 묶음으로 달성 가능한 단계도 합산해 불필요한 구매를 막는다.</summary>
        public bool CanPurchase(KeyboardPartDefinition part, TournamentKeyLayout layout)
        {
            if (part is KeycapDefinition keycap)
            {
                if (keycap.Ability == null) return false;
                foreach (var installed in _installedKeycaps)
                {
                    if (installed.Value != keycap) continue;
                    int possible = GetKeycapLevel(installed.Key);
                    if (_batches.TryGetValue(keycap, out var batches))
                        foreach (var batch in batches)
                            if (batch.Count < KEYCAP_BUNDLE_SIZE && !batch.Contains(installed.Key)) possible++;
                    if (possible < MAX_LEVEL) return true;
                }
                return false;
            }
            return part != null && (part == Switch || part == Plate || part == Housing) && GetPartLevel(part) < MAX_LEVEL;
        }

        /// <summary>part의 구매 재화를 제외한 상태를 갱신한다. 키캡은 독립된 3회 사용 묶음을 추가하고 전체 부품은 즉시 한 단계 해금·강화한다.</summary>
        public bool TryPurchase(KeyboardPartDefinition part, TournamentKeyLayout layout)
        {
            if (!CanPurchase(part, layout)) return false;
            if (part is KeycapDefinition keycap)
            {
                if (!_batches.TryGetValue(keycap, out var batches))
                {
                    batches = new List<HashSet<Key>>();
                    _batches.Add(keycap, batches);
                }
                batches.Add(new HashSet<Key>());
                _counts[part] = GetCount(part) + KEYCAP_BUNDLE_SIZE;
            }
            else
            {
                _counts[part] = GetCount(part) + 1;
                _partLevels[part] = GetPartLevel(part) + 1;
            }
            return true;
        }

        /// <summary>part의 구매 가능 여부를 반환한다. 외형 교체나 자동 무작위 배치는 수행하지 않는다.</summary>
        public bool CanAdd(KeyboardPartDefinition part, TournamentKeyLayout layout) { return CanPurchase(part, layout); }

        /// <summary>로봇이 part를 구매하고 키캡 묶음은 동일 부품의 가능한 위치에 무작위 적용한다. 전체 부품은 구매 시 즉시 강화한다.</summary>
        public bool TryAdd(KeyboardPartDefinition part, TournamentKeyLayout layout)
        {
            if (!TryPurchase(part, layout)) return false;
            if (part is KeycapDefinition keycap)
            {
                var candidates = new List<Key>();
                foreach (var installed in _installedKeycaps)
                    if (installed.Value == keycap && GetKeycapLevel(installed.Key) < MAX_LEVEL) candidates.Add(installed.Key);
                while (candidates.Count > 0)
                {
                    int index = UnityEngine.Random.Range(0, candidates.Count);
                    TryEquipKeycap(keycap, candidates[index], layout);
                    candidates.RemoveAt(index);
                }
            }
            return true;
        }

        /// <summary>part의 아직 배치하지 않은 해금·강화 횟수를 반환한다. 전체 부품은 구매 즉시 소모된다.</summary>
        public int GetAvailableCount(KeyboardPartDefinition part) { return GetCount(part) - GetEquippedCount(part); }

        /// <summary>part에 사용한 해금·강화 횟수를 반환한다. 키캡은 위치별 단계를 합산한다.</summary>
        public int GetEquippedCount(KeyboardPartDefinition part)
        {
            if (!(part is KeycapDefinition keycap)) return GetPartLevel(part);
            int count = 0;
            foreach (var installed in _keycaps)
                if (installed.Value == keycap) count += GetKeycapLevel(installed.Key);
            return count;
        }

        /// <summary>전체 부품의 현재 해금 여부를 반환한다. 구매 시 자동 적용되므로 클릭으로 단계를 추가하지 않는다.</summary>
        public bool TryEquipPart(KeyboardPartDefinition part) { return GetPartLevel(part) > 0; }

        /// <summary>part의 능력은 구매 즉시 적용되므로 해제하지 않고 false를 반환한다. 외형과 강화 상태를 유지한다.</summary>
        public bool TryUnequipPart(KeyboardPartDefinition part) { return false; }

        /// <summary>key와 동일한 keycap의 미사용 구매 묶음 한 개를 소모해 해당 위치만 한 단계 올린다. 한 묶음은 같은 위치에 한 번만 적용한다.</summary>
        public bool TryEquipKeycap(KeycapDefinition keycap, Key key, TournamentKeyLayout layout)
        {
            if (!_installedKeycaps.TryGetValue(key, out var installed) || installed != keycap
                || GetKeycapLevel(key) >= MAX_LEVEL || !_batches.TryGetValue(keycap, out var batches)) return false;
            for (int index = 0; index < batches.Count; index++)
            {
                if (batches[index].Count >= KEYCAP_BUNDLE_SIZE || batches[index].Contains(key)) continue;
                batches[index].Add(key);
                _keycaps[key] = keycap;
                _keycapLevels[key] = GetKeycapLevel(key) + 1;
                _keycapPlacementOrder.Add(key);
                _placementBatches.Add(index);
                return true;
            }
            return false;
        }

        /// <summary>keycap에 현재 남은 묶음을 적용할 수 있는 위치가 있는지 반환한다. 배치나 강화 상태는 변경하지 않는다.</summary>
        public bool CanApplyKeycap(KeycapDefinition keycap)
        {
            if (!_batches.TryGetValue(keycap, out var batches)) return false;
            foreach (var installed in _installedKeycaps)
                if (installed.Value == keycap && GetKeycapLevel(installed.Key) < MAX_LEVEL)
                    foreach (var batch in batches)
                        if (batch.Count < KEYCAP_BUNDLE_SIZE && !batch.Contains(installed.Key)) return true;
            return false;
        }

        /// <summary>keycap의 마지막 해금·강화를 한 단계 취소하고 사용 묶음에 돌려놓는다. 외형은 유지하고 해제한 key를 반환한다.</summary>
        public bool TryUnequipLatestKeycap(KeycapDefinition keycap, out Key key)
        {
            for (int index = _keycapPlacementOrder.Count - 1; index >= 0; index--)
            {
                key = _keycapPlacementOrder[index];
                if (_keycaps[key] != keycap) continue;
                _batches[keycap][_placementBatches[index]].Remove(key);
                int level = GetKeycapLevel(key) - 1;
                if (level == 0) { _keycapLevels.Remove(key); _keycaps.Remove(key); }
                else _keycapLevels[key] = level;
                _keycapPlacementOrder.RemoveAt(index);
                _placementBatches.RemoveAt(index);
                return true;
            }
            key = Key.None;
            return false;
        }

        /// <summary>part의 누적 구매 횟수 또는 키캡 묶음 수량을 반환한다. 미구매 부품은 0이다.</summary>
        public int GetCount(KeyboardPartDefinition part) { return _counts.TryGetValue(part, out int count) ? count : 0; }
    }

    public sealed class TournamentResult
    {
        public int Rank { get; }
        public int Gold { get; }
        public long PlayerScore { get; }
        public long EnemyScore { get; }

        /// <summary>rank, gold와 양측 최종 점수를 보관하는 작업실 전달용 결과를 생성한다.</summary>
        public TournamentResult(int rank, int gold, long playerScore, long enemyScore)
        {
            Rank = rank;
            Gold = gold;
            PlayerScore = playerScore;
            EnemyScore = enemyScore;
        }
    }
}
