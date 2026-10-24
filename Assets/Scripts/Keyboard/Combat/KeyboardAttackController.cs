using System;

using UnityEngine;

using Game.Session;

namespace KeyboardModeling
{
    /// <summary>
    /// 키보드 입력에 따른 기본 손 타격과 기존 강화 기반 주먹 및 망치 추가 타격,
    /// 범위 피해 및 충격파 피해를 계산하고 대상 키캡에 적용하는 공격 처리 전담 컴포넌트이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardAttackController : MonoBehaviour
    {
        private const int SHOCKWAVE_DAMAGE = 1;

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
        [Tooltip("화면 효과 및 타격 시각 연출을 담당하는 매니저입니다.")]
        [SerializeField] private KeyboardFeedbackManager _feedbackManager;
        [Tooltip("강화가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private GameSession _gameSession;
        [Tooltip("피버가 없는 씬에서는 비워 둘 수 있습니다.")]
        [SerializeField] private FeverController _feverController;

        [Header("Attack")]
        [SerializeField, Min(1)] private int _baseDamage = 1;

        private int _partDamageBonus;

        /// <summary>bonus를 현재 장착 부품의 직접 타격 보너스로 저장한다. 기존 기본 대미지와 강화 설정은 유지한다.</summary>
        public void ConfigurePartDamage(int bonus) { _partDamageBonus = Mathf.Max(0, bonus); }

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
        /// 반환값은 없다.
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
            {
                if (keycap != null)
                {
                    keycap.DamageApplied -= HandleDamageApplied;
                }
            }

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
        /// health에 기본 손 피해를 적용하고 기존 강화 설정으로 추가 타격을 선택한다.
        /// 강화의 피해 및 확률과 피버 배율을 사용하며 콤보에 따른 추가 타격은 발생시키지 않는다.
        /// 반환값은 없다.
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
            int targetIndex = Array.IndexOf(keycaps, health);
            if (targetIndex < 0)
            {
                return;
            }

            int damageBonus = _gameSession != null ? _gameSession.Upgrades.GetDamageBonus() : 0;
            float radius = _gameSession != null ? _gameSession.Upgrades.GetAreaRadius() : 0f;
            float fistChance = _gameSession != null ? _gameSession.Upgrades.GetFistChance() : 0f;
            int fistBaseDamage = _gameSession != null ? _gameSession.Upgrades.GetFistDamage() : 0;
            float hammerChance = _gameSession != null ? _gameSession.Upgrades.GetHammerChance() : 0f;
            int hammerBaseDamage = _gameSession != null ? _gameSession.Upgrades.GetHammerDamage() : 0;
            float multiplier = _feverController != null ? _feverController.DamageMultiplier : 1f;

            int damage = Mathf.CeilToInt((_baseDamage + damageBonus + _partDamageBonus) * multiplier);
            int fistDamage = Mathf.CeilToInt(fistBaseDamage * multiplier);
            int hammerDamage = Mathf.CeilToInt(hammerBaseDamage * multiplier);

            ApplyManualAttack(keyboard, destruction, health, damage, 0, 0f, true);

            if (destruction != null && destruction.IsBroken)
            {
                return;
            }

            if (fistDamage > 0 && fistChance > 0f && UnityEngine.Random.value < fistChance)
            {
                _feedbackManager.PlayKeycapStrike(health.transform, () => ApplyManualAttack(keyboard, destruction, health, fistDamage, fistDamage, radius, false));
            }

            if (hammerDamage > 0 && hammerChance > 0f && UnityEngine.Random.value < hammerChance)
            {
                _feedbackManager.PlayHammerStrike(health.transform, () => ApplyHammerAttack(keyboard, destruction, health, hammerDamage));
            }
        }

        /// <summary>
        /// health에 damage를 적용하고 radius 안의 주변 키캡에 areaDamage를 적용한다.
        /// keyboard와 destruction으로 유효 대상을 확인하며 chargeFever가 true인 기본 손 타격만 피버를 충전한다.
        /// 반환값은 없다.
        /// </summary>
        private void ApplyManualAttack(KeyboardInputController keyboard, KeyboardDestruction destruction, KeycapHealth health, int damage, int areaDamage, float radius, bool chargeFever)
        {
            if (keyboard == null || health == null || keyboard != _activeKeyboard
                || !keyboard.InputEnabled || !keyboard.isActiveAndEnabled
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

            Vector2 center = positions[targetIndex];
            keyboard.NotifyManualKeycapHit(health.transform);

            if (health.CurrentHP > 0)
            {
                _directHitTarget = chargeFever ? health : null;
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
        /// health에 망치의 직접 damage를 적용하고 키보드 전체로 퍼지는 충격파 연출 및 피해를 시작한다.
        /// keyboard와 destruction의 유효성을 확인하며 충격파 피해는 도달 시점에 각 키캡에 적용한다.
        /// 반환값은 없다.
        /// </summary>
        private void ApplyHammerAttack(KeyboardInputController keyboard, KeyboardDestruction destruction, KeycapHealth health, int damage)
        {
            if (keyboard == null || health == null || keyboard != _activeKeyboard
                || !keyboard.InputEnabled || !keyboard.isActiveAndEnabled || (destruction != null && destruction.IsBroken))
            {
                return;
            }

            int targetIndex = Array.IndexOf(_keycaps, health);
            if (targetIndex < 0)
            {
                return;
            }

            ApplyManualAttack(keyboard, destruction, health, damage, 0, 0f, false);

            if (destruction != null && destruction.IsBroken)
            {
                return;
            }

            _feedbackManager.PlayHammerShockwave(keyboard, _keycaps, _keycapPositions, targetIndex, keycap =>
            {
                if (keyboard != null && keyboard == _activeKeyboard && keyboard.InputEnabled && keyboard.isActiveAndEnabled
                    && keycap != null && keycap.CurrentHP > 0 && (destruction == null || !destruction.IsBroken))
                {
                    keycap.TakeDamage(SHOCKWAVE_DAMAGE);
                }
            });
        }

        /// <summary>
        /// keycap의 실제 피해 알림이 현재 직접 타격에서 발생했는지 확인한다.
        /// 활성 키보드의 직접 타격만 피버에 전달하여 광역 및 외부 피해가 충전량을 증가시키지 않도록 한다.
        /// 반환값은 없다.
        /// </summary>
        private void HandleDamageApplied(KeycapHealth keycap)
        {
            if (keycap != _directHitTarget || _activeKeyboard == null
                || !_activeKeyboard.InputEnabled || !_activeKeyboard.isActiveAndEnabled || _feverController == null)
            {
                return;
            }

            _feverController.AddHitCharge();
        }

        /// <summary>
        /// keyboard에 범위 타격 효과를 알리고 keycaps와 positions에서 center와 radius 안의 살아 있는 주변 키캡을 타격한다.
        /// targetIndex를 제외하고 damage를 한 번씩 적용하며 destruction의 파괴가 시작되면 남은 처리를 중단한다.
        /// 반환값은 없다.
        /// </summary>
        public static void ApplyAreaDamage(KeyboardInputController keyboard, KeyboardDestruction destruction, KeycapHealth[] keycaps, Vector2[] positions, int targetIndex, Vector2 center, float radius, int damage)
        {
            if (radius <= 0f || damage <= 0)
            {
                return;
            }

            float radiusSquared = radius * radius;
            for (int index = 0; index < keycaps.Length; index++)
            {
                if (destruction != null && destruction.IsBroken)
                {
                    return;
                }

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
