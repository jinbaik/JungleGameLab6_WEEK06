using System;

using UnityEngine;

using Game.Session;
using KeyboardModeling;

namespace Game.Economy
{
    public sealed class KeyboardRewardController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private KeyboardInteractionController _interactionController;

        [Header("Reward")]
        [SerializeField, Min(1)] private long _rewardPerKeycap = 10;

        [Header("Runtime State")]
        private KeyboardInputController _activeKeyboard;
        private KeycapHealth[] _keycaps = Array.Empty<KeycapHealth>();
        public event Action<long, Vector3> RewardGranted;
        public long RewardPerKeycap => _rewardPerKeycap;

        void OnEnable()
        {
            _interactionController = FindAnyObjectByType<KeyboardInteractionController>();
            _interactionController.ActiveKeyboardChanged += BindKeyboard;
            BindKeyboard(_interactionController.ActiveKeyboard);
        }

        void OnDisable()
        {
            _interactionController.ActiveKeyboardChanged -= BindKeyboard;
            UnbindKeyboard();
        }

        /// keyboard를 새 보상 대상으로 사용하고 기존 키캡의 이벤트 연결을 해제한다.
        /// keyboard가 있으면 하위 키캡의 Broken 이벤트를 구독하고 대상과 목록을 저장한다.
        private void BindKeyboard(KeyboardInputController keyboard)
        {
            if (_activeKeyboard == keyboard)
            {
                return;
            }

            UnbindKeyboard();
            _activeKeyboard = keyboard;

            if (keyboard == null)
            {
                return;
            }

            _keycaps = keyboard.GetComponentsInChildren<KeycapHealth>(true);

            for (int index = 0; index < _keycaps.Length; index++)
            {
                _keycaps[index].Broken += OnKeycapBroken;
            }
        }

        /// 저장된 키캡 목록을 사용해 모든 Broken 이벤트 구독을 해제한다.
        /// 현재 대상 키보드와 키캡 목록을 비운다.
        private void UnbindKeyboard()
        {
            for (int index = 0; index < _keycaps.Length; index++)
            {
                _keycaps[index].Broken -= OnKeycapBroken;
            }

            _keycaps = Array.Empty<KeycapHealth>();
            _activeKeyboard = null;
        }

        /// <summary>
        /// keycap의 파괴 알림을 받아 설정된 키캡당 보상을 지급한다.
        /// 생성 데이터의 보상 또는 기존 고정 보상을 지갑에 더하고 지급 금액과 키캡 위치를 RewardGranted로 알린다.
        /// </summary>
        private void OnKeycapBroken(KeycapHealth keycap)
        {
            long reward = keycap.HasSpawnData ? keycap.Reward : _rewardPerKeycap;
            _gameSession.Wallet.Add(reward);
            RewardGranted?.Invoke(reward, keycap.transform.position);
        }

        /// <summary>
        /// 자동작업대에서 파괴된 키캡의 보상을 기존 지갑에 지급한다.
        /// keycap의 생성 보상 값을 사용하며 일반 키보드의 파괴 보상과 같은 지급 함수를 호출한다.
        /// </summary>
        public void GrantAutomaticKeycapReward(KeycapHealth keycap)
        {
            OnKeycapBroken(keycap);
        }
    }
}
