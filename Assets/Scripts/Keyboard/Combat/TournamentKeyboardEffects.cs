using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using KeyboardModeling;

namespace Game.Tournament
{
    public sealed class TournamentKeyboardEffects : MonoBehaviour
    {
        public static event Action<TournamentKeyboardEffects, Key, KeycapDefinition, string, string, string> PlaytestEffectObserved;
        public static event Action<TournamentKeyboardEffects, Key, int, int, int, int, int> PlaytestPassiveDamageObserved;
        public static event Action<TournamentKeyboardEffects> PlaytestPassiveLayoutChanged;

        private struct Activation
        {
            public int Index;
            public string Id;
            public KeycapAbilityDefinition Ability;
            public int Level;
        }

        [Header("Workshop Preview")]
        [SerializeField] private TournamentKeyLayout _workshopLayout;

        [Header("Runtime")]
        private readonly Queue<Activation> _pending = new Queue<Activation>();
        private readonly Dictionary<string, int> _delayedEffects = new Dictionary<string, int>();
        private readonly Dictionary<KeycapHealth, int> _indices = new Dictionary<KeycapHealth, int>();
        private TournamentController _tournament;
        private KeyboardCustomization _workshopCustomization;
        private KeycapDefinition[] _parts;
        private int[] _levels;
        private int[] _passiveBonuses;
        private ArtisanPassiveLayout _passiveLayout;
        private KeyboardPassiveIndicator _passiveIndicator;
        private bool _workshopWasActive;
        private bool _workshopDirty = true;
        private KeyboardInputController _keyboard;
        private KeyboardDestruction _destruction;
        private KeycapHealth[] _keycaps;
        private KeycapAbilityDefinition[] _abilities;
        private Key[] _keys;
        private bool _statusesCleared;
        private Vector2Int[] _coordinates;
        private Vector2[] _positions;

        private bool IsExecutionActive => isActiveAndEnabled && (_tournament != null ? _tournament.IsBattleActive
            : _keyboard != null && _keyboard.AttackEnabled && _keyboard.InputEnabled);

        public KeyboardInputController PlaytestKeyboard => _keyboard;
        public TournamentController PlaytestTournament => _tournament;
        public ArtisanPassiveLayout PlaytestPassiveLayout => _passiveLayout;

        /// <summary>index에 바인딩된 실제 입력 키를 반환한다. 패시브 제공 위치와 대상 위치의 로그 연결에 사용한다.</summary>
        public Key GetPlaytestKey(int index) { return _keys[index]; }

        /// <summary>index에 확정된 파츠 정의를 반환한다. 외형과 능력 유무를 별도로 기록하며 정의가 없으면 null이다.</summary>
        public KeycapDefinition GetPlaytestPart(int index) { return _parts[index]; }

        /// <summary>index의 현재 능력 단계를 반환한다. 초기화된 전투 상태만 읽으며 능력이나 재고를 변경하지 않는다.</summary>
        public int GetPlaytestLevel(int index) { return _levels[index]; }

        /// <summary>health의 실제 바인딩 인덱스를 반환한다. 입력 로그를 현재 전투 구성과 연결한다.</summary>
        public int GetPlaytestIndex(KeycapHealth health) { return _indices[health]; }

