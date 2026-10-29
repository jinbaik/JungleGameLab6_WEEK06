using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;

using Random = UnityEngine.Random;

namespace Game.Tournament
{
    public sealed class TournamentKeyboardEffects : MonoBehaviour
    {
        public static event Action<TournamentKeyboardEffects, Key, SpecialKeycapType, string, string, string> PlaytestEffectObserved;

        [Header("Runtime")]
        private readonly Queue<int> _pending = new Queue<int>();
        private readonly HashSet<int> _delayedExplosions = new HashSet<int>();
        private readonly Dictionary<KeycapHealth, int> _indices = new Dictionary<KeycapHealth, int>();
        private TournamentController _tournament;
        private KeyboardScoreCombo _score;
        private KeyboardInputController _keyboard;
        private KeyboardDestruction _destruction;
        private KeyboardFeedbackManager _feedback;
        private KeycapHealth[] _keycaps;
        private SpecialKeycapType[] _types;
        private Key[] _keys;
        private string[] _effectIds;
        private Vector2Int[] _coordinates;
        private Vector2[] _positions;
        private KeyboardNeighborLayout _neighbors;

        public KeyboardInputController PlaytestKeyboard => _keyboard;
        public TournamentController PlaytestTournament => _tournament;

        /// <summary>
        /// tournament와 inventory, score 및 layout으로 새 키보드의 체력, 특수 유형과 행열을 구성한다.
        /// 보유 수량을 시스템 키를 제외한 80개 무작위 칸에 배치하고 파괴 이벤트를 등록하며 보유 목록은 소모하지 않는다.
        /// </summary>
        public void Initialize(TournamentController tournament, TournamentInventory inventory, KeyboardScoreCombo score, TournamentKeyLayout layout, int health, Material[] materials)
        {
            _tournament = tournament;
            _score = score;
            _keyboard = GetComponent<KeyboardInputController>();
            _keyboard.UseAllDestructionKeys();
            _destruction = GetComponent<KeyboardDestruction>();
            _destruction.ConfigureRequiredBrokenCount(tournament.Settings.GetRequiredBrokenCount(tournament.Round));
            _feedback = GetComponent<KeyboardFeedbackManager>();
            IReadOnlyDictionary<Key, KeycapHealth> bindings = _keyboard.GetKeycapLayout();
            Dictionary<Key, Vector2Int> coordinates = layout.CreateCoordinates();
            _keycaps = new KeycapHealth[bindings.Count];
            _types = new SpecialKeycapType[bindings.Count];
            _keys = new Key[bindings.Count];
            _effectIds = new string[bindings.Count];
            _coordinates = new Vector2Int[bindings.Count];
            _positions = new Vector2[bindings.Count];
            List<int> slots = new List<int>();
            int index = 0;
            foreach (KeyValuePair<Key, KeycapHealth> binding in bindings)
            {
                _keycaps[index] = binding.Value;
                _keys[index] = binding.Key;
                _coordinates[index] = coordinates[binding.Key];
                Vector3 position = transform.InverseTransformPoint(binding.Value.transform.position);
                _positions[index] = new Vector2(position.x, position.z);
                binding.Value.ConfigureTournamentHealth(health);
                if (CanPlaceSpecial(binding.Key)) slots.Add(index);
                _indices.Add(binding.Value, index++);
            }
            _neighbors = new KeyboardNeighborLayout(_positions);
            foreach (SpecialKeycapType type in inventory.CreateTypes())
            {
                int roll = Random.Range(0, slots.Count);
                int slot = slots[roll];
                slots.RemoveAt(roll);
                _types[slot] = type;
                _keycaps[slot].Broken += HandleBroken;
                _keycaps[slot].GetComponent<SpecialKeycapPresenter>().Initialize(_keycaps[slot], type, materials[(int)type - 1]);
            }
        }

