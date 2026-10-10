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
        private double _nextAttackAt;
        private float _attackInterval;
        private bool _unlocked;

        void Awake()
        {
            _manager = KeyboardAutoAttackManager.Instance;
            _gameSession = _manager.GameSession;
            _interactionController = _manager.InteractionController;
            _attackInterval = _manager.AttackInterval;
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
            SetUnlocked(_upgrades.GetLevel(UpgradeId.AutoClick) > 0);
        }

        void OnDisable()
        {
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
                return false;
            AdvanceAttackClock();
            _keyboard.NotifyAutomaticKeycapHit(target.transform);
            target.TakeDamage(_manager.Damage);
            return true;
        }

        /// <summary>
        /// 교체된 키보드에서 기본 흰색 영역의 키캡만 보관한다.
        /// keyboard와 _eligibleKeys로 새 대상 목록을 만들고 이전 대상을 해제하며 공격 시각은 유지한다.
        /// </summary>
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            _keyboard = keyboard;
            _destruction = null;
            _targets.Clear();
            if (_stationFeedback != null)
                _stationFeedback.SetAutomaticKeyboard(_keyboard);
            if (_keyboard == null)
                return;
            _destruction = _keyboard.GetComponent<KeyboardDestruction>();
            IReadOnlyDictionary<Key, KeycapHealth> layout = _keyboard.GetKeycapLayout();
            foreach (Key key in _eligibleKeys)
            {
                if (layout.TryGetValue(key, out KeycapHealth target) && target.CurrentHP > 0)
                    _targets.Add(target);
            }
        }

        /// <summary>
        /// 자동공격 강화의 구매 상태를 적용한다.
        /// id와 level이 AutoClick 강화이면 첫 구매부터 기계팔과 자동공격을 활성화한다.
        /// </summary>
        private void HandleUpgradeChanged(UpgradeId id, int level)
        {
            if (id == UpgradeId.AutoClick)
                SetUnlocked(level > 0);
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
            _arm.SetTemporaryInputEnabled(false);
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
        /// 매니저의 공격 속도 변경을 현재 공격 예약에 반영한다.
        /// 공통 주기와 남은 대기 비율을 사용해 _attackInterval과 _nextAttackAt을 갱신한다.
        /// </summary>
        private void RefreshAttackInterval()
        {
            float interval = _manager.AttackInterval;
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