        /// <summary>
        /// tournament와 inventory, score 및 layout으로 새 키보드의 체력, 특수 유형과 행열을 구성한다.
        /// 장착 SO와 위치별 해금 단계를 읽어 이벤트와 패시브를 구성하며 외형과 구매 재고는 유지한다.
        /// </summary>
        public void Initialize(TournamentController tournament, TournamentInventory inventory, KeyboardScoreCombo score, TournamentKeyLayout layout, int health)
        {
            if (_keyboard != null)
            {
                _keyboard.KeycapsRestored -= HandleKeycapsRestored;
                _keyboard.AttackModeChanged -= HandleWorkshopAttackMode;
            }
            if (_workshopCustomization != null) _workshopCustomization.KeycapDefinitionChanged -= HandleWorkshopDefinitionChanged;
            _workshopCustomization = null;
            _tournament = tournament;

            _keyboard = GetComponent<KeyboardInputController>();
            _keyboard.UseAllDestructionKeys();
            _destruction = GetComponent<KeyboardDestruction>();
            _destruction.ConfigureRequiredBrokenCount(tournament.Settings.GetRequiredBrokenCount(tournament.Round));
            InitializeBindings(layout);
            for (int index = 0; index < _keycaps.Length; index++)
            {
                _keycaps[index].ConfigureTournamentHealth(health);
                if (inventory.InstalledKeycaps.TryGetValue(_keys[index], out KeycapDefinition part))
                {
                    _parts[index] = part;
                    _abilities[index] = part.Ability;
                    _levels[index] = inventory.GetKeycapLevel(_keys[index]);
                }
            }
            ApplyPassives();
            GetComponent<KeyboardAbilityOutline>().ConfigureInventory(inventory);
        }

        void Start()
        {
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive)
            {
                // 순수 커마 씬에서는 아티산 전투 능력과 피해 이벤트를 연결하지 않는다.
                // InitializeWorkshop();
                enabled = false;
                return;
            }
            InitializeWorkshop();
        }

        /// <summary>작업실 레이아웃이 설정된 키보드의 능력 구독을 한 번 준비한다. 입력이 꺼진 동안에는 능력을 실행하지 않는다.</summary>
        private void InitializeWorkshop()
        {
            if (_keycaps != null || _tournament != null || _workshopLayout == null) return;
            _keyboard = GetComponent<KeyboardInputController>();
            _destruction = GetComponent<KeyboardDestruction>();
            _workshopCustomization = GetComponent<KeyboardCustomization>();
            InitializeBindings(_workshopLayout);
            _keyboard.KeycapsRestored += HandleKeycapsRestored;
            _keyboard.AttackModeChanged += HandleWorkshopAttackMode;
            _workshopCustomization.KeycapDefinitionChanged += HandleWorkshopDefinitionChanged;
        }

        /// <summary>layout의 키별 행열과 입력 바인딩을 캐시하고 피해·파괴 이벤트를 연결한다. 체력과 장착 부품은 변경하지 않는다.</summary>
        private void InitializeBindings(TournamentKeyLayout layout)
        {
            _passiveIndicator = GetComponent<KeyboardPassiveIndicator>();
            if (_keycaps != null)
            {
                ClearExecution("Reinitialized");
                foreach (KeycapHealth keycap in _indices.Keys)
                {
                    keycap.Broken -= HandleBroken;
                    keycap.HitReceived -= HandleHit;
                    keycap.PassiveDamageObserved -= HandlePassiveDamage;
                }
                _indices.Clear();
            }
            IReadOnlyDictionary<Key, KeycapHealth> bindings = _keyboard.GetKeycapLayout();
            Dictionary<Key, Vector2Int> coordinates = layout.CreateCoordinates();
            _keycaps = new KeycapHealth[bindings.Count];
            _abilities = new KeycapAbilityDefinition[bindings.Count];
            _keys = new Key[bindings.Count];
            _coordinates = new Vector2Int[bindings.Count];
            _positions = new Vector2[bindings.Count];
            _parts = new KeycapDefinition[bindings.Count];
            _levels = new int[bindings.Count];
            int index = 0;
            foreach (KeyValuePair<Key, KeycapHealth> binding in bindings)
            {
                _keycaps[index] = binding.Value;
                _keys[index] = binding.Key;
                _coordinates[index] = coordinates[binding.Key];
                Vector3 position = transform.InverseTransformPoint(binding.Value.transform.position);
                _positions[index] = new Vector2(position.x, position.z);
                binding.Value.Broken += HandleBroken;
                binding.Value.HitReceived += HandleHit;
                binding.Value.PassiveDamageObserved += HandlePassiveDamage;
                _indices.Add(binding.Value, index++);
            }
        }

