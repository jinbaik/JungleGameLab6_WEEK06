using System.Collections.Generic;

namespace Game.Player
{
    /// <summary>현재 실행 가능한 입력과 행동을 보관한다. 상태 제공자와 표시 UI가 같은 목록을 공유한다.</summary>
    public sealed class WorkshopControlHintState
    {
        public readonly struct Row
        {
            public string Input { get; }
            public string Action { get; }

            /// <summary>input과 action으로 표시할 한 줄을 만든다. 게임 상태는 변경하지 않는다.</summary>
            public Row(string input, string action) { Input = input; Action = action; }
        }

        private readonly List<Row> _rows = new List<Row>();
        public string Title { get; set; }
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>직전 제목과 행을 비운다. 다음 상태 조회가 오래된 문구를 재사용하지 않도록 한다.</summary>
        public void Clear() { Title = string.Empty; _rows.Clear(); }

        /// <summary>input과 action을 표시 순서대로 추가한다. 동일한 행은 한 번만 보관한다.</summary>
        public void Add(string input, string action)
        {
            foreach (Row row in _rows) if (row.Input == input && row.Action == action) return;
            _rows.Add(new Row(input, action));
        }
    }
}
