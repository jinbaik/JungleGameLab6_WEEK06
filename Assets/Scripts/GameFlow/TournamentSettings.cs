using System;

using UnityEngine;

namespace Game.Tournament
{
    public enum TournamentPhase { Preparing, Battle, Result, Shop, Discard, Finished }

    [CreateAssetMenu(menuName = "Game/Tournament Settings")]
    public sealed class TournamentSettings : ScriptableObject
    {
        [Header("Tournament")]
        [SerializeField, Min(1f)] private float _battleDuration = 30f;
        [SerializeField, Min(1)] private int _keycapHealth = 5;
        [SerializeField, Min(1)] private int _capacity = 80;

        [Header("Shop")]
        [SerializeField, Min(1)] private int _scorePerCurrency = 100;
        [SerializeField, Min(0)] private int _rerollCost = 5;
        [SerializeField, Min(1)] private int _rerollLimit = 3;

        [Header("Rewards")]
        [SerializeField, Min(0f)] private float _currencyRewardMultiplier = 1.2f;
        [SerializeField] private int[] _goldRewards = { 50, 40, 30, 10 };

        [Header("Robot Rounds")]
        [SerializeField] private RobotRound[] _rounds =
        {
            new RobotRound(13.2f, 2, 0), new RobotRound(15.4f, 2, 0),
            new RobotRound(17.6f, 2, 0), new RobotRound(19.8f, 2, 0),
            new RobotRound(22f, 2, 0)
        };

        public float BattleDuration => _battleDuration;
        public int KeycapHealth => _keycapHealth;
        public int Capacity => _capacity;
        public int ScorePerCurrency => _scorePerCurrency;
        public float CurrencyRewardMultiplier => _currencyRewardMultiplier;
        public int RerollCost => _rerollCost;
        public int RerollLimit => _rerollLimit;
        public int RoundCount => _rounds.Length;


        /// <summary>rank에 해당하는 작업실 골드를 반환하며 4위 이하에는 기타 보상을 사용한다.</summary>
        public int GetGold(int rank) => _goldRewards[Mathf.Clamp(rank - 1, 0, 3)];

        /// <summary>round의 파괴 필요 수량을 반환하며 1·2대전은 25개, 이후는 직전 수량의 1.2배를 반올림한다.</summary>
        public int GetRequiredBrokenCount(int round)
        {
            int count = 25;
            for (int index = 3; index <= round; index++) count = Mathf.Min(87, Mathf.RoundToInt(count * 1.2f));
            return count;
        }
        /// <summary>1부터 시작하는 round의 로봇 설정을 반환하며 원본 데이터를 유지한다.</summary>
        public RobotRound GetRound(int round) => _rounds[round - 1];
    }

    [Serializable]
    public sealed class RobotRound
    {
        [SerializeField, Min(0.01f)] private float _attacksPerSecond;
        [SerializeField, Min(1)] private int _damage;
        [SerializeField, Min(0)] private int _specialCount;
        public float AttacksPerSecond => _attacksPerSecond;
        public int Damage => _damage;
        public int SpecialCount => _specialCount;

        /// <summary>speed, damage와 specialCount를 저장한 대전별 로봇 설정을 생성한다.</summary>
        public RobotRound(float speed, int damage, int specialCount)
        {
            _attacksPerSecond = speed;
            _damage = damage;
            _specialCount = specialCount;
        }
    }
}