        /// <summary>작업실 키캡 복구가 끝나면 예약된 피해와 연출을 취소한다. 기존 능력 구독과 장착 SO는 유지한다.</summary>
        private void HandleKeycapsRestored()
        {
            ClearExecution("KeycapsRestored");
            _workshopDirty = true;
            if (IsExecutionActive) RefreshWorkshopAbilities();
        }

        /// <summary>작업실의 키캡 SO 변경을 받아 최대 단계 패시브를 갱신 대상으로 표시한다. 입력 구성과 저장 데이터는 변경하지 않는다.</summary>
        private void HandleWorkshopDefinitionChanged() { _workshopDirty = true; }

        /// <summary>작업실 공격 모드 active에 따라 최대 단계 시연을 준비하거나 효과와 패시브를 정리한다. 원본 부품 단계는 저장하지 않는다.</summary>
        private void HandleWorkshopAttackMode(bool active)
        {
            if (active) RefreshWorkshopAbilities();
            else
            {
                ClearExecution("PreviewEnded");
            }
            _workshopWasActive = active;
            GetComponent<KeyboardAbilityOutline>().SetPreview(active);
        }

        /// <summary>작업실의 현재 장착 SO를 읽어 능력을 최대 단계로 구성한다. 무작위 패시브 기준과 직접 피해를 갱신하며 외형은 유지한다.</summary>
        private void RefreshWorkshopAbilities()
        {
            for (int index = 0; index < _keycaps.Length; index++)
            {
                _parts[index] = _keycaps[index].gameObject.activeInHierarchy
                    ? _workshopCustomization.GetKeycapDefinition(_keycaps[index].transform) : null;
                _abilities[index] = _parts[index] != null ? _parts[index].Ability : null;
                _levels[index] = _abilities[index] != null ? TournamentInventory.MaxLevel : 0;
            }
            ApplyPassives();
            _workshopDirty = false;
        }

        /// <summary>장착·해금된 아티산의 단계별 패시브를 키보드에 합산한다. 무작위 기준은 이 초기화마다 다시 선정하며 아티산이 파괴되어도 보충 전까지 유지한다.</summary>
        private void ApplyPassives()
        {
            var installed = new bool[_keycaps.Length];
            for (int index = 0; index < installed.Length; index++) installed[index] = _keycaps[index].gameObject.activeInHierarchy;
            var halfSizes = new Vector2[_keycaps.Length];
            for (int index = 0; index < _keycaps.Length; index++)
            {
                BoxCollider collider = _keycaps[index].GetComponent<BoxCollider>();
                Vector3 x = transform.InverseTransformVector(collider.transform.TransformVector(Vector3.right * collider.size.x * 0.5f));
                Vector3 z = transform.InverseTransformVector(collider.transform.TransformVector(Vector3.forward * collider.size.z * 0.5f));
                halfSizes[index] = new Vector2(Mathf.Abs(x.x), Mathf.Abs(z.z));
            }
            ArtisanPassiveLayout layout = ArtisanPassiveLayout.Build(_parts, _levels, _coordinates, _positions, installed, halfSizes);
            _passiveLayout = layout;
            _passiveBonuses = layout.Bonuses;
            for (int index = 0; index < _keycaps.Length; index++) _keycaps[index].ConfigurePassiveDamage(_passiveBonuses[index]);
            _passiveIndicator.Configure(layout, _keycaps);
            _passiveIndicator.SetVisible(IsExecutionActive);
            if (_tournament != null) PlaytestPassiveLayoutChanged?.Invoke(this);
        }

        /// <summary>health에 실제 적용된 패시브 피해 관찰값을 전투 로거로 전달한다. 계산값과 HP 감소 및 패시브 제외 차이를 분리하며 게임 상태는 변경하지 않는다.</summary>
        private void HandlePassiveDamage(KeycapHealth health, int bonus, int calculated, int applied, int calculatedExtra, int appliedExtra)
        {
            if (_tournament != null)
                PlaytestPassiveDamageObserved?.Invoke(this, _keys[_indices[health]], bonus, calculated, applied, calculatedExtra, appliedExtra);
        }

