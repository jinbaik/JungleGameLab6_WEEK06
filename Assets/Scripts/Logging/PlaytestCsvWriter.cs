using System;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace Game.Logging
{
    public enum PlaytestColumn
    {
        SessionId, BuildVersion, Sequence, RecordType, ElapsedSeconds, StageId, StageRunId,
        Key, Event, PressSequence, HeldSeconds, EndReason, KeyDownCount, IntervalIndex,
        IntervalKind, Combo, StartElapsedSeconds, EndElapsedSeconds, DurationSeconds,
        DownCount, Apm, PlayerScore, EnemyScore, PlayerScoreStart, PlayerScoreEnd,
        ScoreDelta, ScorePerSecond, ScorePerMinute, IsComplete,
        SchemaVersion, TournamentRunId, RoundIndex, BattleKind, Phase, Owner,
        KeyboardId, KeyboardSpawnIndex, SpawnKind, InputState, WasBroken,
        BrokenKeyDownCount, BrokenDownCount, ShopVisitId,
        SnapshotId, SpecialType, Quantity, Action, OfferSlot, OfferGeneration,
        Price, CurrencyBefore, CurrencyAfter, LayoutRow, LayoutColumn, EffectId,
        IsConfirmed, InventoryCapacity, InventoryTotal, ShopTransactionId
    }

    public sealed class PlaytestRecord
    {
        private readonly string[] _values = new string[Enum.GetValues(typeof(PlaytestColumn)).Length];
        public string[] Values => _values;

        /// <summary>
        /// type과 elapsed로 기록 종류와 실행 경과 시간을 설정한다.
        /// 해당하지 않는 열은 빈 값인 새 행을 준비한다.
        /// </summary>
        public PlaytestRecord(string type, double elapsed)
        {
            Set(PlaytestColumn.SchemaVersion, 3L);
            Set(PlaytestColumn.RecordType, type);
            Set(PlaytestColumn.ElapsedSeconds, elapsed);
        }

        /// <summary>
        /// column에 문자열 value를 저장한다.
        /// 숫자나 시간의 별도 변환 없이 해당 CSV 열 값을 변경한다.
        /// </summary>
        public void Set(PlaytestColumn column, string value) { _values[(int)column] = value; }

        /// <summary>
        /// column에 정수 value를 문화권과 무관한 문자열로 저장한다.
        /// 정수 정밀도를 유지하여 해당 CSV 열 값을 변경한다.
        /// </summary>
        public void Set(PlaytestColumn column, long value) { Set(column, value.ToString(CultureInfo.InvariantCulture)); }

        /// <summary>
        /// column에 유한한 실수 value를 소수점 6자리로 저장한다.
        /// 유한하지 않은 값은 빈 문자열로 기록하여 비율 오류를 방지한다.
        /// </summary>
        public void Set(PlaytestColumn column, double value)
        {
            Set(column, double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : value.ToString("F6", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// column에 bool value를 소문자 참과 거짓 문자열로 저장한다.
        /// 정상 완료 여부를 나타내는 해당 열을 변경한다.
        /// </summary>
        public void Set(PlaytestColumn column, bool value) { Set(column, value ? "true" : "false"); }
    }

    public sealed class PlaytestCsvWriter
    {
        private const string HEADER = "session_id,build_version,sequence,record_type,elapsed_seconds,stage_id,stage_run_id,key,event,press_sequence,held_seconds,end_reason,key_down_count,interval_index,interval_kind,combo,start_elapsed_seconds,end_elapsed_seconds,duration_seconds,down_count,apm,player_score,enemy_score,player_score_start,player_score_end,score_delta,score_per_second,score_per_minute,is_complete,schema_version,tournament_run_id,round_index,battle_kind,phase,owner,keyboard_id,keyboard_spawn_index,spawn_kind,input_state,was_broken,broken_key_down_count,broken_down_count,shop_visit_id,snapshot_id,special_type,quantity,action,offer_slot,offer_generation,price,currency_before,currency_after,layout_row,layout_column,effect_id,is_confirmed,inventory_capacity,inventory_total,shop_transaction_id";
        private readonly string _sessionId;
        private readonly string _buildVersion;
        private StreamWriter _writer;
        private long _sequence;
        private bool _failed;
        public string FolderPath { get; }
        public string FilePath { get; }

        /// <summary>
        /// folderPath 아래 sessionId와 buildVersion을 사용할 UTF-8 BOM CSV를 연다.
        /// 헤더를 한 번 작성하고 실패 시 저장만 중단하여 게임 진행을 유지한다.
        /// </summary>
        public PlaytestCsvWriter(string folderPath, string sessionId, string buildVersion)
        {
            _sessionId = sessionId;
            _buildVersion = buildVersion;
            FolderPath = folderPath;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            FilePath = Path.Combine(folderPath, $"Playtest_{timestamp}_{sessionId}.csv");
            try
            {
                Directory.CreateDirectory(folderPath);
                _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
                _writer.WriteLine(HEADER);
                _writer.Flush();
            }
            catch (Exception exception) { Fail(exception); }
        }

        /// <summary>
        /// record에 공통 실행 정보와 순번을 넣고 CSV 한 행을 기록한다.
        /// 문자열을 CSV 규칙대로 이스케이프하고 할당한 순번을 반환한다.
        /// </summary>
        public long Write(PlaytestRecord record)
        {
            long sequence = ++_sequence;
            record.Set(PlaytestColumn.SessionId, _sessionId);
            record.Set(PlaytestColumn.BuildVersion, _buildVersion);
            record.Set(PlaytestColumn.Sequence, sequence);
            if (record.Values[(int)PlaytestColumn.RecordType] == "KeyInput"
                && record.Values[(int)PlaytestColumn.Event] == "Down")
                record.Set(PlaytestColumn.PressSequence, sequence);
            if (_writer == null || _failed) return sequence;
            try
            {
                StringBuilder line = new StringBuilder();
                for (int index = 0; index < record.Values.Length; index++)
                {
                    if (index > 0) line.Append(',');
                    string value = record.Values[index] ?? string.Empty;
                    if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                        line.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                    else line.Append(value);
                }
                _writer.WriteLine(line.ToString());
            }
            catch (Exception exception) { Fail(exception); }
            return sequence;
        }

        /// <summary>
        /// 파일 버퍼의 현재 기록을 디스크로 전달한다.
        /// 입력값 없이 저장 실패 시 반복 오류 출력을 막는다.
        /// </summary>
        public void Flush()
        {
            if (_writer == null || _failed) return;
            try { _writer.Flush(); }
            catch (Exception exception) { Fail(exception); }
        }

        /// <summary>
        /// 열린 파일을 Flush하고 한 번만 닫는다.
        /// 입력값 없이 파일 참조를 정리하여 중복 종료를 방지한다.
        /// </summary>
        public void Close()
        {
            Flush();
            StreamWriter writer = _writer;
            _writer = null;
            try { writer?.Dispose(); }
            catch (Exception exception) { Fail(exception); }
        }

        /// <summary>
        /// exception의 저장 오류를 최초 한 번만 경고한다.
        /// 실패 상태를 저장하여 이후 파일 쓰기를 중단한다.
        /// </summary>
        private void Fail(Exception exception)
        {
            if (_failed) return;
            _failed = true;
            Debug.LogWarning($"[PlaytestLogger] CSV 저장 중단: {exception.Message}");
        }
    }
}
