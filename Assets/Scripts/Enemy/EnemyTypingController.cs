using System.Collections.Generic;

using UnityEngine;

namespace KeyboardModeling
{
    [DisallowMultipleComponent]
    public sealed class EnemyTypingController : MonoBehaviour
    {
        [Header("Arm")]
        [SerializeField] private KeyboardTypingArmController _arm;

        [Header("Attack")]
        [SerializeField, Min(0.01f)] private float _attacksPerSecond = 8f;
        [SerializeField, Min(1)] private int _damage = 2;
        private readonly List<KeycapHealth> _targets = new List<KeycapHealth>();
        private KeyboardInputController _keyboard;
        private float _attackRemaining;
        private bool _isAttacking;
        public bool IsAttacking => _isAttacking;

        /// <summary>speed와 damage로 대전의 공격 설정을 변경하고 팔 동작을 타격 간격에 맞춘다.</summary>
        public void ConfigureTournamentAttack(float speed, int damage)
        {
            StopAttacking();
            _attacksPerSecond = Mathf.Max(0.01f, speed);
            _damage = Mathf.Max(1, damage);
            _arm.ConfigureTournamentMotion(0.7f / _attacksPerSecond);
        }

        void OnDisable()
        {
            StopAttacking();
            SetKeyboard(null);
        }

        /// <summary>
        /// 외부 라운드의 공격 실행을 시작한다.
        /// Inspector의 공격 속도를 사용하여 첫 공격까지의 대기와 실행 상태를 초기화한다.
        /// </summary>
        public void StartAttacking()
        {
            _attackRemaining = 1f / _attacksPerSecond;
            _isAttacking = true;
        }

        /// <summary>
        /// 적의 공격을 중단하고 진행 중인 팔 동작을 취소한다.
        /// 입력값 없이 공격 실행 상태와 키캡 눌림 표시를 해제한다.
        /// </summary>
        public void StopAttacking()
        {
            _isAttacking = false;
            _arm.CancelPress();
        }

        /// <summary>
        /// keyboard의 완파 대상 키캡을 적의 공격 대상으로 연결한다.
        /// 이전 팔 동작을 정리하고 새로운 키보드 참조와 공격 대상 목록을 저장한다.
        /// </summary>
        public void SetKeyboard(KeyboardInputController keyboard)
        {
            _arm.CancelPress();
            _targets.Clear();
            _keyboard = keyboard;
            _attackRemaining = 1f / _attacksPerSecond;
            if (keyboard == null)
                return;
            _targets.AddRange(keyboard.GetDestructionKeycaps());
        }

        /// <summary>
        /// 라운드가 전달한 deltaTime만큼 공격 대기를 진행한다.
        /// 살아 있는 키캡 중 팔이 도달하는 대상을 최대 한 번 타격하고 다음 공격을 예약한다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isAttacking || !isActiveAndEnabled || _keyboard == null || deltaTime <= 0f)
                return;
            _attackRemaining -= deltaTime;
            if (_attackRemaining > 0f || _arm.IsPressing)
                return;
            _attackRemaining = 1f / _attacksPerSecond;
            int first = UnityEngine.Random.Range(0, _targets.Count);
            for (int offset = 0; offset < _targets.Count; offset++)
            {
                KeycapHealth target = _targets[(first + offset) % _targets.Count];
                if (target == null || target.CurrentHP <= 0 || !_arm.PressKey(target.transform))
                    continue;

                KeyboardInputController keyboard = _keyboard;
                keyboard.NotifyAutomaticKeycapHit(target.transform);
                if (!_isAttacking || _keyboard != keyboard)
                {
                    _arm.CancelPress();
                    return;
                }
                target.TakeDamage(_damage);
                return;
            }
        }

    }
}
