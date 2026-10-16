using System;

using UnityEngine;

using Game.Shop;
using Game.Tournament;

namespace Game.Logging
{
    /// <summary>실제 대결 상태를 관찰하여 콤보 경계와 시간별 입력 및 점수 구간을 기록한다.</summary>
    public sealed class PlaytestStageRecorder
    {
        private readonly PlaytestCsvWriter _writer;
        private GameManager _source;
        private KeyboardScoreCombo _player;
        private KeyboardScoreCombo _enemy;
        private long _lastEnemyScore;
        private string _stageId;
        private string _runId;
        private double _stageStart;
        private double _intervalStart;
        private long _intervalScore;
        private long _downCount;
        private long _brokenDownCount;
        private int _intervalIndex;
        private int _intervalCombo;
        private int _previousCombo;
        private long _lastScore;
        private double _snapshotTime = -1d;
        private long _snapshotScore;
        private MonitorSettingsAppView[] _settingsMenus;
        private ShopView[] _shops;
        private KeyboardGoalAppView[] _goalMenus;
        private bool _isClosingMenu;
        public bool IsActive => _runId != null;
        public event Action MenuInputSamplingRequested;

        /// <summary>
        /// writer를 실행 전체 CSV 저장 대상으로 보관한다.
        /// 새 집계기를 생성하며 게임 상태는 변경하지 않는다.
        /// </summary>
        public PlaytestStageRecorder(PlaytestCsvWriter writer) { _writer = writer; }

        /// <summary>
        /// source의 실제 라운드를 새 실행으로 시작한다.
        /// 기존 실행은 Restart로 마감하고 GUID와 시작 점수 및 시간 구간을 저장한다.
        /// </summary>
        public void Begin(GameManager source)
        {
            if (IsActive) End(_source, "Restart", false);
            _source = source;
            _player = source.PlaytestPlayerScore;
            _enemy = source.PlaytestEnemyScore;
            _lastEnemyScore = _enemy != null ? _enemy.Score : 0L;
            _stageId = source.gameObject.scene.path + "/" + source.PlaytestStageName;
            _runId = Guid.NewGuid().ToString("N");
            _stageStart = Time.realtimeSinceStartupAsDouble;
            _intervalStart = _stageStart;
            _intervalIndex = 0;
            _intervalCombo = 0;
            _downCount = 0;
            _brokenDownCount = 0;
            _lastScore = _player != null ? _player.Score : 0L;
            _intervalScore = _lastScore;
            _previousCombo = _player != null ? _player.Combo : 0;
            _snapshotTime = -1d;
            _settingsMenus = UnityEngine.Object.FindObjectsByType<MonitorSettingsAppView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _shops = UnityEngine.Object.FindObjectsByType<ShopView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _goalMenus = UnityEngine.Object.FindObjectsByType<KeyboardGoalAppView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonitorSettingsAppView menu in _settingsMenus) menu.OpenStateChanged += ObserveMenuState;
            foreach (ShopView menu in _shops) menu.OpenStateChanged += ObserveMenuState;
            foreach (KeyboardGoalAppView menu in _goalMenus) menu.OpenStateChanged += ObserveMenuState;
            if (_player != null) _player.StateChanged += ObservePlayerState;
            if (_enemy != null) _enemy.StateChanged += ObserveEnemyState;
            PlaytestRecord row = CreateRecord("StageStart", _stageStart);
            row.Set(PlaytestColumn.PlayerScore, _lastScore);
            row.Set(PlaytestColumn.EnemyScore, _lastEnemyScore);
            _writer.Write(row);
            Snapshot(_stageStart, _lastScore);
            _writer.Flush();
        }

        /// <summary>
        /// record에 현재 스테이지와 실행 식별자를 설정한다.
        /// 스테이지 밖에서는 빈 값으로 두고 원본 키 추적과 독립적으로 처리한다.
        /// </summary>
        public void AddStageContext(PlaytestRecord record)
        {
            if (!IsActive) return;
            record.Set(PlaytestColumn.StageId, _stageId);
            record.Set(PlaytestColumn.StageRunId, _runId);
            AddTournamentContext(record, _source.PlaytestTournament);
            if (_source.PlaytestTournament != null && _source.IsTimerRunning)
                record.Set(PlaytestColumn.Phase, "Battle");
        }

