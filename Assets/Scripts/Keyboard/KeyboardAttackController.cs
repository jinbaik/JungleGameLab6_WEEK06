using UnityEngine;

using Game.Session;
using Game.Upgrades;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class KeyboardAttackController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("키보드 교체를 사용하는 씬에서 연결합니다. 연결하면 Keyboard보다 우선합니다.")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [Tooltip("키보드 교체를 사용하지 않는 씬의 입력 대상을 연결합니다.")]
        [SerializeField] private KeyboardInputController _keyboard;
        private KeyboardInputController _activeKeyboard;

        [Header("Dependencies")]
        [Tooltip("강화가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;
        [Tooltip("피버가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private MiniGameController _miniGameController;

        [Header("Attack")]
        [SerializeField, Min(1)] private int _baseDamage = 1;
        [SerializeField, Min(1)] private int _damagePerLevel = 1;

        void OnEnable()
        {
            if (_interactionController != null)
            {
                _interactionController.ActiveKeyboardChanged += BindKeyboard;
                BindKeyboard(_interactionController.ActiveKeyboard);
            }
            else
            {
                BindKeyboard(_keyboard);
            }
        }

        void OnDisable()
        {
            if (_interactionController != null)
            {
                _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            }

            BindKeyboard(null);
        }

        /// <summary>
        /// keyboard를 공격 입력 대상으로 교체하고 이전 대상의 구독을 해제한다.
        /// 새 대상이 있으면 AttackRequested를 구독하며 현재 대상 참조를 변경한다.
        /// </summary>
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            if (_activeKeyboard == keyboard)
            {
                return;
            }

            if (_activeKeyboard != null)
            {
                _activeKeyboard.AttackRequested -= OnAttackRequested;
            }

            _activeKeyboard = keyboard;

            if (_activeKeyboard != null)
            {
                _activeKeyboard.AttackRequested += OnAttackRequested;
            }
        }

        /// <summary>
        /// health를 대상으로 현재 강화 레벨과 피버 배율을 적용한 피해를 준다.
        /// 기본 공격력과 레벨당 증가량으로 계산한 피해를 전달하여 대상 체력과 파괴 상태를 변경한다.
        /// </summary>
        private void OnAttackRequested(KeycapHealth health)
        {
            int level = _gameSession != null
                ? _gameSession.Upgrades.GetLevel(UpgradeId.SmashDamage)
                : 0;
            int multiplier = _miniGameController != null
                ? _miniGameController.DamageMultiplier
                : 1;
            int damage = (_baseDamage + level * _damagePerLevel) * multiplier;

            health.TakeDamage(damage);
        }
    }
}
