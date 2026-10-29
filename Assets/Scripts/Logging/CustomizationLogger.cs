using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Logging
{
    public static class CustomizationTelemetry
    {
        public static event Action<CustomizationObservation> Observed;

        /// <summary>
        /// definition의 파츠 ID 또는 기존 일반 material의 이름 기반 ID를 반환한다.
        /// 두 입력이 모두 없으면 빈값을 반환하며 게임 상태는 변경하지 않는다.
        /// </summary>
        public static string GetKeycapElementId(Game.Tournament.KeycapDefinition definition, Material material)
        {
            return definition != null ? definition.Id : material != null ? "material:" + Uri.EscapeDataString(material.name) : "";
        }

        /// <summary>
        /// definition의 모델 여부와 기존 일반 material을 사용하여 키캡 종류를 반환한다.
        /// 파츠와 재질 모두 없으면 빈값을 반환하며 게임 상태는 변경하지 않는다.
        /// </summary>
        public static string GetKeycapKind(Game.Tournament.KeycapDefinition definition, Material material)
        {
            return definition != null ? (definition.IsArtisan ? "Artisan" : "Regular") : material != null ? "Regular" : "";
        }

        /// <summary>
        /// target, type, 부품·선택 ID와 적용 결과를 제작 행동 관찰자에게 전달한다.
        /// interactionId, 대상과 결과 및 작업 횟수도 포함하며 게임 상태는 변경하지 않는다.
        /// </summary>
        public static void Emit(KeyboardCustomization target, string type, string part = "", string element = "",
            string previous = "", bool success = true, bool changed = false, string scope = "", string key = "", string details = "", string keycapKind = "", string materialId = "",
            string interactionId = "", string targetId = "", string actionSource = "", string outcomeReason = "",
            int? attemptCount = null, int? successCount = null, int? screwCount = null)
        {
            if (!Application.isPlaying) return;
            // 허용된 제작 씬에서만 로거를 보장한다.
            CustomizationLogger.EnsureLogger();
            Observed?.Invoke(new CustomizationObservation(target, type, part, element, previous, success, changed, scope, key, details, keycapKind, materialId,
                interactionId, targetId, actionSource, outcomeReason, attemptCount, successCount, screwCount));
        }

        /// <summary>
        /// 새 앱 실행에서 관찰 구독을 초기화한다.
        /// 입력값 없이 이전 플레이 실행의 정적 참조를 제거한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetObservers() { Observed = null; }
    }

    public sealed class CustomizationObservation
    {
        public KeyboardCustomization Target { get; }
        public string Type { get; }
        public string Part { get; }
        public string Element { get; }
        public string Previous { get; }
        public bool Success { get; }
        public bool Changed { get; }
        public string Scope { get; }
        public string Key { get; }
        public string Details { get; }
        public string KeycapKind { get; }
        public string MaterialId { get; }
        public string InteractionId { get; }
        public string TargetId { get; }
        public string ActionSource { get; }
        public string OutcomeReason { get; }
        public int? AttemptCount { get; }
        public int? SuccessCount { get; }
        public int? ScrewCount { get; }

        /// <summary>
        /// 대상과 동작, 요소 및 적용 결과를 불변 관찰값으로 보관한다.
        /// 전달된 scope, key, details와 v5 작업 횟수를 포함하며 게임 상태는 변경하지 않는다.
        /// </summary>
        public CustomizationObservation(KeyboardCustomization target, string type, string part, string element,
            string previous, bool success, bool changed, string scope, string key, string details, string keycapKind = "", string materialId = "",
            string interactionId = "", string targetId = "", string actionSource = "", string outcomeReason = "",
            int? attemptCount = null, int? successCount = null, int? screwCount = null)
        {
            Target = target; Type = type; Part = part; Element = element; Previous = previous;
            Success = success; Changed = changed; Scope = scope; Key = key; Details = details;
            KeycapKind = keycapKind; MaterialId = materialId;
            InteractionId = interactionId; TargetId = targetId; ActionSource = actionSource; OutcomeReason = outcomeReason;
            AttemptCount = attemptCount; SuccessCount = successCount; ScrewCount = screwCount;
        }
    }

    [DisallowMultipleComponent]
    public sealed class CustomizationLogger : MonoBehaviour
    {
        private const string HEADER = "schema_version,build_version,tester_id,session_id,customization_run_id,keyboard_id,scene_path,sequence,record_type,elapsed_seconds,part,part_visit_id,element_id,previous_element_id,apply_success,value_changed,target_scope,keycap_index,start_elapsed_seconds,end_elapsed_seconds,duration_seconds,focused_duration_seconds,end_reason,is_complete,details,keycap_kind,material_id,snapshot_id,is_installed,test_run_id,cycle_id,cycle_index,configuration_id,parent_configuration_id,configuration_origin,linked_tournament_run_id,previous_tournament_run_id,tournament_index,participation_context,intervention_id,research_event_id,interaction_id,target_id,action_source,outcome_reason,attempt_count,success_count,screw_count";
        private static CustomizationLogger _instance;

        [Header("Runtime")]
        private readonly ConditionalWeakTable<KeyboardCustomization, KeyboardIdentity> _keyboards = new ConditionalWeakTable<KeyboardCustomization, KeyboardIdentity>();
        private StreamWriter _writer;
        private string _sessionId;
        private string _runId;
        private string _keyboardId;
        private string _scenePath;
        private KeyboardCustomization _target;
        private long _sequence;
        private double _runStart;
        private double _nextFlush;
        private bool _closed;
        private bool _failed;
        private bool _ownsSession;

        public string FilePath { get; private set; }
        public static KeyboardCustomization CurrentTarget => _instance != null ? _instance._target : null;
        public static string CurrentFilePath => _instance != null && _instance._ownsSession && !_instance._closed && _instance._writer != null
            ? _instance.FilePath
            : null;
        public string TesterId => PlaytestResearchSession.TesterId;

        void Awake()
        {
            if (_instance != null) { Destroy(this); return; }
            _instance = this;
            _ownsSession = true;
            DontDestroyOnLoad(gameObject);
            _sessionId = Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
#else
            string folder = Path.Combine(Application.persistentDataPath, "PlaytestLogs");
#endif
            FilePath = Path.Combine(folder, $"Customization_{DateTime.Now:yyyyMMdd_HHmmss}_{_sessionId}.csv");
            try
            {
                Directory.CreateDirectory(folder);
                _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
                _writer.WriteLine(HEADER);
            }
            catch (Exception exception) { Fail(exception); }
            CustomizationTelemetry.Observed += Observe;
            PlaytestResearchSession.Observed += HandleResearchEvent;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            Write("SessionStart");
            Flush();
            //if (!_failed) Debug.Log($"[CustomizationLogger] CSV 기록 시작: {FilePath}");
        }

        /// <summary>
        /// 실행 중 수집 인스턴스가 없으면 독립 로거를 생성한다.
        /// 입력값 없이 첫 UI 이벤트 전에 파일과 C# 구독을 준비하여 열린 씬의 연결 누락을 보완한다.
        /// </summary>
        public static void EnsureLogger()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (_instance == null && (sceneName == "WorkShop2" || sceneName == "W08_Main_Test" || sceneName == "W08_Minigame"))
                new GameObject("CustomizationLogger").AddComponent<CustomizationLogger>();
        }

        /// <summary>
        /// 의뢰 이벤트의 종류와 requestId를 기존 CSV에 기록한다.
        /// mailId, actionSource, result와 failureReason을 해당 열에 저장하며 제작 회차 상태는 변경하지 않는다.
        /// </summary>
        public static void RecordRequestEvent(string type, string requestId, string mailId = "",
            string actionSource = "", string result = "", string failureReason = "")
        {
            if (!Application.isPlaying || string.IsNullOrWhiteSpace(requestId)) return;
            EnsureLogger();
            if (_instance == null || _instance._closed) return;

            string[] row = _instance.CreateRow(type);
            row[4] = "";
            row[5] = "";
            row[6] = SceneManager.GetActiveScene().path;
            row[12] = mailId;
            row[42] = requestId;
            row[43] = actionSource;
            row[44] = result;
            row[24] = failureReason;
            _instance.WriteRow(row);
            _instance.Flush();
        }

        /// <summary>
        /// source의 미니게임 실행이 끝나면 appId와 runId 및 시작·종료 시각을 v5 CSV에 한 행 기록한다.
        /// 제작 회차와 무관하게 실제 체류 시간을 duration_seconds에 저장한다.
        /// </summary>
        public static void RecordMiniGameDuration(GameObject source, string appId, string runId, double startedAt, double endedAt)
        {
            EnsureLogger();
            if (_instance == null || _instance._closed || string.IsNullOrEmpty(runId)) return;
            string[] row = _instance.CreateRow("MiniGameDuration");
            row[4] = ""; row[5] = "";
            row[6] = source.scene.path;
            row[12] = appId;
            row[18] = Number(startedAt); row[19] = Number(endedAt);
            row[20] = Number(Math.Max(0d, endedAt - startedAt));
            row[41] = runId;
            _instance.WriteRow(row);
            _instance.Flush();
        }

        /// <summary>
        /// 현재 실행 환경의 Customization 로그 폴더를 연다.
        /// 에디터에서는 프로젝트 Logs, 빌드에서는 persistentDataPath 아래 PlaytestLogs를 사용한다.
        /// </summary>
        public static void OpenLogFolder()
        {
#if UNITY_EDITOR
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
#else
            string folder = Path.Combine(Application.persistentDataPath, "PlaytestLogs");
#endif
            Directory.CreateDirectory(folder);
            Application.OpenURL(new Uri(folder + Path.DirectorySeparatorChar).AbsoluteUri);
        }

        /// <summary>현재 커스터마이징 CSV의 버퍼를 디스크에 반영한다. 업로드 직전 파일을 읽어도 최신 이벤트가 포함되도록 한다.</summary>
        public static void FlushForUpload()
        {
            if (_instance != null && !_instance._closed) _instance.Flush();
        }

        /// <summary>
        /// 현재 커스터마이징 로그 세션을 LogUpload 사유로 마감한다.
        /// 업로드 전에 버퍼를 기록하고 파일과 이벤트 구독을 닫는다.
        /// </summary>
        public static void FinishForUpload()
        {
            if (_instance != null && _instance._ownsSession) _instance.Finish("LogUpload");
        }

        /// <summary>
        /// 최초 실행 씬이 WorkShop2 또는 W08 모니터 씬이면 로거의 존재를 보장한다.
        /// 이미 씬에 연결된 인스턴스는 그대로 사용하고 중복 파일을 생성하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureWorkshopLogger()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "WorkShop2" || sceneName == "W08_Main_Test" || sceneName == "W08_Minigame") EnsureLogger();
        }

        void Update()
        {
            if (!_ownsSession || _closed) return;
            if (Time.realtimeSinceStartupAsDouble >= _nextFlush)
            {
                Flush();
                _nextFlush = Time.realtimeSinceStartupAsDouble + 1d;
            }
        }

        void OnApplicationQuit() { if (_ownsSession) Finish("ApplicationQuit"); }
        void OnDestroy() { if (_ownsSession) { Finish("LoggerDestroyed"); if (_instance == this) _instance = null; } }

        /// <summary>
        /// 편집이 끝난 상태에서 testerId로 새 연구 참여자를 시작한다.
        /// PlaytestResearchSession의 식별자 문맥을 갱신하며 게임 설정과 저장 파일은 유지한다.
        /// </summary>
        public void SetTesterId(string testerId)
        {
            if (string.IsNullOrWhiteSpace(testerId)) throw new ArgumentException("Tester ID is required.", nameof(testerId));
            PlaytestResearchSession.StartParticipant(testerId);
        }

        /// <summary>
        /// observation에서 허용한 메뉴, 선택, 설치·제거 및 도구 행동을 열린 제작 회차에 기록한다.
        /// target과 type을 확인하고 중복 선택은 제외하며, 제작 종료 신호가 오면 회차 요약을 기록한다.
        /// </summary>
        private void Observe(CustomizationObservation observation)
        {
            if (_closed) return;
            if (observation.Type == "CustomizationStart")
            {
                EndRun("Reentered", false);
                _target = observation.Target;
                if (_target == null) return;
                _runId = Guid.NewGuid().ToString("N");
                _keyboardId = _keyboards.GetValue(_target, CreateIdentity).Id;
                _scenePath = _target.gameObject.scene.path;
                _runStart = Time.realtimeSinceStartupAsDouble;
                PlaytestResearchSession.BeginCustomization(_runId);
                Write("CustomizationStart");
                CaptureConfiguration("Initial");
            }
            else if (_runId == null) return;
            else if (observation.Type == "CustomizationEnd" && observation.Success && observation.Details == "NextScene")
                EndRun("NextScene", true);
            else if (!ReferenceEquals(_target, observation.Target)) return;
            else if (observation.Type == "CustomizationEnd") EndRun(observation.Details, observation.Success);
            else if (observation.Type == "PartChanged")
            {
                WriteObservation(observation, "PartMenuChanged");
            }
            else
            {
                string recordType = observation.Type == "ElementSelected" ? "OptionSelected" : observation.Type;
                if (IsSupportedRecordType(recordType) && (recordType != "OptionSelected" || observation.Changed))
                    WriteObservation(observation, recordType);
            }
            Flush();
        }

        /// <summary>
        /// PlaytestResearchSession의 세션 종료 신호를 받아 열린 제작 회차를 닫는다.
        /// type과 reason을 사용하며 TestEnd가 전달되면 실패로 간주하지 않고 미완료 상태로 마감한다.
        /// </summary>
        private void HandleResearchEvent(string type, string reason, string eventId)
        {
            if (type == "TestEnd") EndRun(reason, false);
        }

        /// <summary>
        /// observation의 측정 이벤트를 CSV 한 행으로 기록한다.
        /// recordType을 사용하고 기존 열과 v5 개수 열에 식별값 및 결과를 저장한다.
        /// </summary>
        private void WriteObservation(CustomizationObservation observation, string recordType)
        {
            string[] row = CreateRow(recordType);
            row[10] = observation.Part;
            row[12] = observation.Element; row[13] = observation.Previous;
            if (recordType != "BulkPlacementStarted")
            {
                row[14] = Bool(observation.Success);
                row[15] = Bool(observation.Changed);
            }
            row[16] = observation.Scope; row[17] = observation.Key; row[24] = observation.Details;
            row[25] = observation.KeycapKind; row[26] = observation.MaterialId;
            row[41] = observation.InteractionId; row[42] = observation.TargetId;
            row[43] = observation.ActionSource; row[44] = observation.OutcomeReason;
            row[45] = observation.AttemptCount?.ToString(CultureInfo.InvariantCulture);
            row[46] = observation.SuccessCount?.ToString(CultureInfo.InvariantCulture);
            row[47] = observation.ScrewCount?.ToString(CultureInfo.InvariantCulture);
            WriteRow(row);
        }

        /// <summary>
        /// 새 행동 로그에 포함할 recordType인지 반환한다.
        /// 정의된 메뉴, 설치·제거, 도구, 조립, 나사 및 일괄 배치 이벤트만 통과시킨다.
        /// </summary>
        private static bool IsSupportedRecordType(string recordType)
        {
            return recordType == "OptionSelected" || recordType == "PartInstallAttempted"
                || recordType == "PartRemoveAttempted" || recordType == "ToolPickedUp"
                || recordType == "ToolPutDown" || recordType == "PartReturned"
                || recordType == "StructureDragCancelled" || recordType == "ScrewChanged"
                || recordType == "BulkPlacementStarted" || recordType == "BulkPlacementEnded";
        }

        /// <summary>편집 대상에 연결된 storage에서 실제 구성을 읽어 식별자를 갱신한다. 실패는 품질 이벤트로 남기며 편집은 유지한다.</summary>
        private void CaptureConfiguration(string reason)
        {
            foreach (KeyboardCustomizationStorage storage in UnityEngine.Object.FindObjectsByType<KeyboardCustomizationStorage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (storage.Customization != _target) continue;
                try
                {
                    PlaytestResearchSession.CaptureConfiguration(storage.CaptureCustomization(_target), "Workshop:" + reason);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is FormatException)
                {
                    PlaytestResearchSession.Emit("ConfigurationCaptureFailed", "WorkshopCapture");
                }
                return;
            }
            PlaytestResearchSession.Emit("ConfigurationCaptureFailed", "NoMatchingStorage");
        }

        /// <summary>
        /// 열린 회차를 reason과 complete로 마감한다.
        /// 실제 최종 설정과 회차 시간을 기록하고 식별자를 비운다.
        /// </summary>
        private void EndRun(string reason, bool complete)
        {
            if (_runId == null) return;
            CaptureConfiguration(complete ? "Final" : "Interrupted");
            string[] row = CreateRow("CustomizationEnd");
            double now = Time.realtimeSinceStartupAsDouble;
            row[18] = Number(_runStart); row[19] = Number(now); row[20] = Number(now - _runStart);
            row[22] = reason; row[23] = Bool(complete);
            WriteRow(row);
            _runId = null; _target = null; _keyboardId = null;
            PlaytestResearchSession.EndCustomization();
            Flush();
        }

        /// <summary>
        /// scene가 현재 편집 대상의 씬이면 미완료로 마감한다.
        /// 씬 경로를 비교하며 다른 씬 언로드는 기록에 영향을 주지 않는다.
        /// </summary>
        private void HandleSceneUnloaded(Scene scene) { if (scene.path == _scenePath) EndRun("SceneExit", false); }

        /// <summary>
        /// type의 이벤트 행에 현재 식별자와 순번 및 실시간을 설정하여 반환한다.
        /// 해당하지 않는 열은 빈 값으로 유지한다.
        /// </summary>
        private string[] CreateRow(string type)
        {
            string[] row = new string[48];
            row[0] = "5"; row[1] = Application.version; row[2] = TesterId; row[3] = _sessionId;
            row[4] = _runId; row[5] = _keyboardId; row[6] = _scenePath;
            row[7] = (++_sequence).ToString(CultureInfo.InvariantCulture); row[8] = type;
            row[9] = Number(Time.realtimeSinceStartupAsDouble);
            row[29] = PlaytestResearchSession.TestRunId;
            row[30] = PlaytestResearchSession.CycleId;
            row[31] = PlaytestResearchSession.CycleIndex.ToString(CultureInfo.InvariantCulture);
            row[32] = PlaytestResearchSession.ConfigurationId;
            row[33] = PlaytestResearchSession.ParentConfigurationId;
            row[34] = PlaytestResearchSession.ConfigurationOrigin;
            row[35] = PlaytestResearchSession.TournamentRunId;
            row[36] = PlaytestResearchSession.PreviousTournamentRunId;
            row[37] = PlaytestResearchSession.TournamentIndex.ToString(CultureInfo.InvariantCulture);
            row[38] = PlaytestResearchSession.ParticipationContext;
            row[39] = PlaytestResearchSession.InterventionId;
            return row;
        }

        /// <summary>
        /// type의 공통 이벤트를 한 행 기록한다.
        /// 입력된 종류 외의 문맥은 현재 회차 상태에서 읽는다.
        /// </summary>
        private void Write(string type) { WriteRow(CreateRow(type)); }

        /// <summary>
        /// row를 올바르게 CSV 이스케이프하여 파일에 순차 추가한다.
        /// 저장 오류는 게임 진행에 전달하지 않고 최초 한 번 경고한다.
        /// </summary>
        private void WriteRow(string[] row)
        {
            if (_failed || _writer == null) return;
            try
            {
                for (int index = 0; index < row.Length; index++)
                {
                    if (index > 0) _writer.Write(',');
                    string value = row[index] ?? "";
                    _writer.Write('"'); _writer.Write(value.Replace("\"", "\"\"")); _writer.Write('"');
                }
                _writer.WriteLine();
            }
            catch (Exception exception) { Fail(exception); }
        }

        /// <summary>
        /// 열린 CSV의 버퍼를 디스크로 전달한다.
        /// 입력값 없이 저장 실패를 한 번만 보고한다.
        /// </summary>
        private void Flush() { if (_writer != null && !_failed) { try { _writer.Flush(); } catch (Exception exception) { Fail(exception); } } }

        /// <summary>
        /// exception의 저장 실패를 최초 한 번 경고한다.
        /// 이후 파일 작업만 중단하여 커스터마이징 동작을 유지한다.
        /// </summary>
        private void Fail(Exception exception) { if (_failed) return; _failed = true; Debug.LogWarning($"[CustomizationLogger] 저장 중단: {exception.Message}"); }

        /// <summary>
        /// reason으로 진행 회차와 세션을 한 번 마감한다.
        /// 관찰 구독을 해제하고 파일을 닫아 종료 콜백의 중복 기록을 방지한다.
        /// </summary>
        private void Finish(string reason)
        {
            if (_closed) return;
            EndRun(reason, false);
            Write("SessionEnd"); Flush(); _closed = true;
            CustomizationTelemetry.Observed -= Observe;
            PlaytestResearchSession.Observed -= HandleResearchEvent;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            try { _writer?.Dispose(); } catch (Exception exception) { Fail(exception); }
            _writer = null;
        }

        /// <summary>value를 문화권과 무관한 실시간 문자열로 변환하여 반환한다.</summary>
        private static string Number(double value) { return value.ToString("F6", CultureInfo.InvariantCulture); }
        /// <summary>value를 CSV의 소문자 참 또는 거짓 문자열로 변환하여 반환한다.</summary>
        private static string Bool(bool value) { return value ? "true" : "false"; }
        /// <summary>target의 새 키보드 식별 상태를 생성하여 반환하며 대상 설정은 변경하지 않는다.</summary>
        private static KeyboardIdentity CreateIdentity(KeyboardCustomization target) { return new KeyboardIdentity(); }
        /// <summary>새 앱 실행의 로거 참조를 초기화하며 이전 실행 인스턴스를 재사용하지 않는다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() { _instance = null; }

#if UNITY_EDITOR
        /// <summary>
        /// Unity Editor의 Play 모드 중지 이벤트를 한 번 등록한다.
        /// Play 모드가 끝나기 전에 현재 커스터마이징 CSV를 닫을 수 있도록 연결한다.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorPlayModeCallback()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += HandleEditorPlayModeStateChanged;
        }

        /// <summary>
        /// Unity Editor가 Play 모드를 종료할 때 현재 커스터마이징 세션을 마감한다.
        /// state가 ExitingPlayMode이면 CSV와 이벤트 구독을 닫는다.
        /// </summary>
        private static void HandleEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode || _instance == null || !_instance._ownsSession) return;
            _instance.Finish("EditorPlayModeStopped");
        }
#endif

        private sealed class KeyboardIdentity { public string Id { get; } = Guid.NewGuid().ToString("N"); }
    }
}

