using UnityEngine;

using Game.Economy;
using Game.Session;

namespace KeyboardModeling
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class KeyboardAutoAttackManager : MonoBehaviour
    {
        public static KeyboardAutoAttackManager Instance { get; private set; }

        [Header("Shared Scene References")]
        [SerializeField] private KeyboardInteractionController _interactionController;
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private KeyboardRewardController _rewardController;
        [SerializeField] private GameObject _keyboardFragmentsPrefab;
        public KeyboardInteractionController InteractionController => _interactionController;
        public GameSession GameSession => _gameSession;
        public KeyboardRewardController RewardController => _rewardController;
        public GameObject KeyboardFragmentsPrefab => _keyboardFragmentsPrefab;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