        /// <summary>keycap 파괴를 받아 장착 능력을 예약한다. 복구 후에도 같은 구독으로 재발동하며 실행별 관찰 ID를 생성한다.</summary>
        private void HandleBroken(KeycapHealth keycap)
        {
            QueueActivation(_indices[keycap], "Broken");
        }

        /// <summary>keycap의 kind가 직접 타격일 때 능력을 예약한다. 능력 피해로 피격 능력이 서로 재귀 발동하지 않도록 제한한다.</summary>
        private void HandleHit(KeycapHealth keycap, KeycapHitKind kind)
        {
            if (kind == KeycapHitKind.Direct) QueueActivation(_indices[keycap], "Hit");
        }

        /// <summary>index의 능력을 trigger 관찰 정보와 함께 예약한다. 실행마다 독립된 ID를 만들며 비활성 대전은 예약하지 않는다.</summary>
        private void QueueActivation(int index, string trigger)
        {
            if (_workshopCustomization != null)
            {
                _parts[index] = _workshopCustomization.GetKeycapDefinition(_keycaps[index].transform);
                _abilities[index] = _parts[index] != null ? _parts[index].Ability : null;
                _levels[index] = TournamentInventory.MaxLevel;
            }
            if (_levels[index] <= 0 || (_parts[index] != null && _parts[index].IsArtisan)) return;
            KeycapAbilityDefinition ability = _abilities[index];
            if (ability == null || ability.Target == KeycapTargetPattern.None
                || (ability.Trigger == KeycapAbilityTrigger.Broken) != (trigger == "Broken")) return;
            var activation = new Activation { Index = index, Id = Guid.NewGuid().ToString("N"), Ability = ability, Level = _levels[index] };
            ObserveEffect(activation, trigger, "");
            if (!IsExecutionActive) { ObserveEffect(activation, "Skipped", "BattleInactive"); return; }
            _statusesCleared = false;
            _pending.Enqueue(activation);
            ObserveEffect(activation, "Queued", "");
        }

        void LateUpdate()
        {
            if (Game.Tournament.TournamentController.IsReturnPaused) return;
            if (_keycaps == null) return;
            if (_workshopCustomization != null)
            {
                bool active = IsExecutionActive;
                if (active && (!_workshopWasActive || _workshopDirty)) RefreshWorkshopAbilities();
                _workshopWasActive = active;
                GetComponent<KeyboardAbilityOutline>().SetPreview(active);
            }
            if (!IsExecutionActive || _destruction.IsBroken)
            {
                if (!_statusesCleared) ClearExecution(!IsExecutionActive ? "BattleInactive" : "KeyboardBroken");
                return;
            }
            if (_statusesCleared && _passiveBonuses != null)
                for (int index = 0; index < _keycaps.Length; index++) _keycaps[index].ConfigurePassiveDamage(_passiveBonuses[index]);
            _passiveIndicator.SetVisible(true);
            _statusesCleared = false;
            int starts = 0;
            while (_pending.Count > 0 && !_destruction.IsBroken && starts++ < 8)
            {
                Activation activation = _pending.Dequeue();
                _delayedEffects.Add(activation.Id, activation.Index);
                StartCoroutine(ExecuteAbility(activation));
            }
        }

