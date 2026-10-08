using UnityEngine;
using UnityEngine.UI;

using Game.Session;

namespace Game.Shop
{
    public sealed class StoreDemo : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private Button _rewardButton;
        [SerializeField] private Text _rewardText;

        [Header("Demo Reward")]
        [SerializeField, Min(1)] private long _rewardAmount = 100;

        void Start()
        {
            _rewardText.text = $"+{_rewardAmount:N0}";
            _rewardButton.onClick.AddListener(OnRewardClicked);
        }

        void OnDestroy()
        {
            _rewardButton.onClick.RemoveListener(OnRewardClicked);
        }

        /// <summary>
        /// 재화 지급 버튼의 입력을 받아 설정된 rewardAmount만큼 보상을 지급한다.
        /// 연결된 세션의 지갑 잔액을 변경하여 파괴 보상 흐름을 대신 확인한다.
        /// </summary>
        private void OnRewardClicked()
        {
            _gameSession.Wallet.Add(_rewardAmount);
        }
    }
}
