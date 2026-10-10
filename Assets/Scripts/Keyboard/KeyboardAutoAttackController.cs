using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

using Game.Session;
using Game.Upgrades;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardAutoAttackController : MonoBehaviour
    {
        private const float ATTACK_INTERVAL = 1f;

        [Header("Dependencies")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private KeyboardTypingArmController _arm;
        private UpgradeService _upgrades;
        private KeyboardInputController _keyboard;
        private KeyboardDestruction _destruction;

        [Header("Automatic Attack")]
        [SerializeField, Min(1)] private int _damage = 1;
        [Tooltip("기본 모델에서 흰색인 키의 식별자입니다. 실행 중 등급 색상은 대상 판정에 사용하지 않습니다.")]
        [SerializeField] private Key[] _eligibleKeys;
        private readonly List<KeycapHealth> _targets = new List<KeycapHealth>();
        private float _elapsed;
        private bool _unlocked;

        void OnEnable()
        {
            _interactionController.ActiveKeyboardChanged += BindKeyboard;
            BindKeyboard(_interactionController.ActiveKeyboard);
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
            _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            if (_upgrades != null)
                _upgrades.LevelChanged -= HandleUpgradeChanged;
            BindKeyboard(null);
        }

        void Update()
        {
            if (!_unlocked || _keyboard == null || (_destruction != null && _destruction.IsBroken))
                return;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < ATTACK_INTERVAL)
                return;
            _elapsed %= ATTACK_INTERVAL;
            for (int index = _targets.Count - 1; index >= 0; index--)
            {
                if (_targets[index] == null || _targets[index].CurrentHP <= 0)
                    _targets.RemoveAt(index);
            }
            if (_targets.Count > 0)
                AttackKeycap(_targets[Random.Range(0, _targets.Count)]);
        }

        /// <summary>
        /// 자동공격으로 지정한 살아 있는 키캡을 한 번 타격한다.
        /// target을 전용 기계팔 함수와 기존 피드백에 전달한 뒤 단일 피해를 적용하며 실제 타격 성공 여부를 반환한다.
        /// </summary>
        public bool AttackKeycap(KeycapHealth target)
        {
            if (!_unlocked || _keyboard == null || target == null || target.CurrentHP <= 0 ||
                (_destruction != null && _destruction.IsBroken) || !_targets.Contains(target))
                return false;
            if (!_arm.PressKeyAutomatically(target.transform))
                return false;
            _keyboard.NotifyAutomaticKeycapHit(target.transform);
            target.TakeDamage(_damage);
            return true;
        }

        /// <summary>
        /// 교체된 키보드에서 기본 흰색 영역의 키캡만 보관한다.
        /// keyboard와 _eligibleKeys로 새 대상 목록을 만들고 이전 대상과 공격 경과 시간을 초기화한다.
        /// </summary>
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            _keyboard = keyboard;
            _destruction = null;
            _targets.Clear();
            _elapsed = 0;
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
        /// unlocked를 보관하고 공격 경과 시간을 초기화하며 팔의 GameObject 상태를 변경한다.
        /// </summary>
        private void SetUnlocked(bool unlocked)
        {
            _unlocked = unlocked;
            _elapsed = 0;
            _arm.gameObject.SetActive(unlocked);
        }
    }
}