        /// <summary>
        /// tournament의 현재 실행과 대전 번호 및 진행 상태를 record에 복사한다.
        /// 비토너먼트 모드에서는 해당 열을 비워 두고 게임 상태는 변경하지 않는다.
        /// </summary>
        public static void AddTournamentContext(PlaytestRecord record, TournamentController tournament)
        {
            if (tournament == null) return;
            record.Set(PlaytestColumn.TournamentRunId, tournament.PlaytestRunId);
            record.Set(PlaytestColumn.RoundIndex, tournament.Round);
            record.Set(PlaytestColumn.BattleKind, tournament.PlaytestBattleKind);
            record.Set(PlaytestColumn.Phase, tournament.Phase.ToString());
        }

        /// <summary>
        /// 원본 입력의 새로운 Down 한 건을 실행 중인 구간에 가산한다.
        /// 열린 메뉴와 스테이지 밖 입력은 집계하지 않고 게임의 성공 판정은 참조하지 않는다.
        /// </summary>
        public void AddDown()
        {
            if (CanCountDown()) _downCount++;
        }

        /// <summary>
        /// 현재 실행과 메뉴 상태로 대전 입력 집계 가능 여부를 반환한다.
        /// 입력값 없이 기존 APM 제외 조건을 재입력 집계에서도 공유한다.
        /// </summary>
        public bool CanCountDown()
        {
            if (!IsActive || _isClosingMenu) return false;
            foreach (MonitorSettingsAppView menu in _settingsMenus) if (menu != null && menu.IsOpen) return false;
            foreach (ShopView menu in _shops) if (menu != null && menu.IsOpen) return false;
            foreach (KeyboardGoalAppView menu in _goalMenus) if (menu != null && menu.IsOpen) return false;
            return true;
        }

        /// <summary>
        /// 실제 대전에서 관찰한 파괴 위치 재입력 한 건을 가산한다.
        /// 입력값 없이 현재 대전의 종료 요약에 사용할 누적 횟수를 변경한다.
        /// </summary>
        public void AddBrokenDown() { _brokenDownCount++; }