        /// <summary>key가 좌우 Ctrl, Alt, Windows 또는 메뉴 키인지 확인하여 특수 키캡 배치 가능 여부를 반환한다.</summary>
        private bool CanPlaceSpecial(Key key)
        {
            return key != Key.LeftCtrl && key != Key.RightCtrl && key != Key.LeftAlt && key != Key.RightAlt
                && key != Key.LeftMeta && key != Key.RightMeta && key != Key.ContextMenu;
        }

        /// <summary>keycap의 파괴 효과를 예약하고 보너스는 먼저 활성화하여 해당 파괴 점수부터 적용한다.</summary>
        private void HandleBroken(KeycapHealth keycap)
        {
            keycap.Broken -= HandleBroken;
            int index = _indices[keycap];
            _effectIds[index] = Guid.NewGuid().ToString("N");
            ObserveEffect(index, "Broken", "");
            if (!_tournament.IsBattleActive)
            {
                ObserveEffect(index, "Skipped", "BattleInactive");
                return;
            }
            if (_types[index] == SpecialKeycapType.Bonus)
            {
                _score.ActivateBonus(3f);
                ObserveEffect(index, "Activated", "");
            }
            else
            {
                _pending.Enqueue(index);
                ObserveEffect(index, "Queued", "");
            }
        }

        void LateUpdate()
        {
            if (!_tournament.IsBattleActive) { CancelPendingEffects("BattleInactive"); return; }
            while (_pending.Count > 0 && !_destruction.IsBroken)
            {
                int center = _pending.Dequeue();
                SpecialKeycapType type = _types[center];
                if (type == SpecialKeycapType.All)
                {
                    bool started = false;
                    _feedback.PlayHammerShockwave(_keyboard, _keycaps, _positions, center, ApplyWaveDamage, false, false,
                        () => { started = true; ObserveEffect(center, "Activated", ""); }, travelDuration: 2f);
                    if (!started) ObserveEffect(center, "Skipped", "WaveUnavailable");
                    continue;
                }
                if (type == SpecialKeycapType.Explosion)
                {
                    _delayedExplosions.Add(center);
                    StartCoroutine(ExplodeAfterDelay(center));
                    continue;
                }
                PlayAreaWave(center, type);
            }
        }

        /// <summary>
        /// center 키캡의 파괴 효과를 0.25초 지연한 뒤 주변 12칸에 피해 4를 적용한다.
        /// 대전 종료나 완파 시 취소를 기록하고 지연 중인 폭발 목록에서 제거한다.
        /// </summary>
        private IEnumerator ExplodeAfterDelay(int center)
        {
            float elapsed = 0f;
            while (elapsed < 0.25f && _tournament.IsBattleActive && !_destruction.IsBroken)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            _delayedExplosions.Remove(center);
            if (!_tournament.IsBattleActive || _destruction.IsBroken)
            {
                ObserveEffect(center, "Cancelled", !_tournament.IsBattleActive ? "BattleInactive" : "KeyboardBroken");
                yield break;
            }
            PlayAreaWave(center, SpecialKeycapType.Explosion);
        }