        /// <summary>activation의 지연, 전파와 반복을 실행한다. 모든 대기는 deltaTime을 사용하고 대전 종료 또는 완파 시 후속 피해와 연출을 중단한다.</summary>
        private IEnumerator ExecuteAbility(Activation activation)
        {
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
            int center = activation.Index;
            KeycapAbilityDefinition ability = activation.Ability;
            if (ability.Delay > 0f && ability.WindupEffectPrefab != null)
            {
                Vector3 position = GetSurfacePosition(center);
                KeycapAbilityVfx.Play(ability.WindupEffectPrefab, transform, position, position, ability.Delay + 0.08f, 0.65f);
            }
            float elapsed = 0f;
            while (elapsed < ability.Delay)
            {
                if (!CanContinue(activation)) yield break;
                yield return null;
                while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
                elapsed += Time.deltaTime;
            }
            for (int repeat = 0; repeat < ability.RepeatCount; repeat++)
            {
                if (!CanContinue(activation)) yield break;
                List<int> targets = KeycapAbilityTargeting.Select(ability, center, _coordinates, _positions, _keycaps);
                if (ability.AlternateDirection && repeat % 2 == 1 && ability.Propagation != KeycapPropagation.Distance) targets.Reverse();
                float[] arrivals = KeycapAbilityTargeting.GetArrivalTimes(ability, center, targets, _positions);
                PlayActivationEffect(activation);
                int reached = 0;
                int previous = center;
                elapsed = 0f;
                while (reached < targets.Count)
                {
                    if (!CanContinue(activation)) yield break;
                    while (reached < targets.Count && arrivals[reached] <= elapsed)
                    {
                        int target = targets[reached++];
                        if (_keycaps[target].gameObject.activeInHierarchy && _keycaps[target].CurrentHP > 0)
                        {
                            Vector3 source = GetSurfacePosition(ability.Propagation == KeycapPropagation.NearestChain ? previous : center);
                            Vector3 destination = GetSurfacePosition(target);
                            KeycapAbilityVfx.Play(ability.ImpactEffectPrefab, transform, source, destination,
                                ability.ImpactLifetime, keyboard: _keyboard, keycap: _keycaps[target].transform);
                            ApplyEffects(target, ability, activation.Level);
                        }
                        previous = target;
                        if (!CanContinue(activation)) yield break;
                    }
                    if (reached < targets.Count) { yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null; elapsed += Time.deltaTime; }
                }
                if (repeat + 1 < ability.RepeatCount)
                {
                    elapsed = 0f;
                    while (elapsed < ability.RepeatInterval)
                    {
                        if (!CanContinue(activation)) yield break;
                        yield return null;
                        while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
                        elapsed += Time.deltaTime;
                    }
                }
            }
            _delayedEffects.Remove(activation.Id);
            ObserveEffect(activation, "Completed", "");
        }

        /// <summary>activation의 대전 상태와 완파 여부를 검사한다. 중단된 실행은 한 번 취소를 기록하고 활성 목록에서 제거한다.</summary>
        private bool CanContinue(Activation activation)
        {
            if (IsExecutionActive && !_destruction.IsBroken) return true;
            if (_delayedEffects.Remove(activation.Id))
                ObserveEffect(activation, "Cancelled", !IsExecutionActive ? "BattleInactive" : "KeyboardBroken");
            return false;
        }

        /// <summary>index 키캡의 현재 모델 상단을 월드 좌표로 반환한다. 모델 크기와 키보드 회전을 반영해 표면 위에서 연출을 시작한다.</summary>
        private Vector3 GetSurfacePosition(int index)
        {
            Transform shell = _keycaps[index].transform.Find("PBT_SculptedShell");
            Bounds bounds = shell.GetComponent<MeshFilter>().sharedMesh.bounds;
            return shell.TransformPoint(new Vector3(bounds.center.x, bounds.max.y + 0.12f, bounds.center.z));
        }

        /// <summary>activation의 발동을 기록하고 키캡별 프리팹을 재생한다. 연출 한도에 도달해도 능력 판정은 계속 실행한다.</summary>
        private void PlayActivationEffect(Activation activation)
        {
            ObserveEffect(activation, "Activated", "");
            KeycapAbilityDefinition ability = activation.Ability;
            Vector3 position = GetSurfacePosition(activation.Index);
            KeycapAbilityVfx.Play(ability.ActivationEffectPrefab, transform, position, position, ability.EffectLifetime, ability.EffectScale);
        }

