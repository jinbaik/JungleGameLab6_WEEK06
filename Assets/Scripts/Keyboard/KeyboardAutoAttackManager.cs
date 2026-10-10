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

        [Header("Common Automatic Attack Settings")]
        [Tooltip("기계팔 한 번의 타격으로 적용하는 피해량입니다.")]
        [SerializeField, Min(1)] private int _damage = 1;
        [Tooltip("기계팔 하나가 초당 공격하는 횟수입니다. 모든 자식 기계팔에 공통 적용됩니다.")]
        [SerializeField, Min(0.01f)] private float _attacksPerSecond = 1f;
        public int Damage => Mathf.Max(1, _damage);
        public float AttackInterval => 1f / Mathf.Max(0.01f, _attacksPerSecond);

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