        /// <summary>
        /// source의 확정 상태를 reason과 complete에 따라 한 번 마감한다.
        /// 최종 스냅샷과 구간 및 스테이지 결과를 기록하고 상태 구독을 해제한다.
        /// </summary>
        public void End(GameManager source, string reason, bool complete)
        {
            if (!IsActive || !ReferenceEquals(source, _source)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            long playerScore = _player != null ? _player.Score : _lastScore;
            long enemyScore = _enemy != null ? _enemy.Score : _lastEnemyScore;
            Snapshot(now, playerScore);
            CloseInterval(now, playerScore, reason, complete);
            PlaytestRecord row = CreateRecord("StageEnd", now);
            row.Set(PlaytestColumn.StartElapsedSeconds, _stageStart);
            row.Set(PlaytestColumn.EndElapsedSeconds, now);
            row.Set(PlaytestColumn.DurationSeconds, now - _stageStart);
            row.Set(PlaytestColumn.PlayerScore, playerScore);
            row.Set(PlaytestColumn.EnemyScore, enemyScore);
            row.Set(PlaytestColumn.EndReason, reason);
            row.Set(PlaytestColumn.IsComplete, complete);
            row.Set(PlaytestColumn.BrokenDownCount, _brokenDownCount);
            _writer.Write(row);
            if (_player != null) _player.StateChanged -= ObservePlayerState;
            if (_enemy != null) _enemy.StateChanged -= ObserveEnemyState;
            foreach (MonitorSettingsAppView menu in _settingsMenus) if (menu != null) menu.OpenStateChanged -= ObserveMenuState;
            foreach (ShopView menu in _shops) if (menu != null) menu.OpenStateChanged -= ObserveMenuState;
            foreach (KeyboardGoalAppView menu in _goalMenus) if (menu != null) menu.OpenStateChanged -= ObserveMenuState;
            _player = null;
            _enemy = null;
            _source = null;
            _runId = null;
            _writer.Flush();
        }

        /// <summary>
        /// 진행 중인 실행을 reason에 따른 중단으로 마감한다.
        /// 앱 종료 시에도 실제 중단 시점의 점수를 저장한다.
        /// </summary>
        public void Abort(string reason) { if (IsActive) End(_source, reason, false); }

        /// <summary>
        /// isOpen이 거짓인 메뉴 닫힘 통지에서 아직 기록하지 않은 장치 입력을 요청한다.
        /// 닫는 프레임의 메뉴 입력도 APM에서 제외하도록 임시 집계 상태를 설정하고 복원한다.
        /// </summary>
        private void ObserveMenuState(bool isOpen)
        {
            if (isOpen || !IsActive) return;
            _isClosingMenu = true;
            try { MenuInputSamplingRequested?.Invoke(); }
            finally { _isClosingMenu = false; }
        }

        /// <summary>
        /// 적의 확정 점수 통지를 읽어 마지막 원본 점수를 보관한다.
        /// 씬 이탈 중 컴포넌트가 먼저 파괴되어도 중단 결과의 적 점수를 유지한다.
        /// </summary>
        private void ObserveEnemyState() { _lastEnemyScore = _enemy.Score; }

        /// <summary>
        /// 플레이어의 확정 점수와 콤보 통지를 읽는다.
        /// 점수 변경 스냅샷과 모든 100 단위 경계를 기록하며 감소한 콤보는 새 연속 구간으로 처리한다.
        /// </summary>
        private void ObservePlayerState()
        {
            if (!IsActive) return;
            double now = Time.realtimeSinceStartupAsDouble;
            long score = _player.Score;
            int combo = _player.Combo;
            if (score != _lastScore) Snapshot(now, score);
            if (combo > _previousCombo)
            {
                for (long boundary = ((long)_previousCombo / 100 + 1) * 100; boundary <= combo; boundary += 100)
                {
                    CloseInterval(now, score, "ComboBoundary", false);
                    PlaytestRecord row = CreateRecord("ComboBoundary", now);
                    row.Set(PlaytestColumn.Combo, boundary);
                    row.Set(PlaytestColumn.PlayerScore, score);
                    row.Set(PlaytestColumn.IntervalIndex, _intervalIndex + 1);
                    _writer.Write(row);
                    Snapshot(now, score);
                    _intervalIndex++;
                    _intervalCombo = (int)boundary;
                    _intervalStart = now;
                    _intervalScore = score;
                    _downCount = 0;
                    _writer.Flush();
                }
            }
            _previousCombo = combo;
            _lastScore = score;
        }

        /// <summary>
        /// now와 score의 누적 점수 스냅샷을 기록한다.
        /// 같은 시간과 점수의 중복은 생략하고 마지막 저장 값을 변경한다.
        /// </summary>
        private void Snapshot(double now, long score)
        {
            if (_snapshotTime == now && _snapshotScore == score) return;
            PlaytestRecord row = CreateRecord("ScoreSnapshot", now);
            row.Set(PlaytestColumn.PlayerScore, score);
            _writer.Write(row);
            _snapshotTime = now;
            _snapshotScore = score;
        }

        /// <summary>
        /// now의 구간 종료와score의 점수 차이를reason 및complete와 함께 기록한다.
        /// 실제 경과 시간으로 비율을 계산하고 0초 구간에서는 비율 열을 비워 둔다.
        /// </summary>
        private void CloseInterval(double now, long score, string reason, bool complete)
        {
            double duration = now - _intervalStart;
            long delta = score - _intervalScore;
            PlaytestRecord row = CreateRecord("IntervalSummary", now);
            row.Set(PlaytestColumn.IntervalIndex, _intervalIndex);
            row.Set(PlaytestColumn.IntervalKind, _intervalIndex == 0 ? "BeforeFirstCombo" : "AfterCombo");
            row.Set(PlaytestColumn.Combo, _intervalCombo);
            row.Set(PlaytestColumn.StartElapsedSeconds, _intervalStart);
            row.Set(PlaytestColumn.EndElapsedSeconds, now);
            row.Set(PlaytestColumn.DurationSeconds, duration);
            row.Set(PlaytestColumn.DownCount, _downCount);
            row.Set(PlaytestColumn.PlayerScoreStart, _intervalScore);
            row.Set(PlaytestColumn.PlayerScoreEnd, score);
            row.Set(PlaytestColumn.ScoreDelta, delta);
            row.Set(PlaytestColumn.EndReason, reason);
            if (reason != "ComboBoundary") row.Set(PlaytestColumn.IsComplete, complete);
            if (duration > 0d)
            {
                row.Set(PlaytestColumn.Apm, _downCount * 60d / duration);
                row.Set(PlaytestColumn.ScorePerSecond, delta / duration);
                row.Set(PlaytestColumn.ScorePerMinute, delta * 60d / duration);
            }
            _writer.Write(row);
            _writer.Flush();
        }

        /// <summary>
        /// type과 now로 현재 실행 정보를 가진 CSV 행을 생성한다.
        /// 스테이지 식별자를 추가한 새 기록을 반환한다.
        /// </summary>
        private PlaytestRecord CreateRecord(string type, double now)
        {
            PlaytestRecord row = new PlaytestRecord(type, now);
            AddStageContext(row);
            return row;
        }
    }
}
