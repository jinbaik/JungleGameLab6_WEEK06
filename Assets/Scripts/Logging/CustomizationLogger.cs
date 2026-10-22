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
        /// target의 실제 UI 동작과 part, element, previous 및 적용 결과를 관찰자에게 전달한다.
        /// scope와 key 및 details는 추가 문맥이며 게임 설정과 저장 상태는 변경하지 않는다.
        /// </summary>
        public static void Emit(KeyboardCustomization target, string type, string part = "", string element = "",
            string previous = "", bool success = true, bool changed = false, string scope = "", string key = "", string details = "", string keycapKind = "", string materialId = "")
        {
            if (!Application.isPlaying) return;
            // WorkShop0에서도 커마 조작은 기록하며 전투 로거 차단과 분리한다.
            CustomizationLogger.EnsureLogger();
            Observed?.Invoke(new CustomizationObservation(target, type, part, element, previous, success, changed, scope, key, details, keycapKind, materialId));
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

        /// <summary>
        /// 대상과 동작, 요소 및 적용 결과를 불변 관찰값으로 보관한다.
        /// 전달된 scope, key와 details를 포함하며 게임 상태는 변경하지 않는다.
        /// </summary>
        public CustomizationObservation(KeyboardCustomization target, string type, string part, string element,
            string previous, bool success, bool changed, string scope, string key, string details, string keycapKind = "", string materialId = "")
        {
            Target = target; Type = type; Part = part; Element = element; Previous = previous;
            Success = success; Changed = changed; Scope = scope; Key = key; Details = details;
            KeycapKind = keycapKind; MaterialId = materialId;
        }
    }

    [DisallowMultipleComponent]
    public sealed class CustomizationLogger : MonoBehaviour
    {
        private const string HEADER = "schema_version,build_version,tester_id,session_id,customization_run_id,keyboard_id,scene_path,sequence,record_type,elapsed_seconds,part,part_visit_id,element_id,previous_element_id,apply_success,value_changed,target_scope,keycap_index,start_elapsed_seconds,end_elapsed_seconds,duration_seconds,focused_duration_seconds,end_reason,is_complete,details,keycap_kind,material_id,snapshot_id,is_installed,test_run_id,cycle_id,cycle_index,configuration_id,parent_configuration_id,configuration_origin,linked_tournament_run_id,previous_tournament_run_id,tournament_index,participation_context,intervention_id,research_event_id";
        private static CustomizationLogger _instance;

        [Header("Runtime")]
        private readonly ConditionalWeakTable<KeyboardCustomization, KeyboardIdentity> _keyboards = new ConditionalWeakTable<KeyboardCustomization, KeyboardIdentity>();
        private StreamWriter _writer;
        private string _sessionId;
        private string _runId;
        private string _keyboardId;
        private string _scenePath;
        private string _part;
        private string _visitId;
        private KeyboardCustomization _target;
        private long _sequence;
        private double _runStart;
        private double _partStart;
        private double _lastTime;
        private double _runFocused;
        private double _partFocused;
        private double _nextFlush;
        private bool _focused;
        private bool _externalDialog;
        private bool _closed;
        private bool _failed;
        private bool _ownsSession;

        public string FilePath { get; private set; }
        public static string CurrentFilePath => _instance != null && _instance._ownsSession && !_instance._closed && _instance._writer != null
            ? _instance.FilePath
            : null;
        public string TesterId => PlaytestResearchSession.TesterId;

        void Awake()
        {
            if (_instance != null) { Destroy(gameObject); return; }
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
            _lastTime = Time.realtimeSinceStartupAsDouble;
            _focused = HasFocus();
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
            if (_instance == null)
                new GameObject("CustomizationLogger").AddComponent<CustomizationLogger>();
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
        /// 최초 실행 씬이 WorkShop2 또는 WorkShop0이면 로거의 존재를 보장한다.
        /// 이미 씬에 연결된 인스턴스는 그대로 사용하고 중복 파일을 생성하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureWorkshopLogger()
        {
            if (SceneManager.GetActiveScene().name == "WorkShop2" || Game.Workshop.CustomizationOnlyWorkshop.IsActive) EnsureLogger();
        }

        void Update()
        {
            if (!_ownsSession || _closed) return;
            Accumulate();
            _focused = HasFocus();
            if (Time.realtimeSinceStartupAsDouble >= _nextFlush)
            {
                Flush();
                _nextFlush = Time.realtimeSinceStartupAsDouble + 1d;
            }
        }

        void OnApplicationFocus(bool focused) { if (_ownsSession && !_closed) { Accumulate(); _focused = focused && HasFocus(); } }
        void OnApplicationQuit() { if (_ownsSession) Finish("ApplicationQuit"); }
        void OnDestroy() { if (_ownsSession) { Finish("LoggerDestroyed"); if (_instance == this) _instance = null; } }

        /// <summary>
        /// 작업실의 편집 종료 상태에서 testerId로 새 참여자를 시작한다.
        /// 두 CSV의 식별자를 함께 변경하며 게임 설정과 저장 파일은 유지한다.
        /// </summary>
        public void SetTesterId(string testerId)
        {
            if (string.IsNullOrWhiteSpace(testerId)) throw new ArgumentException("Tester ID is required.", nameof(testerId));
            PlaytestResearchSession.StartParticipant(testerId);
        }

        /// <summary>연구 이벤트를 eventId로 전투 CSV와 연결한다. 참여자 종료 전에 열린 편집을 마감하고 reason을 기록한다.</summary>
        private void HandleResearchEvent(string type, string reason, string eventId)
        {
            if (_closed) return;
            if (type == "TestEnd") EndRun(reason, false);
            string[] row = CreateRow(type);
            row[4] = PlaytestResearchSession.CustomizationRunId;
            row[6] = SceneManager.GetActiveScene().path;
            row[22] = reason;
            row[40] = eventId;
            if (type == "TournamentFinished" || type == "TournamentInterrupted") row[23] = Bool(type == "TournamentFinished");
            WriteRow(row);
            Flush();
        }

        /// <summary>
        /// observation의 실제 UI 이벤트를 열린 회차에 기록한다.
        /// 진입, 항목 이동, 선택 및 이미지 동작을 분리하고 전투 진입 시 열린 편집 회차를 정상 마감한다.
        /// 전투 진입 이외에는 다른 키보드의 이벤트를 제외한다.
        /// </summary>
        private void Observe(CustomizationObservation observation)
        {
            if (_closed) return;
            Accumulate();
            if (observation.Type == "CustomizationStart")
            {
                EndRun("Reentered", false);
                _target = observation.Target;
                if (_target == null) return;
                _runId = Guid.NewGuid().ToString("N");
                _keyboardId = _keyboards.GetValue(_target, CreateIdentity).Id;
                _scenePath = _target.gameObject.scene.path;
                _runStart = Time.realtimeSinceStartupAsDouble;
                _runFocused = 0d;
                _externalDialog = false;
                PlaytestResearchSession.BeginCustomization(_runId);
                Write("CustomizationStart");
                Snapshot("Initial");
            }
            else if (_runId == null) return;
            else if (observation.Type == "CustomizationEnd" && observation.Success && observation.Details == "NextScene")
                EndRun("NextScene", true);
            else if (!ReferenceEquals(_target, observation.Target)) return;
            else if (observation.Type == "CustomizationEnd") EndRun(observation.Details, observation.Success);
            else if (observation.Type == "PartChanged")
            {
                EndPart("PartChanged", true);
                if (!string.IsNullOrEmpty(observation.Part))
                {
                    _part = observation.Part;
                    _visitId = Guid.NewGuid().ToString("N");
                    _partStart = Time.realtimeSinceStartupAsDouble;
                    _partFocused = 0d;
                    Write("PartVisitStart");
                }
            }
            else if (observation.Type == "ExternalDialogStart") _externalDialog = true;
            else if (observation.Type == "ExternalDialogEnd") { _externalDialog = false; _focused = HasFocus(); }
            else
            {
                string[] row = CreateRow(observation.Type);
                row[10] = observation.Part;
                row[12] = observation.Element; row[13] = observation.Previous;
                row[14] = Bool(observation.Success); row[15] = Bool(observation.Changed);
                row[16] = observation.Scope; row[17] = observation.Key; row[24] = observation.Details;
                row[25] = observation.KeycapKind; row[26] = observation.MaterialId;
                WriteRow(row);
                if (observation.Success && (observation.Type == "Save" || observation.Type == "Load" || observation.Type == "KeycapApply"
                    || observation.Type == "ImageAction" && (observation.Element == "Apply" || observation.Element == "Replace" || observation.Element == "Remove")))
                    Snapshot(observation.Type + ":" + observation.Element);
            }
            Flush();
        }

        /// <summary>
        /// 현재 실제 설정을 reason 스냅샷으로 저장한다.
        /// 이미지 바이트나 사용자 파일 경로를 포함하지 않고 공개된 파츠 상태만 읽는다.
        /// </summary>
        private void Snapshot(string reason)
        {
            if (_target == null) return;
            if (reason == "Initial" || reason == "Final" || reason == "Interrupted") CaptureConfiguration(reason);
            string snapshotId = Guid.NewGuid().ToString("N");
            foreach (string[] value in _target.GetCustomizationLogValues())
            {
                string[] row = CreateRow("CustomizationSnapshot");
                row[10] = value[0]; row[12] = value[1]; row[22] = reason;
                row[27] = snapshotId;
                if (reason != "Initial" && reason != "Final" && reason != "Interrupted")
                {
                    row[32] = null;
                    row[33] = PlaytestResearchSession.ConfigurationId;
                    row[34] = "UncommittedWorkshopSnapshot";
                }
                if (value.Length > 2)
                {
                    row[17] = value[2]; row[25] = value[3]; row[26] = value[4]; row[28] = value[5];
                    row[16] = "SingleKeycap";
                }
                WriteRow(row);
            }
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
        /// 열린 파츠 방문을 reason과 complete로 한 번 마감한다.
        /// 실제 경과 시간과 포커스 시간을 저장하고 방문 상태를 비운다.
        /// </summary>
        private void EndPart(string reason, bool complete)
        {
            if (_visitId == null) return;
            Accumulate();
            string[] row = CreateRow("PartVisitEnd");
            double now = Time.realtimeSinceStartupAsDouble;
            row[18] = Number(_partStart); row[19] = Number(now); row[20] = Number(now - _partStart);
            row[21] = Number(_partFocused); row[22] = reason; row[23] = Bool(complete);
            WriteRow(row);
            _visitId = null; _part = null;
        }

        /// <summary>
        /// 열린 회차를 reason과 complete로 마감한다.
        /// 마지막 파츠와 실제 최종 설정 및 회차 시간을 기록하고 식별자를 비운다.
        /// </summary>
        private void EndRun(string reason, bool complete)
        {
            if (_runId == null) return;
            Accumulate();
            EndPart(reason, complete);
            Snapshot(complete ? "Final" : "Interrupted");
            string[] row = CreateRow("CustomizationEnd");
            double now = Time.realtimeSinceStartupAsDouble;
            row[18] = Number(_runStart); row[19] = Number(now); row[20] = Number(now - _runStart);
            row[21] = Number(_runFocused); row[22] = reason; row[23] = Bool(complete);
            WriteRow(row);
            _runId = null; _target = null; _keyboardId = null; _externalDialog = false;
            PlaytestResearchSession.EndCustomization();
            Flush();
        }

        /// <summary>
        /// scene가 현재 편집 대상의 씬이면 미완료로 마감한다.
        /// 씬 경로를 비교하며 다른 씬 언로드는 기록에 영향을 주지 않는다.
        /// </summary>
        private void HandleSceneUnloaded(Scene scene) { if (scene.path == _scenePath) EndRun("SceneExit", false); }

        /// <summary>
        /// 이전 관찰 이후 실시간을 현재 포커스 상태에 따라 누적한다.
        /// 파일 선택 대화상자 시간은 포커스 체류에서 제외하며 전체 시간은 유지한다.
        /// </summary>
        private void Accumulate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double delta = Math.Max(0d, now - _lastTime);
            if (_runId != null && _focused && !_externalDialog)
            {
                _runFocused += delta;
                if (_visitId != null) _partFocused += delta;
            }
            _lastTime = now;
        }

        /// <summary>
        /// 실제 게임 창의 포커스 여부를 반환한다.
        /// 에디터에서는 GameView와 일시정지 상태를 함께 확인한다.
        /// </summary>
        private bool HasFocus()
        {
#if UNITY_EDITOR
            var window = UnityEditor.EditorWindow.focusedWindow;
            return Application.isFocused && !UnityEditor.EditorApplication.isPaused && window != null && window.GetType().Name == "GameView";
#else
            return Application.isFocused;
#endif
        }

        /// <summary>
        /// type의 이벤트 행에 현재 식별자와 순번 및 실시간을 설정하여 반환한다.
        /// 해당하지 않는 열은 빈 값으로 유지한다.
        /// </summary>
        private string[] CreateRow(string type)
        {
            string[] row = new string[41];
            row[0] = "3"; row[1] = Application.version; row[2] = TesterId; row[3] = _sessionId;
            row[4] = _runId; row[5] = _keyboardId; row[6] = _scenePath;
            row[7] = (++_sequence).ToString(CultureInfo.InvariantCulture); row[8] = type;
            row[9] = Number(Time.realtimeSinceStartupAsDouble); row[10] = _part; row[11] = _visitId;
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

