using Game.Session;
using Game.Upgrades;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardAutoAttackController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private KeyboardTypingArmController _arm;
        [SerializeField] private KeyboardAutomaticStation _station;
        [SerializeField] private KeyboardFeedbackManager _stationFeedback;
        private GameSession _gameSession;
        private KeyboardInteractionController _interactionController;
        private UpgradeService _upgrades;
        private KeyboardInputController _keyboard;
        private KeyboardDestruction _destruction;
        private KeyboardAutoAttackManager _manager;

        [Header("Automatic Attack")]
        [Tooltip("기본 모델에서 흰색인 키의 식별자입니다. 실행 중 등급 색상은 대상 판정에 사용하지 않습니다.")]
        [SerializeField] private Key[] _eligibleKeys;
        private readonly List<KeycapHealth> _targets = new List<KeycapHealth>();
        private KeycapHealth[] _areaTargets = System.Array.Empty<KeycapHealth>();
        private Vector2[] _areaPositions = System.Array.Empty<Vector2>();
        private double _nextAttackAt;
        private float _attackInterval;
        private bool _unlocked;
        private string _lastDiagnosticState;
        private int _diagnosticAttackCount;
        private double _nextDiagnosticAt;

        void Awake()
        {
            _manager = KeyboardAutoAttackManager.Instance;
            _gameSession = _manager.GameSession;
            _interactionController = _manager.InteractionController;
            _attackInterval = 1f;
        }

        void OnEnable()
        {
            if (_station != null)
            {
                _station.KeyboardChanged += BindKeyboard;
                BindKeyboard(_station.Keyboard);
            }
            else
            {
                _interactionController.PlacedKeyboardChanged += BindKeyboard;
                BindKeyboard(_interactionController.PlacedKeyboard);
            }
            if (_upgrades != null)
            {
                _upgrades.LevelChanged += HandleUpgradeChanged;
                SetUnlocked(_upgrades.GetLevel(UpgradeId.AutoClick) > 0);
            }
        }

        void Start()
        {
            _upgrades = _gameSession.Upgrades;
            _upgrades.LevelChanged += HandleUpgradeChanged;
            RefreshAttackInterval();
            SetUnlocked(_upgrades.GetLevel(UpgradeId.AutoClick) > 0);
        }

        void OnDisable()
        {
            Debug.Log($"[AutoAttack][Disabled] Controller={name}", this);
            if (_station != null)
                _station.KeyboardChanged -= BindKeyboard;
            else
                _interactionController.PlacedKeyboardChanged -= BindKeyboard;
            if (_upgrades != null)
                _upgrades.LevelChanged -= HandleUpgradeChanged;
            BindKeyboard(null);
        }

        void Update()
        {
            RefreshAttackInterval();
            LogAttackState();
            if (_keyboard != null && Time.unscaledTimeAsDouble >= _nextDiagnosticAt)
            {
                _nextDiagnosticAt = Time.unscaledTimeAsDouble + 5d;
                Debug.Log($"[AutoAttack][Heartbeat] Controller={name}, Hits={_diagnosticAttackCount}, Interval={_attackInterval:F3}s, Wait={System.Math.Max(0d, _nextAttackAt - Time.unscaledTimeAsDouble):F3}s, ArmId={_arm.GetInstanceID()}, Pressing={_arm.IsPressing}, Targets={_targets.Count}, TimeScale={Time.timeScale}", this);
            }
            if (!_unlocked || Time.unscaledTimeAsDouble < _nextAttackAt)
                return;
            if (_keyboard == null || (_destruction != null && _destruction.IsBroken))
            {
                AdvanceAttackClock();
                return;
            }
            for (int index = _targets.Count - 1; index >= 0; index--)
            {
                if (_targets[index] == null || _targets[index].CurrentHP <= 0)
                    _targets.RemoveAt(index);
            }
            if (_targets.Count > 0)
            {
                int first = Random.Range(0, _targets.Count);
                for (int offset = 0; offset < _targets.Count; offset++)
                {
                    if (AttackKeycap(_targets[(first + offset) % _targets.Count]))
                        return;
                }
            }
            AdvanceAttackClock();
        }

        /// <summary>
        /// 자동공격으로 지정한 살아 있는 키캡을 한 번 타격한다.
        /// target을 전용 기계팔 함수와 기존 피드백에 전달한 뒤 단일 피해를 적용하며 실제 타격 성공 여부를 반환한다.
        /// </summary>
        public bool AttackKeycap(KeycapHealth target)
        {
            RefreshAttackInterval();
            if (!_unlocked || Time.unscaledTimeAsDouble < _nextAttackAt || _keyboard == null || target == null || target.CurrentHP <= 0 ||
                (_destruction != null && _destruction.IsBroken) || !_targets.Contains(target))
                return false;
            if (!_arm.PressKeyAutomatically(target.transform))
            {
                if (_lastDiagnosticState != "ArmRejected")
                {
                    Debug.LogWarning($"[AutoAttack][ArmRejected] Controller={name}, Key={target.name}, Reason={_arm.LastPressFailure}", this);
                    _lastDiagnosticState = "ArmRejected";
                }
                return false;
            }
            if (_lastDiagnosticState != "Attacking")
            {
                Debug.Log($"[AutoAttack][Attacking] Controller={name}, Keyboard={_keyboard.name}, Key={target.name}", this);
                _lastDiagnosticState = "Attacking";
            }
            KeyboardInputController keyboard = _keyboard;
            KeyboardDestruction destruction = _destruction;
            KeycapHealth[] areaTargets = _areaTargets;
            Vector2[] areaPositions = _areaPositions;
            int index = System.Array.IndexOf(areaTargets, target);
            int damage = _upgrades.GetAutoAttackDamage();
            float radius = _upgrades.GetAutoAttackRadius();
            AdvanceAttackClock();
            keyboard.NotifyAutomaticKeycapHit(target.transform);
            int previousHP = target.CurrentHP;
            target.TakeDamage(damage);
            _diagnosticAttackCount++;
            if (_diagnosticAttackCount <= 3)
                Debug.Log($"[AutoAttack][DamageApplied] Controller={name}, Key={target.name}, HP={previousHP}->{target.CurrentHP}, Damage={damage}, NextWait={System.Math.Max(0d, _nextAttackAt - Time.unscaledTimeAsDouble):F3}s", this);
            if (index >= 0)
                KeyboardAttackController.ApplyAreaDamage(keyboard, destruction, areaTargets, areaPositions, index, areaPositions[index], radius, damage);
            return true;
        }

        /// <summary>
        /// 교체된 키보드에서 기본 흰색 영역의 키캡만 보관한다.
        /// keyboard와 _eligibleKeys로 새 대상 목록을 만들고 이전 대상을 해제하며 공격 시각은 유지한다.
        /// </summary>
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            _lastDiagnosticState = null;
            Debug.Log($"[AutoAttack][BindBegin] Controller={name}, Station={(_station != null ? _station.name : "Manual")}, Keyboard={(keyboard != null ? keyboard.name : "None")}", this);
            _keyboard = keyboard;
            _destruction = null;
            _targets.Clear();
            _areaTargets = System.Array.Empty<KeycapHealth>();
            _areaPositions = System.Array.Empty<Vector2>();
            if (_stationFeedback != null)
            {
                Debug.Log($"[AutoAttack][FeedbackBegin] Controller={name}", this);
                _stationFeedback.SetAutomaticKeyboard(_keyboard);
                Debug.Log($"[AutoAttack][FeedbackEnd] Controller={name}", this);
            }
            if (_keyboard == null)
                return;
            _destruction = _keyboard.GetComponent<KeyboardDestruction>();
            _areaTargets = _keyboard.GetComponentsInChildren<KeycapHealth>(true);
            _areaPositions = new Vector2[_areaTargets.Length];
            for (int index = 0; index < _areaTargets.Length; index++)
            {
                Vector3 position = _keyboard.transform.InverseTransformPoint(_areaTargets[index].transform.position);
                _areaPositions[index] = new Vector2(position.x, position.z);
            }
            IReadOnlyDictionary<Key, KeycapHealth> layout = _keyboard.GetKeycapLayout();
            foreach (Key key in _eligibleKeys)
            {
                if (layout.TryGetValue(key, out KeycapHealth target) && target.CurrentHP > 0)
                    _targets.Add(target);
            }
            Debug.Log($"[AutoAttack][BindEnd] Controller={name}, Targets={_targets.Count}, ArmActive={_arm.isActiveAndEnabled}, Unlocked={_unlocked}, Wait={System.Math.Max(0d, _nextAttackAt - Time.unscaledTimeAsDouble):F3}s", this);
        }

        /// <summary>
        /// 현재 공격 조건을 확인하여 상태가 바뀔 때만 진단 로그를 출력한다.
        /// 키보드와 공격 예약 및 활성 상태를 사용하며 진단 상태만 저장하고 공격 동작은 변경하지 않는다.
        /// </summary>
        private void LogAttackState()
        {
            string state = !_unlocked ? "Locked"
                : _keyboard == null ? "NoKeyboard"
                : _destruction != null && _destruction.IsBroken ? "Broken"
                : _targets.Count == 0 ? "NoTargets"
                : Time.unscaledTimeAsDouble < _nextAttackAt ? "Waiting" : "Ready";
            if (state == "Ready" || (state == "Waiting" && (_lastDiagnosticState == "Attacking" || _lastDiagnosticState == "ArmRejected"))) return;
            if (_lastDiagnosticState == state) return;
            _lastDiagnosticState = state;
            Debug.Log($"[AutoAttack][State] Controller={name}, State={state}, Targets={_targets.Count}, ArmActive={_arm.isActiveAndEnabled}, Wait={System.Math.Max(0d, _nextAttackAt - Time.unscaledTimeAsDouble):F3}s", this);
        }

        /// <summary>
        /// 자동공격 강화의 구매 상태를 적용한다.
        /// id와 level이 AutoClick 강화이면 첫 구매부터 기계팔과 자동공격을 활성화한다.
        /// </summary>
        private void HandleUpgradeChanged(UpgradeId id, int level)
        {
            if (id == UpgradeId.AutoClick)
            {
                RefreshAttackInterval();
                SetUnlocked(level > 0);
            }
        }

        /// <summary>
        /// 강화 여부에 따라 자동공격과 기계팔 오브젝트의 활성 상태를 적용한다.
        /// unlocked를 보관하고 첫 해금 시 공격 시각을 설정하며 플레이어 입력을 끄고 팔의 GameObject 상태를 변경한다.
        /// </summary>
        private void SetUnlocked(bool unlocked)
        {
            if (unlocked && !_unlocked)
                _nextAttackAt = Time.unscaledTimeAsDouble + _attackInterval;
            _unlocked = unlocked;
            if (_arm.TryGetComponent(out KeyboardTypingArmInputBinding inputBinding))
                inputBinding.SetInputEnabled(false);
            _arm.gameObject.SetActive(unlocked);
        }

        /// <summary>
        /// 다음 자동공격 시각을 공통 설정의 한 주기만큼 진행한다.
        /// 현재 주기와 실제 시간을 사용하며 지연된 프레임에서는 몰아치지 않고 한 번만 처리한다.
        /// </summary>
        private void AdvanceAttackClock()
        {
            _nextAttackAt += _attackInterval;
            if (_nextAttackAt <= Time.unscaledTimeAsDouble)
                _nextAttackAt = Time.unscaledTimeAsDouble + _attackInterval;
        }

        /// <summary>
        /// 자동클릭 데이터의 현재 레벨 최종 속도를 공격 예약에 반영한다.
        /// 설정된 초당 횟수와 남은 대기 비율을 사용해 _attackInterval과 _nextAttackAt을 갱신한다.
        /// </summary>
        private void RefreshAttackInterval()
        {
            float speed = _upgrades != null ? _upgrades.GetAutoAttackSpeed() : 0f;
            float interval = speed > 0f ? 1f / speed : _attackInterval;
            if (interval == _attackInterval)
                return;
            double now = Time.unscaledTimeAsDouble;
            if (_unlocked)
            {
                double remaining = System.Math.Max(0d, _nextAttackAt - now);
                _nextAttackAt = now + remaining / _attackInterval * interval;
            }
            _attackInterval = interval;
        }
    }
}