        /// <summary>
        /// center와 type으로 주변 12칸 또는 같은 행열을 선정하여 해당 범위에만 물결과 도달 피해를 적용한다.
        /// 폭발은 0.35초에 피해 4, 행열은 0.45초에 피해 3을 전달하고 실제 시작 여부를 로그에 기록한다.
        /// </summary>
        private void PlayAreaWave(int center, SpecialKeycapType type)
        {
            List<KeycapHealth> targets = new List<KeycapHealth> { _keycaps[center] };
            List<Vector2> positions = new List<Vector2> { _positions[center] };
            if (type == SpecialKeycapType.Explosion)
            {
                int count = Mathf.Min(12, _neighbors.GetNeighborCount(center));
                for (int index = 0; index < count; index++)
                {
                    int neighbor = _neighbors.GetNeighbor(center, index);
                    targets.Add(_keycaps[neighbor]);
                    positions.Add(_positions[neighbor]);
                }
            }
            else
            {
                for (int index = 0; index < _keycaps.Length; index++)
                {
                    bool sameLine = type == SpecialKeycapType.Row
                        ? _coordinates[index].x == _coordinates[center].x
                        : _coordinates[index].y == _coordinates[center].y;
                    if (index == center || !sameLine) continue;
                    targets.Add(_keycaps[index]);
                    positions.Add(_positions[index]);
                }
            }
            int damage = type == SpecialKeycapType.Explosion ? 4 : 3;
            bool started = false;
            _feedback.PlayHammerShockwave(_keyboard, targets.ToArray(), positions.ToArray(), 0,
                keycap => ApplyDamage(_indices[keycap], damage), false, false,
                () => { started = true; ObserveEffect(center, "Activated", ""); },
                travelDuration: type == SpecialKeycapType.Explosion ? 0.35f : 0.45f);
            if (!started) ObserveEffect(center, "Skipped", "WaveUnavailable");
        }
        /// <summary>index의 살아 있는 키에 damage를 적용하며 종료된 대전과 완파 상태의 지연 피해는 무시한다.</summary>
        private void ApplyDamage(int index, int damage)
        {
            if (!_tournament.IsBattleActive || _destruction.IsBroken || _keycaps[index].CurrentHP <= 0) return;
            _keycaps[index].TakeDamage(damage);
        }

        /// <summary>충격파가 도달한 keycap에 피해 2를 적용하며 종료된 대전에는 피해를 남기지 않는다.</summary>
        private void ApplyWaveDamage(KeycapHealth keycap)
        {
            ApplyDamage(_indices[keycap], 2);
        }

        /// <summary>
        /// keycap의 실제 배치 유형을 반환한다.
        /// 초기화된 인덱스를 읽으며 배치나 난수 상태는 변경하지 않는다.
        /// </summary>
        public SpecialKeycapType GetPlaytestType(KeycapHealth keycap) { return _types[_indices[keycap]]; }

        /// <summary>
        /// keycap에 실제 적용한 행과 열 좌표를 반환한다.
        /// 기존 배치 배열을 읽으며 키보드 상태는 변경하지 않는다.
        /// </summary>
        public Vector2Int GetPlaytestCoordinates(KeycapHealth keycap) { return _coordinates[_indices[keycap]]; }

        /// <summary>
        /// index의 기믹 파괴 또는 발동 상태를 action과 reason으로 알린다.
        /// 실제 키와 유형 및 효과 식별자를 전달하며 피해나 점수를 계산하지 않는다.
        /// </summary>
        private void ObserveEffect(int index, string action, string reason)
        {
            PlaytestEffectObserved?.Invoke(this, _keys[index], _types[index], action, reason, _effectIds[index]);
        }

        /// <summary>
        /// 실행되지 못한 대기 효과를 reason의 취소 이벤트로 알리고 비운다.
        /// 입력 사유만 기록하며 기존 대기 효과의 폐기 동작을 유지한다.
        /// </summary>
        private void CancelPendingEffects(string reason)
        {
            while (_pending.Count > 0) ObserveEffect(_pending.Dequeue(), "Cancelled", reason);
        }

        /// <summary>
        /// 새 실행에서 기믹 효과 관찰 이벤트를 초기화한다.
        /// 입력값 없이 이전 플레이 실행의 정적 구독을 제거한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaytestObservers() { PlaytestEffectObserved = null; }

        void OnDestroy()
        {
            CancelPendingEffects("KeyboardDestroyed");
            foreach (int center in _delayedExplosions) ObserveEffect(center, "Cancelled", "KeyboardDestroyed");
            _delayedExplosions.Clear();
            foreach (KeycapHealth keycap in _indices.Keys)
                if (keycap != null) keycap.Broken -= HandleBroken;
        }
    }
}