        /// <summary>살아 있는 index 키캡에 ability의 피해 또는 상태 효과 목록을 순서대로 적용한다. 능력 피해로 구분해 추가 피해의 재귀 호출과 피격 능력 재발동을 방지한다.</summary>
        private void ApplyEffects(int index, KeycapAbilityDefinition ability, int level)
        {
            if (!IsExecutionActive || _destruction.IsBroken || !_keycaps[index].gameObject.activeInHierarchy || _keycaps[index].CurrentHP <= 0) return;
            foreach (KeycapAbilityEffect effect in ability.Effects)
            {
                if (_destruction.IsBroken || _keycaps[index].CurrentHP <= 0) break;
                if (effect.Kind == KeycapEffectKind.Damage) _keycaps[index].TakeDamage(effect.Damage * level, KeycapHitKind.Ability);
                else _keycaps[index].ApplyStatus(effect.Status, level);
            }
        }

        /// <summary>
        /// keycap의 실제 배치 유형을 반환한다.
        /// 대전은 초기 배치, 작업실은 현재 장착 SO를 읽으며 필요하면 작업실 바인딩을 준비한다.
        /// </summary>
        public string GetPlaytestType(KeycapHealth keycap)
        {
            InitializeWorkshop();
            KeycapDefinition part = _workshopCustomization != null ? _workshopCustomization.GetKeycapDefinition(keycap.transform) : _parts[_indices[keycap]];
            return part != null ? part.Id : "keycap-aluminum";
        }

        /// <summary>
        /// keycap에 실제 적용한 행과 열 좌표를 반환한다.
        /// 필요하면 작업실 바인딩을 준비하고 기존 배치 배열의 좌표를 반환한다.
        /// </summary>
        public Vector2Int GetPlaytestCoordinates(KeycapHealth keycap) { InitializeWorkshop(); return _coordinates[_indices[keycap]]; }

        /// <summary>
        /// activation의 기믹 발동 상태를 action과 reason으로 알린다.
        /// 실제 키와 유형 및 효과 식별자를 전달하며 피해나 점수를 계산하지 않는다.
        /// </summary>
        private void ObserveEffect(Activation activation, string action, string reason)
        {
            if (_tournament == null) return;
            int index = activation.Index;
            PlaytestEffectObserved?.Invoke(this, _keys[index], _parts[index], action, reason, activation.Id);
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
        private static void ResetPlaytestObservers() { PlaytestEffectObserved = null; PlaytestPassiveDamageObserved = null; PlaytestPassiveLayoutChanged = null; }

        /// <summary>reason을 기록하며 모든 대기·실행 능력과 상태 및 연출을 정리한다. 키캡 체력과 장착 구성은 유지한다.</summary>
        private void ClearExecution(string reason)
        {
            CancelPendingEffects(reason);
            foreach (var effect in _delayedEffects)
                ObserveEffect(new Activation { Index = effect.Value, Id = effect.Key }, "Cancelled", reason);
            _delayedEffects.Clear();
            StopAllCoroutines();
            KeycapAbilityVfx.CancelFor(transform);
            if (_passiveIndicator != null) _passiveIndicator.SetVisible(false);
            foreach (KeycapHealth keycap in _indices.Keys)
                if (keycap != null) { keycap.ClearStatuses(); keycap.ConfigurePassiveDamage(0); }
            _statusesCleared = true;
        }

        void OnDisable()
        {
            ClearExecution("KeyboardDisabled");
        }

        void OnDestroy()
        {
            ClearExecution("KeyboardDestroyed");
            if (_keyboard != null)
            {
                _keyboard.KeycapsRestored -= HandleKeycapsRestored;
                _keyboard.AttackModeChanged -= HandleWorkshopAttackMode;
            }
            if (_workshopCustomization != null) _workshopCustomization.KeycapDefinitionChanged -= HandleWorkshopDefinitionChanged;
            foreach (KeycapHealth keycap in _indices.Keys)
                if (keycap != null)
                {
                    keycap.Broken -= HandleBroken;
                    keycap.HitReceived -= HandleHit;
                    keycap.PassiveDamageObserved -= HandlePassiveDamage;
                }
        }
    }
}
