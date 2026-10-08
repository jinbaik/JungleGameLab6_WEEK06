using System;

using UnityEngine;

namespace Game.Upgrades
{
    [CreateAssetMenu(fileName = "Upgrade", menuName = "Game/Upgrade Definition")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private UpgradeId _id;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea] private string _description;

        [Header("Progression")]
        [SerializeField, Min(1)] private long[] _levelCosts = { 10, 25, 60 };

        public UpgradeId Id => _id;
        public string DisplayName => _displayName;
        public string Description => _description;
        public int MaxLevel => _levelCosts.Length;

        void OnValidate()
        {
            for (int i = 0; i < _levelCosts.Length; i++)
            {
                _levelCosts[i] = Math.Max(1, _levelCosts[i]);
            }
        }

        /// <summary>
        /// currentLevel에서 다음 레벨로 강화하는 데 필요한 가격을 반환한다.
        /// currentLevel은 0 이상이며 최대 레벨보다 작아야 한다.
        /// </summary>
        public long GetCost(int currentLevel)
        {
            if (currentLevel < 0 || currentLevel >= MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(currentLevel));
            }

            return _levelCosts[currentLevel];
        }
    }
}
