using System;
using System.Collections.Generic;

namespace Game.Tournament
{
    public sealed class TournamentInventory
    {
        private readonly int[] _counts = new int[6];
        private readonly int _capacity;
        public int Capacity => _capacity;
        public int Total { get; private set; }

        /// <summary>capacity를 상한으로 하는 빈 특수 키캡 보유 상태를 생성한다.</summary>
        public TournamentInventory(int capacity) { _capacity = capacity; }

        /// <summary>source의 수량을 복사하고 capacity를 사용할 독립 보유 상태를 생성한다.</summary>
        public TournamentInventory(TournamentInventory source, int capacity)
        {
            _capacity = capacity;
            Array.Copy(source._counts, _counts, _counts.Length);
            Total = source.Total;
        }

        /// <summary>type의 현재 보유 개수를 반환하며 수량을 변경하지 않는다.</summary>
        public int GetCount(SpecialKeycapType type) => _counts[(int)type];

        /// <summary>type을 count개 추가하고 성공 여부를 반환하며 상한 초과와 잘못된 수량은 거부한다.</summary>
        public bool TryAdd(SpecialKeycapType type, int count)
        {
            if (type == SpecialKeycapType.None || count <= 0 || Total + count > _capacity) return false;
            _counts[(int)type] += count;
            Total += count;
            return true;
        }

        /// <summary>type의 키캡을 하나 폐기하고 성공 여부를 반환하며 총 보유 수량을 감소시킨다.</summary>
        public bool RemoveOne(SpecialKeycapType type)
        {
            if (_counts[(int)type] == 0) return false;
            _counts[(int)type]--;
            Total--;
            return true;
        }

        /// <summary>현재 보유 수량대로 유형 목록을 생성하여 반환하며 원본 수량은 유지한다.</summary>
        public List<SpecialKeycapType> CreateTypes()
        {
            List<SpecialKeycapType> types = new List<SpecialKeycapType>(Total);
            for (int type = 1; type < _counts.Length; type++)
                for (int index = 0; index < _counts[type]; index++) types.Add((SpecialKeycapType)type);
            return types;
        }
    }

    public sealed class TournamentResult
    {
        public int Rank { get; }
        public int Gold { get; }
        public long PlayerScore { get; }
        public long EnemyScore { get; }

        /// <summary>rank, gold와 양측 최종 점수를 보관하는 작업실 전달용 결과를 생성한다.</summary>
        public TournamentResult(int rank, int gold, long playerScore, long enemyScore)
        {
            Rank = rank;
            Gold = gold;
            PlayerScore = playerScore;
            EnemyScore = enemyScore;
        }
    }
}
