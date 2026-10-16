using System;

using UnityEngine;

using Game.Economy;
using KeyboardModeling;

namespace Game.UI
{
    public sealed class KeyboardToastPresenter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private KeyboardRewardController _rewardController;
        [SerializeField] private ToastPool _toastPool;

        [Header("Damage Display")]
        [SerializeField] private bool _showRequestedDamage;
        private KeyboardInputController _activeKeyboard;
        private KeycapHealth[] _keycaps = Array.Empty<KeycapHealth>();

        void OnEnable()
        {
            _interactionController = FindAnyObjectByType<KeyboardInteractionController>();
            _interactionController.ActiveKeyboardChanged += BindKeyboard;
            _rewardController.RewardGranted += OnRewardGranted;
            BindKeyboard(_interactionController.ActiveKeyboard);
        }

        void OnDisable()
        {
            _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            _rewardController.RewardGranted -= OnRewardGranted;
            UnbindKeyboard();
        }

        private void BindKeyboard(KeyboardInputController keyboard)
        {
            if (_activeKeyboard == keyboard) return;
            UnbindKeyboard();
            _activeKeyboard = keyboard;
            if (keyboard == null) return;

            _keycaps = keyboard.GetComponentsInChildren<KeycapHealth>(true);
            for (int index = 0; index < _keycaps.Length; index++) _keycaps[index].Damaged += OnDamaged;
        }

        /// <summary>
        /// 저장한 키캡의 피해 알림 구독을 모두 해제한다.
        /// 표시 대상 목록과 키보드 참조를 비운다.
        /// </summary>
        private void UnbindKeyboard()
        {
            for (int index = 0; index < _keycaps.Length; index++)
                if (_keycaps[index] != null) _keycaps[index].Damaged -= OnDamaged;
            _keycaps = Array.Empty<KeycapHealth>();
            _activeKeyboard = null;
        }

        private void OnDamaged(KeycapDamage damage)
        {
            long amount = _showRequestedDamage ? damage.RequestedDamage : damage.AppliedDamage;
            _toastPool.Show(new ToastRequest(ToastKind.Damage, amount, damage.WorldPosition, damage.RequestedDamage, damage.AppliedDamage));
        }

        /// <summary>
        /// 지급 완료된 amount와 worldPosition 및 isDoubleReward를 보상 Toast로 전달한다.
        /// 최종 지급액과 두 배 판정 결과를 풀에 요청하여 표시 내용과 글자 크기에 반영한다.
        /// </summary>
        private void OnRewardGranted(long amount, Vector3 worldPosition, bool isDoubleReward)
        {
            _toastPool.Show(new ToastRequest(ToastKind.Reward, amount, worldPosition, isDoubleReward: isDoubleReward));
        }
    }
}
