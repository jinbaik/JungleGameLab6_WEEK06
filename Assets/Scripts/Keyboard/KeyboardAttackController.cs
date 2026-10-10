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
        private KeyboardDestruction _keyboardDestruction;
        private KeycapHealth[] _keycaps = Array.Empty<KeycapHealth>();
        private Vector2[] _keycapPositions = Array.Empty<Vector2>();
        private KeycapHealth _directHitTarget;

        [Header("Dependencies")]
        [Tooltip("강화가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;
        [Tooltip("피버가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private FeverController _feverController;

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
        /// 이전 공격 및 피해 구독을 해제하고 새 대상의 키캡, 평면 위치와 유효 타격 구독을 보관한다.
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
            foreach (KeycapHealth keycap in _keycaps)
                if (keycap != null) keycap.DamageApplied -= HandleDamageApplied;

            _activeKeyboard = keyboard;
            _keyboardDestruction = null;
            _keycaps = Array.Empty<KeycapHealth>();
            _keycapPositions = Array.Empty<Vector2>();
            _directHitTarget = null;

            if (_activeKeyboard != null)
            {
                _keyboardDestruction = _activeKeyboard.GetComponent<KeyboardDestruction>();
                _keycaps = _activeKeyboard.GetComponentsInChildren<KeycapHealth>(true);
                _keycapPositions = new Vector2[_keycaps.Length];
                for (int index = 0; index < _keycaps.Length; index++)
                {
                    Vector3 position = _activeKeyboard.transform.InverseTransformPoint(_keycaps[index].transform.position);
                    _keycapPositions[index] = new Vector2(position.x, position.z);
                    _keycaps[index].DamageApplied += HandleDamageApplied;
                }

                _activeKeyboard.AttackRequested += OnAttackRequested;
            }
        }

        /// <summary>
        /// health의 위치를 중심으로 같은 키보드 반경 안의 살아 있는 키캡을 한 번씩 타격한다.
        /// 공격 시작 시의 피버 배율로 피해를 계산하며 살아 있는 중심에만 직접 피해와 충전을 적용하고 깨진 중심도 광역 피해를 발생시킨다.
        /// </summary>
        private void OnAttackRequested(KeycapHealth health)
        {
            KeyboardInputController keyboard = _activeKeyboard;
            KeyboardDestruction destruction = _keyboardDestruction;
            if (keyboard == null || !keyboard.InputEnabled || !keyboard.isActiveAndEnabled
                || (destruction != null && destruction.IsBroken))
            {
                return;
            }

            KeycapHealth[] keycaps = _keycaps;
            Vector2[] positions = _keycapPositions;
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
            float multiplier = _feverController != null
                ? _feverController.DamageMultiplier
                : 1f;
            int damage = Mathf.CeilToInt((_baseDamage + damageBonus) * multiplier);
            int areaDamage = Mathf.CeilToInt((_baseAreaDamage + areaDamageBonus) * multiplier);
            Vector2 center = positions[targetIndex];

            if (health.CurrentHP > 0)
            {
                _directHitTarget = health;
                try
                {
                    health.TakeDamage(damage);
                }
                finally
                {
                    _directHitTarget = null;
                }
            }
            ApplyAreaDamage(keyboard, destruction, keycaps, positions, targetIndex, center, radius, areaDamage);
        }

        /// <summary>
        /// keycap의 실제 피해 알림이 현재 직접 타격에서 발생했는지 확인한다.
        /// 활성 키보드의 직접 타격만 피버에 전달하여 광역 및 외부 피해가 충전량을 증가시키지 않도록 한다.
        /// </summary>
        private void HandleDamageApplied(KeycapHealth keycap)
        {
            if (keycap != _directHitTarget || _activeKeyboard == null
                || !_activeKeyboard.InputEnabled || !_activeKeyboard.isActiveAndEnabled || _feverController == null)
                return;

            _feverController.AddHitCharge();
        }

        /// <summary>
        /// keyboard에 범위 타격 효과를 알리고 keycaps와 positions에서 center와 radius 안의 살아 있는 주변 키캡을 타격한다.
        /// targetIndex를 제외하고 damage를 한 번씩 적용하며 destruction의 파괴가 시작되면 남은 처리를 중단한다.
        /// </summary>
        private void ApplyAreaDamage(KeyboardInputController keyboard, KeyboardDestruction destruction, KeycapHealth[] keycaps, Vector2[] positions, int targetIndex, Vector2 center, float radius, int damage)
        {
            if (radius <= 0f || damage <= 0)
            {
                return;
            }

            float radiusSquared = radius * radius;
            for (int index = 0; index < keycaps.Length; index++)
            {
                if (destruction != null && destruction.IsBroken)
                    return;
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
