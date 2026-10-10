using System;

using UnityEngine;

using Game.Session;

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
        private KeycapHealth[] _keycaps = Array.Empty<KeycapHealth>();
        private Vector2[] _keycapPositions = Array.Empty<Vector2>();

        [Header("Dependencies")]
        [Tooltip("강화가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;
        [Tooltip("피버가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private MiniGameController _miniGameController;

        [Header("Attack")]
        [SerializeField, Min(1)] private int _baseDamage = 1;
        [SerializeField, Min(0)] private int _baseAreaDamage = 1;

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
        /// 새 대상의 키캡과 평면 위치를 보관하고 AttackRequested 구독과 현재 대상 참조를 변경한다.
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
            _keycaps = Array.Empty<KeycapHealth>();
            _keycapPositions = Array.Empty<Vector2>();

            if (_activeKeyboard != null)
            {
                _keycaps = _activeKeyboard.GetComponentsInChildren<KeycapHealth>(true);
                _keycapPositions = new Vector2[_keycaps.Length];
                for (int index = 0; index < _keycaps.Length; index++)
                {
                    Vector3 position = _activeKeyboard.transform.InverseTransformPoint(_keycaps[index].transform.position);
                    _keycapPositions[index] = new Vector2(position.x, position.z);
                }

                _activeKeyboard.AttackRequested += OnAttackRequested;
            }
        }

        /// <summary>
        /// health와 같은 키보드의 반경 안에 있는 살아 있는 키캡을 한 번씩 타격한다.
        /// 직접 및 광역 공격력 강화와 피버 배율로 각 피해량을 계산하여 대상 체력과 파괴 상태를 변경한다.
        /// </summary>
        private void OnAttackRequested(KeycapHealth health)
        {
            if (health.CurrentHP <= 0)
            {
                return;
            }

            KeycapHealth[] keycaps = _keycaps;
            Vector2[] positions = _keycapPositions;
            KeyboardInputController keyboard = _activeKeyboard;
            int targetIndex = Array.IndexOf(keycaps, health);
            if (targetIndex < 0)
            {
                return;
            }

            int damageBonus = _gameSession != null
                ? _gameSession.Upgrades.GetDamageBonus()
                : 0;
            int areaDamageBonus = _gameSession != null
                ? _gameSession.Upgrades.GetAreaDamageBonus()
                : 0;
            float radius = _gameSession != null
                ? _gameSession.Upgrades.GetAreaRadius()
                : 0f;
            float multiplier = _miniGameController != null
                ? _miniGameController.DamageMultiplier
                : 1f;
            int damage = Mathf.CeilToInt((_baseDamage + damageBonus) * multiplier);
            int areaDamage = Mathf.CeilToInt((_baseAreaDamage + areaDamageBonus) * multiplier);
            Vector2 center = positions[targetIndex];

            // 파괴 콜백이 현재 키보드를 해제해도 이번 타격은 보관한 대상과 위치를 사용한다.
            health.TakeDamage(damage);
            ApplyAreaDamage(keyboard, keycaps, positions, targetIndex, center, radius, areaDamage);
        }

        /// <summary>
        /// keyboard에 범위 타격 효과를 알리고 keycaps와 positions에서 center와 radius 안의 살아 있는 주변 키캡을 타격한다.
        /// targetIndex의 직접 타격 대상은 제외하고 damage를 한 번씩 적용하며 추가 공격 요청은 발생시키지 않는다.
        /// </summary>
        private void ApplyAreaDamage(KeyboardInputController keyboard, KeycapHealth[] keycaps, Vector2[] positions, int targetIndex, Vector2 center, float radius, int damage)
        {
            if (radius <= 0f || damage <= 0)
            {
                return;
            }

            float radiusSquared = radius * radius;
            for (int index = 0; index < keycaps.Length; index++)
            {
                if (index == targetIndex || keycaps[index].CurrentHP <= 0)
                {
                    continue;
                }

                if ((positions[index] - center).sqrMagnitude <= radiusSquared)
                {
                    keyboard.NotifyAreaKeycapHit(keycaps[index].transform);
                    keycaps[index].TakeDamage(damage);
                }
            }
        }
    }
}
