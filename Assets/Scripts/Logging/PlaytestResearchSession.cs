using System;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Game.Logging
{
    /// <summary>사후 설문과 제작·전투 기록을 연결하는 실행 문맥과 진행자용 기록 화면이다.</summary>
    public sealed class PlaytestResearchSession : MonoBehaviour
    {
        private const string SURVEY_URL = "https://docs.google.com/forms/d/e/1FAIpQLScHe8Lc1GgrmsLXcW9egP4cRd9rQ4LMgZx3Db-nZigFXcEW6Q/viewform";
        private static string _fingerprint;
        private static bool _nextCyclePending;
        private static bool _finishedForUpload;

        public static event Action<string, string, string> Observed;
        public static string TestRunId { get; private set; }
        public static string TesterId { get; private set; }
        public static string CycleId { get; private set; }
        public static int CycleIndex { get; private set; }
        public static string CustomizationRunId { get; private set; }
        public static string ConfigurationId { get; private set; }
        public static string ParentConfigurationId { get; private set; }
        public static string ConfigurationOrigin { get; private set; }
        public static string TournamentRunId { get; private set; }
        public static string PreviousTournamentRunId { get; private set; }
        public static int TournamentIndex { get; private set; }
        public static string ParticipationContext { get; private set; }
        public static string InterventionId { get; private set; }
        public static bool IsCustomizing { get; private set; }
        public static bool IsUploadConfirmationOpen { get; private set; }

        [Header("Runtime")]
        private bool _panelOpen;
        private bool _uploadConfirmationOpen;
        private IDisposable _uploadConfirmationCursorDialog;
        private string _interventionReason = "OperationHelp";

        /// <summary>새 앱 실행의 구독과 식별자를 초기화한다. 이전 실행의 문맥은 재사용하지 않는다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            Observed = null;
            _finishedForUpload = false;
            IsUploadConfirmationOpen = false;
            ResetIdentity(null);
        }

        /// <summary>최초 씬에 진행자용 기록 화면을 생성한다. 씬 이동 후에도 같은 테스트 문맥을 유지한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreatePanel()
        {
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive)
            {
                // WorkShop0에서는 전투 연구 세션과 진행자용 기록 화면을 생성하지 않는다.
                // new GameObject("PlaytestResearchSession").AddComponent<PlaytestResearchSession>();
                return;
            }
            new GameObject("PlaytestResearchSession").AddComponent<PlaytestResearchSession>();
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void Start() { Emit("TestStart", "ApplicationLaunch"); }

        void OnDestroy()
        {
            SetUploadConfirmationOpen(false);
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        void OnApplicationQuit()
        {
            if (_finishedForUpload) return;
            if (!string.IsNullOrEmpty(InterventionId)) Emit("InterventionEnd", "ApplicationQuit");
            Emit("TestEnd", "ApplicationQuit");
        }

        /// <summary>
        /// 업로드 동의를 기록하고 현재 연구 세션의 마지막 이벤트를 보낸다.
        /// 이후 앱 종료 콜백에서 TestEnd가 중복 기록되지 않도록 표시한다.
        /// </summary>
        public static void FinishForUpload()
        {
            if (_finishedForUpload) return;
            if (!string.IsNullOrEmpty(InterventionId))
            {
                Emit("InterventionEnd", "LogUpload");
                InterventionId = null;
            }
            Emit("TestEnd", "LogUpload");
            _finishedForUpload = true;
        }

        /// <summary>
        /// 로드된 scene 경로를 기록하고 Workshop2를 떠날 때 업로드 동의 창을 닫는다.
        /// scene 이름을 사용해 전환 기록과 입력 잠금 상태를 갱신한다.
        /// </summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsUploadScene(scene.name) && _uploadConfirmationOpen)
                SetUploadConfirmationOpen(false);

            Emit(scene.name == "WorkShop2" ? "WorkshopEntered" : "SceneEntered", scene.path);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            {
                if (IsUploadScene(SceneManager.GetActiveScene().name))
                {
                    LogUploadCoordinator coordinator = LogUploadCoordinator.EnsureAvailable();
                    if (coordinator != null && coordinator.IsBusy) return;
                    _panelOpen = false;
                    SetUploadConfirmationOpen(!_uploadConfirmationOpen);
                    Emit("LogUploadPrompt", _uploadConfirmationOpen ? "Opened" : "Closed");
                    return;
                }
                _panelOpen = !_panelOpen;
                Emit("ResearchPanel", _panelOpen ? "Opened" : "Closed");
            }
        }

        void OnGUI()
        {
            if (IsUploadScene(SceneManager.GetActiveScene().name))
            {
                if (_uploadConfirmationOpen) DrawUploadConfirmation();
                return;
            }
            if (!_panelOpen) return;
            GUILayout.BeginArea(new Rect(12, 12, 370, 410), GUI.skin.box);
            GUILayout.Label("테스트 코드: " + TesterId);
            if (GUILayout.Button("테스트 코드 복사")) GUIUtility.systemCopyBuffer = TesterId;
            if (GUILayout.Button("업로드할 로그 폴더 열기"))
            {
                Emit("LogUploadFolderRequested", "OperatorPanel");
                CustomizationLogger.FlushForUpload();
                CustomizationLogger.OpenLogFolder();
            }
            GUILayout.Label("Customization_ CSV 한 개를 확인해 주세요.");
            if (GUILayout.Button("테스트 코드 복사하고 사후 설문 열기"))
            {
                GUIUtility.systemCopyBuffer = TesterId;
                Emit("SurveyOpened", "OperatorPanel");
                CustomizationLogger.FlushForUpload();
                CustomizationLogger.OpenLogFolder();
                Application.OpenURL(SURVEY_URL);
            }
            GUILayout.Label("반복 기회·진행 구분: " + ParticipationContext);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("미확인")) SetParticipationContext("Unknown");
            if (GUILayout.Button("자유 선택 기회")) SetParticipationContext("FreeChoiceOpportunity");
            if (GUILayout.Button("진행자 요청")) SetParticipationContext("Requested");
            GUILayout.EndHorizontal();
            GUILayout.Label("개입 사유");
            GUI.enabled = string.IsNullOrEmpty(InterventionId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("조작 안내")) _interventionReason = "OperationHelp";
            if (GUILayout.Button("문제 해결")) _interventionReason = "Troubleshooting";
            if (GUILayout.Button("반복 요청")) _interventionReason = "RepeatRequest";
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label(_interventionReason);
            if (GUILayout.Button(string.IsNullOrEmpty(InterventionId) ? "개입 시작" : "개입 종료"))
            {
                if (string.IsNullOrEmpty(InterventionId))
                {
                    InterventionId = Guid.NewGuid().ToString("N");
                    Emit("InterventionStart", _interventionReason);
                }
                else
                {
                    Emit("InterventionEnd", _interventionReason);
                    InterventionId = null;
                }
            }
            GUI.enabled = SceneManager.GetActiveScene().name == "WorkShop2" && !IsCustomizing;
            if (GUILayout.Button("다음 참여자 · 로그 구분만 새로 시작")) StartParticipant(null);
            GUI.enabled = true;
            GUILayout.Label("F12: 닫기 · 게임은 일시정지되지 않습니다.");
            GUILayout.EndArea();
        }

        /// <summary>
        /// 로그 전송 동의 화면을 표시하고 예 또는 아니오에 따라 업로드를 시작하거나 취소한다.
        /// 업로드 시작이 거절되면 오류를 표시하고 현재 CSV 기록은 열린 상태로 둔다.
        /// </summary>
        private void DrawUploadConfirmation()
        {
            float width = 390f;
            float height = 190f;
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height), GUI.skin.window);
            GUILayout.Label("로그를 서버로 전송하시겠습니까?");
            GUILayout.Label("예를 선택하면 현재 세션의 Customization_ CSV 한 개만 마감한 뒤 전송합니다.");
            GUILayout.Label("전송이 끝나면 앱이 종료됩니다.");

            LogUploadCoordinator coordinator = LogUploadCoordinator.Instance;
            if (coordinator == null)
            {
                GUILayout.Label("업로드 기능을 준비하지 못했습니다. 로그 폴더를 열어 파일을 보관해 주세요.");
            }
            else if (!string.IsNullOrEmpty(coordinator.LastError))
            {
                GUILayout.Label(coordinator.LastError);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("로그 폴더 열기"))
            {
                Emit("LogUploadFolderRequested", "UploadConfirmation");
                CustomizationLogger.FlushForUpload();
                CustomizationLogger.OpenLogFolder();
            }
            GUI.enabled = coordinator != null;
            if (GUILayout.Button("예, 전송"))
            {
                if (coordinator.BeginUploadAfterConsent()) SetUploadConfirmationOpen(false);
            }
            GUI.enabled = true;
            if (GUILayout.Button("아니오"))
            {
                Emit("LogUploadConsent", "Declined");
                SetUploadConfirmationOpen(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>
        /// sceneName이 Customization CSV 업로드를 제공하는 씬인지 반환한다.
        /// WorkShop2와 W08_Main_Test에서 F12 확인 창을 사용한다.
        /// </summary>
        private static bool IsUploadScene(string sceneName)
        {
            return sceneName == "WorkShop2" || sceneName == "W08_Main_Test";
        }

        /// <summary>
        /// 업로드 동의 창의 열림 상태를 갱신한다.
        /// isOpen 값을 인스턴스 상태와 플레이어 입력 제어용 공개 상태에 저장한다.
        /// </summary>
        private void SetUploadConfirmationOpen(bool isOpen)
        {
            if (isOpen)
            {
                if (_uploadConfirmationCursorDialog == null)
                    _uploadConfirmationCursorDialog = InGameCursor.SuspendForSystemDialog();
            }
            else
            {
                _uploadConfirmationCursorDialog?.Dispose();
                _uploadConfirmationCursorDialog = null;
            }

            _uploadConfirmationOpen = isOpen;
            IsUploadConfirmationOpen = isOpen;
        }

        /// <summary>testerId로 새 참여 구간을 시작한다. 기존 구간을 마감하고 설문 코드와 구성·순환 식별자를 새로 만든다.</summary>
        public static void StartParticipant(string testerId)
        {
            if (SceneManager.GetActiveScene().name != "WorkShop2" || IsCustomizing)
                throw new InvalidOperationException("작업실에서 편집을 종료한 뒤 참여자를 변경해 주세요.");
            if (!string.IsNullOrEmpty(InterventionId)) Emit("InterventionEnd", "ParticipantChanged");
            Emit("TestEnd", "ParticipantChanged");
            ResetIdentity(testerId);
            Emit("TestStart", "ParticipantChanged");
        }

        /// <summary>testerId 또는 임의 익명 코드로 실행 문맥을 초기화한다. 게임 구성과 저장 파일은 변경하지 않는다.</summary>
        private static void ResetIdentity(string testerId)
        {
            TestRunId = Guid.NewGuid().ToString("N");
            TesterId = string.IsNullOrWhiteSpace(testerId) ? TestRunId.Substring(0, 12).ToUpperInvariant() : testerId.Trim();
            CycleId = Guid.NewGuid().ToString("N");
            CycleIndex = 1;
            CustomizationRunId = null;
            ConfigurationId = null;
            ParentConfigurationId = null;
            ConfigurationOrigin = null;
            TournamentRunId = null;
            PreviousTournamentRunId = null;
            TournamentIndex = 0;
            ParticipationContext = "Unknown";
            InterventionId = null;
            IsCustomizing = false;
            _fingerprint = null;
            _nextCyclePending = false;
        }

        /// <summary>진행자가 명시한 context를 이후 기록에 적용한다. 선택 기회와 실제 자발적 행동은 구분해 기록한다.</summary>
        private static void SetParticipationContext(string context)
        {
            ParticipationContext = context;
            Emit("ParticipationContextChanged", "OperatorDeclared");
        }

        /// <summary>전투 이후 첫 제작 또는 재시작에서 새 순환을 시작한다. 라운드 진행과 반복 편집 진입은 순환 수를 늘리지 않는다.</summary>
        private static void PrepareCycle()
        {
            if (!_nextCyclePending) return;
            CycleId = Guid.NewGuid().ToString("N");
            CycleIndex++;
            _nextCyclePending = false;
            Emit("CycleStart", "AfterTournament");
        }

        /// <summary>runId를 실제 편집 회차로 연결한다. 완료된 전투 이후 최초 진입이면 새 순환과 재편집 시작을 기록한다.</summary>
        public static void BeginCustomization(string runId)
        {
            bool reedit = _nextCyclePending;
            CustomizationRunId = runId;
            IsCustomizing = true;
            PrepareCycle();
            if (reedit) Emit("ReeditStarted", "EditorEntered");
        }

        /// <summary>열린 편집 구간을 닫힌 상태로 표시한다. 마지막 runId는 전투 및 설문 연결을 위해 유지한다.</summary>
        public static void EndCustomization() { IsCustomizing = false; }

        /// <summary>state의 동일 여부를 실행 내부에서만 비교한다. 원본 이미지·경로·직렬화값은 기록하지 않고 변경 시 구성 계보를 갱신한다.</summary>
        public static void CaptureConfiguration(KeyboardCustomizationState state, string origin)
        {
            if (state == null) return;
            string fingerprint;
            using (var hash = new HMACSHA256(Encoding.UTF8.GetBytes(TestRunId)))
                fingerprint = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(state))));
            ConfigurationOrigin = origin;
            if (_fingerprint == fingerprint) return;
            ParentConfigurationId = ConfigurationId;
            ConfigurationId = Guid.NewGuid().ToString("N");
            _fingerprint = fingerprint;
            Emit("ConfigurationCaptured", origin);
        }

        /// <summary>runId의 토너먼트에 state와 origin을 연결한다. 실제 시작 구성을 확정한 뒤 전체 순번과 이전 실행 연결을 기록한다.</summary>
        public static void BeginTournament(string runId, KeyboardCustomizationState state, string origin)
        {
            PreviousTournamentRunId = TournamentRunId;
            TournamentRunId = runId;
            TournamentIndex++;
            PrepareCycle();
            CaptureConfiguration(state, origin);
            Emit("TournamentStart", TournamentIndex == 1 ? "FirstTournament" : "RepeatTournament");
        }

        /// <summary>전체 토너먼트의 reason 결과를 기록하고 다음 순환을 예약한다. 라운드 종료에는 호출하지 않는다.</summary>
        public static void FinishTournament(string reason)
        {
            Emit("TournamentFinished", reason);
            _nextCyclePending = true;
        }

        /// <summary>완료되지 않은 토너먼트를 reason으로 마감한다. 다음 실행은 새 순환으로 구분하고 반복 기회를 제시했다고 간주하지 않는다.</summary>
        public static void InterruptTournament(string reason)
        {
            Emit("TournamentInterrupted", reason);
            _nextCyclePending = true;
        }

        /// <summary>
        /// type, reason과 eventId를 연구 세션 관찰자에게 전달한다.
        /// 새 행동 CSV에 행을 쓰지 않으며 TestEnd를 제작 회차 종료에 알리는 데 사용한다.
        /// </summary>
        public static void Emit(string type, string reason)
        {
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive) return;
            Observed?.Invoke(type, reason, Guid.NewGuid().ToString("N"));
        }

        /// <summary>record에 현재 설문 코드와 제작·전투 연결 문맥을 복사한다. 이미 저장된 문맥이 있으면 해당 값을 유지한다.</summary>
        public static void AddContext(PlaytestRecord record)
        {
            SetIfEmpty(record, PlaytestColumn.TesterId, TesterId);
            SetIfEmpty(record, PlaytestColumn.TestRunId, TestRunId);
            SetIfEmpty(record, PlaytestColumn.CycleId, CycleId);
            SetIfEmpty(record, PlaytestColumn.CycleIndex, CycleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetIfEmpty(record, PlaytestColumn.CustomizationRunId, CustomizationRunId);
            SetIfEmpty(record, PlaytestColumn.ConfigurationId, ConfigurationId);
            SetIfEmpty(record, PlaytestColumn.ParentConfigurationId, ParentConfigurationId);
            SetIfEmpty(record, PlaytestColumn.ConfigurationOrigin, ConfigurationOrigin);
            SetIfEmpty(record, PlaytestColumn.LinkedTournamentRunId, TournamentRunId);
            SetIfEmpty(record, PlaytestColumn.PreviousTournamentRunId, PreviousTournamentRunId);
            SetIfEmpty(record, PlaytestColumn.TournamentIndex, TournamentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetIfEmpty(record, PlaytestColumn.ParticipationContext, ParticipationContext);
            SetIfEmpty(record, PlaytestColumn.InterventionId, InterventionId);
        }

        /// <summary>column이 비어 있으면 value를 record에 복사한다. 지연 효과가 보관한 원래 문맥은 덮어쓰지 않는다.</summary>
        private static void SetIfEmpty(PlaytestRecord record, PlaytestColumn column, string value)
        {
            if (string.IsNullOrEmpty(record.Values[(int)column])) record.Set(column, value);
        }
    }
}
