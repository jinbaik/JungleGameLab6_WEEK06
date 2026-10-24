using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Game.Logging
{
    [DisallowMultipleComponent]
    public sealed class LogUploadCoordinator : MonoBehaviour
    {
        private const long MAX_FILE_SIZE = 25L * 1024L * 1024L;
        private const int MAX_UPLOAD_ATTEMPTS = 3;
        private const int REQUEST_TIMEOUT_SECONDS = 60;
        private const int FILE_UPLOAD_TIMEOUT_SECONDS = 180;
        private const string INIT_ENDPOINT = "/api/device/uploads/init";
        private const string MANIFEST_FILE_NAME = "LogUploadManifest.json";

        private static LogUploadCoordinator _instance;

        [Header("Runtime State")]
        private GameConfig _config;
        private UploadManifest _manifest;
        private string _manifestPath;
        private string _apiBaseUrl;
        private bool _isBusy;
        private bool _showStatus;
        private bool _manifestLoadFailed;
        private string _statusMessage;
        private string _lastError;
        private int _currentEntryIndex;
        private int _totalEntries;

        public static LogUploadCoordinator Instance => _instance;
        public bool IsBusy => _isBusy;
        public string LastError => _lastError;

        /// <summary>
        /// 새 앱 실행의 업로드 조정기 참조를 초기화한다.
        /// 도메인 재로드가 꺼진 에디터에서 이전 실행 인스턴스를 재사용하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance()
        {
            _instance = null;
        }

        /// <summary>
        /// 로그 업로드 조정기가 없으면 실행용 오브젝트를 생성한다.
        /// WorkShop0에서는 업로드 화면과 조정기를 생성하지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureCoordinator()
        {
            EnsureAvailable();
        }

        /// <summary>
        /// 현재 씬에서 업로드 조정기가 없으면 새 인스턴스를 생성하여 반환한다.
        /// WorkShop0에서는 생성하지 않고 null을 반환하며 F12 진입 시에도 복구에 사용한다.
        /// </summary>
        public static LogUploadCoordinator EnsureAvailable()
        {
            if (Game.Workshop.CustomizationOnlyWorkshop.IsActive) return null;
            if (_instance == null)
                new GameObject("LogUploadCoordinator").AddComponent<LogUploadCoordinator>();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            _config = Resources.Load<GameConfig>("GameConfig");
            _manifestPath = Path.Combine(Application.persistentDataPath, MANIFEST_FILE_NAME);
            LoadManifest();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnGUI()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (!_showStatus || sceneName != "WorkShop2" && sceneName != "W08_Main_Test") return;
            float height = string.IsNullOrEmpty(_lastError) ? 70f : 105f;
            GUILayout.BeginArea(new Rect(12f, Screen.height - height - 12f, 500f, height), GUI.skin.box);
            GUILayout.Label(_statusMessage);
            if (_isBusy && _totalEntries > 0)
                GUILayout.Label($"파일 {_currentEntryIndex}/{_totalEntries}");
            if (!string.IsNullOrEmpty(_lastError)) GUILayout.Label(_lastError);
            GUILayout.EndArea();
        }

        /// <summary>
        /// 설정과 현재 세션의 Customization CSV 경로를 확인한 뒤 동의된 파일을 마감한다.
        /// 입력 파일을 대기열에 넣고 업로드를 시작하면 true를 반환하며, 사전 확인에 실패하면 로그를 열린 상태로 둔다.
        /// </summary>
        public bool BeginUploadAfterConsent()
        {
            if (_isBusy) return false;
            if (!ValidateConfiguration(out string configurationError))
            {
                _lastError = configurationError;
                _showStatus = true;
                return false;
            }
            if (_manifestLoadFailed || _manifest == null)
            {
                _lastError = "업로드 대기열을 읽지 못했습니다. 로그 파일은 유지됩니다.";
                _showStatus = true;
                return false;
            }

            string[] loggerPaths = { CustomizationLogger.CurrentFilePath };
            if (!TryNormalizeCurrentSessionPaths(loggerPaths, out string[] currentSessionPaths, out string pathError))
            {
                _lastError = pathError;
                _showStatus = true;
                return false;
            }

            _lastError = null;
            _showStatus = true;
            _isBusy = true;
            _statusMessage = "현재 CSV를 마감하고 업로드를 준비합니다.";
            PlaytestResearchSession.Emit("LogUploadConsent", "Accepted");
            PlaytestResearchSession.FinishForUpload();
            CustomizationLogger.FinishForUpload();
            StartCoroutine(UploadApprovedEntries(currentSessionPaths));
            return true;
        }

        /// <summary>
        /// 저장된 업로드 대기열을 읽고 손상 여부를 확인한다.
        /// 업로드 상태 파일을 찾거나 해석할 수 없으면 네트워크 전송을 막는다.
        /// </summary>
        private void LoadManifest()
        {
            _manifest = new UploadManifest();
            if (!File.Exists(_manifestPath)) return;

            try
            {
                _manifest = JsonUtility.FromJson<UploadManifest>(File.ReadAllText(_manifestPath, Encoding.UTF8));
                if (_manifest == null) throw new InvalidDataException("Manifest JSON is empty.");
                if (_manifest.entries == null) _manifest.entries = new List<UploadEntry>();
            }
            catch (Exception exception)
            {
                _manifestLoadFailed = true;
                _lastError = "업로드 대기열을 읽지 못했습니다. 파일을 보존했습니다.";
                Debug.LogWarning($"[LogUpload] 대기열 읽기 실패: {exception.GetType().Name}");
            }
        }

        /// <summary>
        /// 설정 에셋의 HTTPS API 주소와 업로드 토큰을 확인한다.
        /// 유효한 API origin을 _apiBaseUrl에 저장하고 오류 설명을 반환한다.
        /// </summary>
        private bool ValidateConfiguration(out string error)
        {
            error = null;
            if (_config == null)
            {
                error = "Resources/GameConfig 설정 에셋을 찾지 못했습니다.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_config.ApiBaseUrl)
                || !Uri.TryCreate(_config.ApiBaseUrl.Trim(), UriKind.Absolute, out Uri apiUri)
                || apiUri.Scheme != Uri.UriSchemeHttps)
            {
                error = "GameConfig의 API 주소에 유효한 HTTPS origin을 입력해 주세요.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_config.UploadToken))
            {
                error = "GameConfig에 서버 업로드 토큰을 입력해 주세요.";
                return false;
            }

            _apiBaseUrl = apiUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return true;
        }

        /// <summary>
        /// 현재 세션에서 전달된 Customization CSV만 manifest에 승인하고 안정적인 업로드 ID를 먼저 저장한다.
        /// 기존 대기열은 제거하고 파일의 경로, 크기, 수정 시각, SHA-256이 같은 항목만 재사용한다.
        /// </summary>
        private bool ApproveClosedFiles(string[] currentSessionPaths, out string error)
        {
            error = null;
            if (!TryNormalizeCurrentSessionPaths(currentSessionPaths, out string[] normalizedPaths, out error)) return false;

            string folderPath = GetLogFolderPath();
            if (!Directory.Exists(folderPath))
            {
                error = "로그 폴더를 찾지 못했습니다.";
                return false;
            }

            try
            {
                HashSet<string> currentPathSet = new HashSet<string>(normalizedPaths, StringComparer.OrdinalIgnoreCase);
                for (int index = _manifest.entries.Count - 1; index >= 0; index--)
                {
                    UploadEntry existingEntry = _manifest.entries[index];
                    if (existingEntry == null || string.IsNullOrWhiteSpace(existingEntry.path)
                        || !currentPathSet.Contains(existingEntry.path))
                        _manifest.entries.RemoveAt(index);
                }

                foreach (string path in normalizedPaths)
                {
                    string name = Path.GetFileName(path);
                    FileInfo fileInfo = new FileInfo(path);
                    if (fileInfo.Length <= 0)
                    {
                        error = $"CSV 파일이 비어 있습니다: {name}";
                        return false;
                    }
                    if (fileInfo.Length > MAX_FILE_SIZE)
                    {
                        error = $"25 MiB를 초과한 CSV가 있습니다: {name}";
                        return false;
                    }
                    if (name.Length > 160)
                    {
                        error = $"서버 파일명 제한을 초과한 CSV가 있습니다: {name}";
                        return false;
                    }

                    string fullPath = Path.GetFullPath(path);
                    string contentHash = ComputeSha256(fullPath);
                    UploadEntry entry = FindEntry(fullPath, contentHash);
                    if (entry == null)
                    {
                        MarkChangedEntries(fullPath);
                        entry = new UploadEntry
                        {
                            uploadId = Guid.NewGuid().ToString(),
                            name = name,
                            path = fullPath,
                            size = fileInfo.Length,
                            lastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                            sha256 = contentHash,
                            state = "Queued",
                            approved = true
                        };
                        _manifest.entries.Add(entry);
                    }
                    else
                    {
                        entry.name = name;
                        entry.size = fileInfo.Length;
                        entry.lastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;
                        entry.approved = true;
                        if (!IsTerminalState(entry.state)) entry.state = "Queued";
                    }
                }

                if (!TrySaveManifest(out error)) return false;
                return true;
            }
            catch (Exception exception)
            {
                error = "로그 파일을 대기열에 넣지 못했습니다. 원본은 보존했습니다.";
                Debug.LogWarning($"[LogUpload] 대기열 준비 실패: {exception.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// 전달된 파일 하나가 현재 로그 폴더의 Customization CSV인지 확인한다.
        /// 절대 경로 하나를 반환하며 파일이 없거나 종류와 폴더가 맞지 않으면 오류를 반환한다.
        /// </summary>
        private static bool TryNormalizeCurrentSessionPaths(string[] paths, out string[] normalizedPaths, out string error)
        {
            normalizedPaths = null;
            error = null;
            if (paths == null || paths.Length != 1)
            {
                error = "현재 세션의 Customization CSV를 찾지 못했습니다.";
                return false;
            }

            try
            {
                string expectedFolder = Path.GetFullPath(GetLogFolderPath());
                string customizationPath = null;

                foreach (string path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        error = "현재 세션의 Customization CSV를 찾지 못했습니다.";
                        return false;
                    }

                    string fullPath = Path.GetFullPath(path);
                    string name = Path.GetFileName(fullPath);
                    string folder = Path.GetDirectoryName(fullPath);
                    if (!IsSupportedFileName(name)
                        || !string.Equals(folder, expectedFolder, StringComparison.OrdinalIgnoreCase)
                        || !File.Exists(fullPath))
                    {
                        error = "현재 세션 CSV의 경로 또는 파일을 확인하지 못했습니다.";
                        return false;
                    }

                    if (name.StartsWith("Customization_", StringComparison.Ordinal))
                    {
                        if (customizationPath != null)
                        {
                            error = "현재 세션의 Customization CSV가 중복되었습니다.";
                            return false;
                        }
                        customizationPath = fullPath;
                    }
                }

                if (customizationPath == null)
                {
                    error = "현재 세션의 Customization CSV를 찾지 못했습니다.";
                    return false;
                }

                normalizedPaths = new[] { customizationPath };
                return true;
            }
            catch (Exception)
            {
                error = "현재 세션 CSV의 경로를 확인하지 못했습니다.";
                return false;
            }
        }

        /// <summary>
        /// Editor 또는 Player에서 로거가 쓰는 로그 디렉터리를 반환한다.
        /// 반환 경로는 현재 앱의 로그 폴더이며 파일 상태는 변경하지 않는다.
        /// </summary>
        private static string GetLogFolderPath()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
#else
            return Path.Combine(Application.persistentDataPath, "PlaytestLogs");
#endif
        }

        /// <summary>
        /// 서버가 허용한 Customization CSV 접두사와 확장자를 확인한다.
        /// 파일명이 Customization_ CSV일 때 true를 반환한다.
        /// </summary>
        private static bool IsSupportedFileName(string name)
        {
            return name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                && name.StartsWith("Customization_", StringComparison.Ordinal);
        }

        /// <summary>
        /// 전체 파일 내용을 메모리에 복사하지 않고 SHA-256 해시를 계산한다.
        /// 파일 스트림을 독점 읽기 모드로 열어 아직 쓰는 중인 로그를 제외한다.
        /// </summary>
        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(stream);
                return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>
        /// 경로와 내용 해시가 같은 manifest 항목을 찾는다.
        /// 일치하는 기존 항목을 반환하고 없으면 null을 반환한다.
        /// </summary>
        private UploadEntry FindEntry(string path, string contentHash)
        {
            foreach (UploadEntry entry in _manifest.entries)
            {
                if (string.Equals(entry.path, path, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.sha256, contentHash, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        /// <summary>
        /// 승인된 뒤 내용이 바뀐 같은 경로의 미완료 파일을 실패 상태로 기록한다.
        /// 변경 전 파일의 ID를 새 내용에 재사용하지 않도록 막는다.
        /// </summary>
        private void MarkChangedEntries(string path)
        {
            foreach (UploadEntry entry in _manifest.entries)
            {
                if (!entry.approved || IsTerminalState(entry.state)
                    || !string.Equals(entry.path, path, StringComparison.OrdinalIgnoreCase)) continue;
                entry.state = "Failed";
                entry.error = "동의 후 로컬 파일 내용이 변경되었습니다.";
            }
        }

        /// <summary>
        /// 저장 대기열을 임시 파일에 쓴 뒤 manifest 경로에 반영한다.
        /// 저장 성공 여부와 실패 설명을 반환하며 실패하면 네트워크 요청을 시작하지 않는다.
        /// </summary>
        private bool TrySaveManifest(out string error)
        {
            error = null;
            string temporaryPath = _manifestPath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(_manifestPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(_manifest, true), new UTF8Encoding(false));
                if (File.Exists(_manifestPath)) File.Replace(temporaryPath, _manifestPath, null);
                else File.Move(temporaryPath, _manifestPath);
                return true;
            }
            catch (Exception exception)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                error = "업로드 대기열을 저장하지 못했습니다. 네트워크 전송을 시작하지 않았습니다.";
                Debug.LogWarning($"[LogUpload] 대기열 저장 실패: {exception.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// 현재 세션의 Customization CSV 한 개만 승인 대기열에 넣고 전송한다.
        /// currentSessionPaths는 동의 전에 현재 로거에서 얻은 경로이며 파일의 API 흐름을 실행한다.
        /// </summary>
        private IEnumerator UploadApprovedEntries(string[] currentSessionPaths)
        {
            _statusMessage = "현재 세션의 Customization CSV를 확인하고 업로드 대기열을 저장합니다.";
            if (!ApproveClosedFiles(currentSessionPaths, out string approvalError))
            {
                FinishWithError(approvalError);
                yield break;
            }

            List<UploadEntry> entries = GetApprovedPendingEntries();
            _totalEntries = entries.Count;
            _currentEntryIndex = 0;
            if (entries.Count == 0 && !HasApprovedTerminalErrors())
            {
                FinishWithoutUpload("전송할 CSV 로그가 없습니다. 앱은 계속 실행됩니다.");
                yield break;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                _currentEntryIndex = index + 1;
                yield return UploadFileEntry(entries[index]);
            }

            FinishBatch();
        }

        /// <summary>
        /// API가 아직 완료로 표시하지 않은 승인 항목을 순서대로 반환한다.
        /// imported, duplicate, failed 항목은 자동 재요청 대상에서 제외한다.
        /// </summary>
        private List<UploadEntry> GetApprovedPendingEntries()
        {
            List<UploadEntry> entries = new List<UploadEntry>();
            foreach (UploadEntry entry in _manifest.entries)
            {
                if (entry.approved && !IsTerminalState(entry.state)) entries.Add(entry);
            }
            return entries;
        }

        /// <summary>
        /// 서버 init부터 필요 시 Blob PUT와 complete까지 한 파일을 처리한다.
        /// 재시도는 같은 uploadId를 사용하며 파일은 25 MiB 이하로 유지한다.
        /// </summary>
        private IEnumerator UploadFileEntry(UploadEntry entry)
        {
            if (!TryValidateLocalFile(entry, out string localError))
            {
                MarkEntryFailed(entry, localError);
                yield break;
            }

            for (int attempt = 1; attempt <= MAX_UPLOAD_ATTEMPTS; attempt++)
            {
                _statusMessage = $"로그 전송 중: {entry.name}";
                entry.state = "Initializing";
                if (!TrySaveManifest(out string saveError))
                {
                    MarkEntryFailed(entry, saveError);
                    yield break;
                }

                InitRequest initBody = new InitRequest
                {
                    uploadId = entry.uploadId,
                    name = entry.name,
                    size = entry.size
                };

                UnityWebRequest initRequest = null;
                string requestCreationError = null;
                try
                {
                    initRequest = CreateJsonRequest(_apiBaseUrl + INIT_ENDPOINT, JsonUtility.ToJson(initBody));
                }
                catch (Exception exception)
                {
                    requestCreationError = $"초기 요청을 준비하지 못했습니다 ({exception.GetType().Name}).";
                }
                if (initRequest == null)
                {
                    yield return RetryOrFinish(entry, attempt, requestCreationError);
                    if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                    continue;
                }

                RequestResult initResult = null;
                yield return SendRequest(initRequest, REQUEST_TIMEOUT_SECONDS, result => initResult = result);
                InitResponse initResponse = ParseResponse<InitResponse>(initResult.body);
                if (!initResult.success)
                {
                    if (IsPermanentApiFailure(initResult.responseCode))
                    {
                        MarkEntryFailed(entry, GetApiFailureMessage(initResult.responseCode));
                        yield break;
                    }
                    string initError = initResult.responseCode >= 400
                        ? GetApiFailureMessage(initResult.responseCode)
                        : "서버 연결을 완료하지 못했습니다.";
                    yield return RetryOrFinish(entry, attempt, initError);
                    if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                    continue;
                }

                if (initResponse == null || string.IsNullOrEmpty(initResponse.status))
                {
                    yield return RetryOrFinish(entry, attempt, "서버 응답을 해석하지 못했습니다.");
                    if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                    continue;
                }
                if (!string.IsNullOrEmpty(initResponse.fileId)
                    && !string.Equals(initResponse.fileId, entry.uploadId, StringComparison.Ordinal))
                {
                    MarkEntryFailed(entry, "서버가 다른 업로드 ID를 반환했습니다.");
                    yield break;
                }

                if (initResponse.status == "imported" || initResponse.status == "duplicate")
                {
                    MarkEntryComplete(entry, initResponse.status);
                    yield break;
                }
                if (initResponse.status == "failed")
                {
                    MarkEntryFailed(entry, "서버가 이 로그를 실패 상태로 표시했습니다.");
                    yield break;
                }

                if (initResponse.status == "pending_upload")
                {
                    if (string.IsNullOrWhiteSpace(initResponse.uploadUrl))
                    {
                        yield return RetryOrFinish(entry, attempt, "서버가 업로드 주소를 반환하지 않았습니다.");
                        if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                        continue;
                    }

                    entry.state = "Uploading";
                    if (!TrySaveManifest(out saveError))
                    {
                        MarkEntryFailed(entry, saveError);
                        yield break;
                    }

                    UnityWebRequest putRequest = null;
                    requestCreationError = null;
                    try
                    {
                        putRequest = CreateFileUploadRequest(initResponse.uploadUrl, entry.path);
                    }
                    catch (Exception exception)
                    {
                        requestCreationError = $"파일 전송을 준비하지 못했습니다 ({exception.GetType().Name}).";
                    }
                    if (putRequest == null)
                    {
                        yield return RetryOrFinish(entry, attempt, requestCreationError);
                        if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                        continue;
                    }

                    RequestResult putResult = null;
                    yield return SendRequest(putRequest, FILE_UPLOAD_TIMEOUT_SECONDS, result => putResult = result);
                    if (!putResult.success)
                    {
                        yield return RetryOrFinish(entry, attempt, "CSV 파일 전송을 완료하지 못했습니다.");
                        if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                        continue;
                    }
                }
                else if (initResponse.status != "uploaded" && initResponse.status != "importing")
                {
                    yield return RetryOrFinish(entry, attempt, "서버가 예상하지 못한 업로드 상태를 반환했습니다.");
                    if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
                    continue;
                }

                ImportResult importResult = null;
                yield return CompleteImport(entry, result => importResult = result);
                if (importResult.completed)
                {
                    MarkEntryComplete(entry, importResult.status);
                    yield break;
                }
                if (importResult.terminal)
                {
                    MarkEntryFailed(entry, importResult.message);
                    yield break;
                }

                yield return RetryOrFinish(entry, attempt, importResult.message);
                if (attempt == MAX_UPLOAD_ATTEMPTS) yield break;
            }
        }

        /// <summary>
        /// 가져오기 완료 상태를 요청하고 importing 상태를 제한 횟수만 조회한다.
        /// 완료, 서버 실패, 재시도 또는 init 재호출 필요 상태를 반환한다.
        /// </summary>
        private IEnumerator CompleteImport(UploadEntry entry, Action<ImportResult> completed)
        {
            const int MAX_IMPORT_POLLS = 4;
            string endpoint = _apiBaseUrl + "/api/device/uploads/" + Uri.EscapeDataString(entry.uploadId) + "/complete";

            for (int poll = 1; poll <= MAX_IMPORT_POLLS; poll++)
            {
                entry.state = "Completing";
                if (!TrySaveManifest(out string saveError))
                {
                    completed(new ImportResult { terminal = true, message = saveError });
                    yield break;
                }

                UnityWebRequest request = null;
                string requestCreationError = null;
                try
                {
                    request = CreateJsonRequest(endpoint, "{}");
                }
                catch (Exception exception)
                {
                    requestCreationError = $"완료 요청을 준비하지 못했습니다 ({exception.GetType().Name}).";
                }
                if (request == null)
                {
                    completed(new ImportResult { message = requestCreationError });
                    yield break;
                }

                RequestResult response = null;
                yield return SendRequest(request, REQUEST_TIMEOUT_SECONDS, result => response = result);
                CompleteResponse completeResponse = ParseResponse<CompleteResponse>(response.body);
                if (response.responseCode == 409 && completeResponse != null)
                {
                    if (completeResponse.status == "pending_upload")
                    {
                        completed(new ImportResult { message = "서버에서 파일을 확인하지 못해 업로드 상태를 다시 확인합니다." });
                        yield break;
                    }
                    if (completeResponse.status == "failed")
                    {
                        completed(new ImportResult { terminal = true, message = "서버가 파일 확인에 실패했습니다." });
                        yield break;
                    }
                }
                if (!response.success)
                {
                    if (IsPermanentApiFailure(response.responseCode))
                    {
                        completed(new ImportResult { terminal = true, message = GetApiFailureMessage(response.responseCode) });
                        yield break;
                    }
                    string completeError = response.responseCode >= 400
                        ? GetApiFailureMessage(response.responseCode)
                        : "가져오기 완료 응답을 받지 못했습니다.";
                    completed(new ImportResult { message = completeError });
                    yield break;
                }

                if (completeResponse == null || string.IsNullOrEmpty(completeResponse.status))
                {
                    completed(new ImportResult { message = "가져오기 응답을 해석하지 못했습니다." });
                    yield break;
                }
                if (completeResponse.status == "imported" || completeResponse.status == "duplicate")
                {
                    completed(new ImportResult { completed = true, status = completeResponse.status });
                    yield break;
                }
                if (completeResponse.status == "failed")
                {
                    completed(new ImportResult { terminal = true, message = "서버가 CSV 가져오기에 실패했습니다." });
                    yield break;
                }
                if (completeResponse.status == "pending_upload")
                {
                    completed(new ImportResult { message = "서버에서 파일을 확인하지 못해 업로드 상태를 다시 확인합니다." });
                    yield break;
                }
                if (completeResponse.status != "importing")
                {
                    completed(new ImportResult { message = "서버가 예상하지 못한 가져오기 상태를 반환했습니다." });
                    yield break;
                }

                entry.state = "Importing";
                if (!TrySaveManifest(out saveError))
                {
                    completed(new ImportResult { terminal = true, message = saveError });
                    yield break;
                }
                if (poll < MAX_IMPORT_POLLS) yield return DelaySeconds(2f);
            }

            completed(new ImportResult { message = "서버의 CSV 가져오기가 아직 끝나지 않았습니다." });
        }

        /// <summary>
        /// JSON API 요청을 만들고 bearer 토큰과 application/json 헤더를 설정한다.
        /// 요청을 반환하며 호출자가 응답 이후 dispose를 담당한다.
        /// </summary>
        private UnityWebRequest CreateJsonRequest(string url, string json)
        {
            UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", "Bearer " + _config.UploadToken);
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        /// <summary>
        /// 서명된 업로드 주소에 CSV 파일 경로를 직접 연결한다.
        /// 요청에는 bearer 토큰을 넣지 않고 text/csv 파일만 전송한다.
        /// </summary>
        private static UnityWebRequest CreateFileUploadRequest(string uploadUrl, string filePath)
        {
            UnityWebRequest request = new UnityWebRequest(uploadUrl, UnityWebRequest.kHttpVerbPUT);
            request.uploadHandler = new UploadHandlerFile(filePath);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "text/csv");
            return request;
        }

        /// <summary>
        /// 요청을 비동기로 전송하고 응답 코드, 본문, 성공 여부를 콜백에 전달한다.
        /// 완료 시 UnityWebRequest를 dispose해 업로드 파일 핸들을 해제한다.
        /// </summary>
        private IEnumerator SendRequest(UnityWebRequest request, int timeoutSeconds, Action<RequestResult> completed)
        {
            request.timeout = timeoutSeconds;
            using (request)
            {
                yield return request.SendWebRequest();
                completed(new RequestResult
                {
                    success = request.result == UnityWebRequest.Result.Success,
                    responseCode = request.responseCode,
                    body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty
                });
            }
        }

        /// <summary>
        /// JSON 응답을 요청한 DTO로 읽는다.
        /// 빈 값 또는 형식 오류에서는 null을 반환한다.
        /// </summary>
        private static T ParseResponse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>
        /// 로컬 파일의 이름, 크기, 내용 해시와 파일 닫힘 상태를 확인한다.
        /// 업로드 전 승인된 파일과 다르면 실패 사유를 반환한다.
        /// </summary>
        private bool TryValidateLocalFile(UploadEntry entry, out string error)
        {
            error = null;
            if (!File.Exists(entry.path))
            {
                error = "승인된 로컬 CSV를 찾지 못했습니다.";
                return false;
            }
            if (!IsSupportedFileName(entry.name))
            {
                error = "서버가 허용하지 않는 파일 이름입니다.";
                return false;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(entry.path);
                if (fileInfo.Length <= 0 || fileInfo.Length > MAX_FILE_SIZE || fileInfo.Length != entry.size)
                {
                    error = "CSV 크기가 승인 당시와 다르거나 25 MiB 제한을 넘었습니다.";
                    return false;
                }
                if (!string.Equals(ComputeSha256(entry.path), entry.sha256, StringComparison.Ordinal))
                {
                    error = "동의 후 로컬 파일 내용이 변경되었습니다.";
                    return false;
                }
                return true;
            }
            catch (IOException)
            {
                error = "CSV 파일이 열려 있거나 읽을 수 없습니다.";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                error = "CSV 파일을 읽을 권한이 없습니다.";
                return false;
            }
        }

        /// <summary>
        /// 응답이 인증, 마감, 입력 오류처럼 재시도할 수 없는 HTTP 오류인지 확인한다.
        /// 해당 상태 코드이면 true를 반환한다.
        /// </summary>
        private static bool IsPermanentApiFailure(long responseCode)
        {
            if (responseCode == 401 || responseCode == 403 || responseCode == 404) return false;
            return responseCode == 400 || responseCode == 409 || responseCode == 413
                || responseCode >= 400 && responseCode < 500 && responseCode != 408 && responseCode != 429;
        }

        /// <summary>
        /// HTTP 상태 코드에 맞는 짧은 운영 오류 설명을 반환한다.
        /// 응답 본문이나 업로드 주소, 토큰은 사용자 메시지에 포함하지 않는다.
        /// </summary>
        private static string GetApiFailureMessage(long responseCode)
        {
            if (responseCode == 401) return "서버 인증에 실패했습니다. GameConfig의 토큰을 확인해 주세요.";
            if (responseCode == 403) return "서버가 업로드를 거부했거나 업로드 기간이 종료되었습니다.";
            if (responseCode == 413) return "서버 파일 크기 제한을 넘었습니다.";
            if (responseCode == 409) return "서버의 업로드 ID와 파일 정보가 일치하지 않습니다.";
            if (responseCode == 404) return "서버에서 업로드 항목을 찾지 못했습니다.";
            if (responseCode == 429) return "서버 요청 제한으로 대기 중입니다.";
            return $"서버가 요청을 거부했습니다 (HTTP {responseCode}).";
        }

        /// <summary>
        /// 현재 시도가 끝나면 지연 재시도하거나 항목을 재시도 가능 상태로 남긴다.
        /// 상태와 오류 설명을 manifest에 저장하고 최종 시도 여부를 표시한다.
        /// </summary>
        private IEnumerator RetryOrFinish(UploadEntry entry, int attempt, string message)
        {
            entry.error = message;
            if (attempt >= MAX_UPLOAD_ATTEMPTS)
            {
                entry.state = "Retryable";
                TrySaveManifest(out _);
                _lastError = message;
                yield break;
            }

            entry.state = "Retrying";
            TrySaveManifest(out _);
            yield return DelaySeconds(GetRetryDelay(entry, attempt));
        }

        /// <summary>
        /// 시도 횟수에 따른 지수 지연에 uploadId 기반 jitter를 더한다.
        /// 최대 대기 시간을 30초로 제한한 초 단위 값을 반환한다.
        /// </summary>
        private static float GetRetryDelay(UploadEntry entry, int attempt)
        {
            uint hash = unchecked((uint)(entry.uploadId ?? string.Empty).GetHashCode());
            float jitterSeconds = hash % 1000u / 1000f;
            return Mathf.Min(30f, Mathf.Pow(2f, attempt) + jitterSeconds);
        }

        /// <summary>
        /// 코루틴 대기 시간을 Time.deltaTime으로 누적해 경과시킨다.
        /// 지정된 초가 지나면 코루틴을 다음 단계로 진행한다.
        /// </summary>
        private IEnumerator DelaySeconds(float seconds)
        {
            float remaining = seconds;
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// 가져오기가 끝난 항목을 imported 또는 duplicate 상태로 저장한다.
        /// 원본 CSV는 삭제하지 않고 manifest의 uploadId를 유지한다.
        /// </summary>
        private void MarkEntryComplete(UploadEntry entry, string status)
        {
            entry.state = status == "duplicate" ? "Duplicate" : "Imported";
            entry.error = null;
            TrySaveManifest(out string saveError);
            if (!string.IsNullOrEmpty(saveError)) _lastError = saveError;
        }

        /// <summary>
        /// 더 진행할 수 없는 항목을 Failed 상태로 저장한다.
        /// 오류 메시지는 manifest와 업로드 상태 화면에 남긴다.
        /// </summary>
        private void MarkEntryFailed(UploadEntry entry, string message)
        {
            entry.state = "Failed";
            entry.error = message;
            TrySaveManifest(out string saveError);
            _lastError = string.IsNullOrEmpty(saveError) ? message : saveError;
        }

        /// <summary>
        /// 모든 승인 항목의 상태를 확인하고 성공 종료 또는 재시도 안내를 표시한다.
        /// 실패 항목이 있으면 앱을 종료하지 않고 manifest와 CSV를 보존한다.
        /// </summary>
        private void FinishBatch()
        {
            _isBusy = false;
            int failedCount = 0;
            int pendingCount = 0;
            foreach (UploadEntry entry in _manifest.entries)
            {
                if (!entry.approved) continue;
                if (entry.state == "Failed") failedCount++;
                else if (!IsCompletedState(entry.state)) pendingCount++;
            }

            if (failedCount > 0 || pendingCount > 0)
            {
                _statusMessage = "일부 로그를 전송하지 못했습니다. 원본 파일과 대기열은 보존했습니다.";
                if (string.IsNullOrEmpty(_lastError))
                    _lastError = "F12를 눌러 다시 전송하거나 오류 상태를 확인해 주세요.";
                return;
            }

            _lastError = null;
            _statusMessage = "모든 동의된 로그를 서버에서 처리했습니다.";
            if (!Application.isEditor) Application.Quit();
        }

        /// <summary>
        /// 승인 가능한 CSV가 없을 때 앱을 유지하고 전송 결과를 표시한다.
        /// 닫힌 파일과 현재 manifest는 수정하거나 삭제하지 않는다.
        /// </summary>
        private void FinishWithoutUpload(string message)
        {
            _isBusy = false;
            _lastError = null;
            _statusMessage = message;
        }

        /// <summary>
        /// 대기열 저장 또는 파일 준비가 실패했을 때 업로드를 중단한다.
        /// 오류와 원본 파일 경로를 보존하고 앱을 종료하지 않는다.
        /// </summary>
        private void FinishWithError(string message)
        {
            _isBusy = false;
            _lastError = message;
            _statusMessage = "로그 업로드를 시작하지 못했습니다.";
        }

        /// <summary>
        /// 승인된 manifest 항목에 서버 또는 로컬 실패가 남아 있는지 확인한다.
        /// 실패 항목이 하나 이상이면 true를 반환한다.
        /// </summary>
        private bool HasApprovedTerminalErrors()
        {
            foreach (UploadEntry entry in _manifest.entries)
            {
                if (entry.approved && entry.state == "Failed") return true;
            }
            return false;
        }

        /// <summary>
        /// 상태가 이미 업로드 완료 또는 실패로 끝난 값인지 판별한다.
        /// terminal 상태인 Imported, Duplicate, Failed에서 true를 반환한다.
        /// </summary>
        private static bool IsTerminalState(string state)
        {
            return state == "Imported" || state == "Duplicate" || state == "Failed";
        }

        /// <summary>
        /// 서버 가져오기가 완료된 상태인지 판별한다.
        /// Imported 또는 Duplicate이면 true를 반환한다.
        /// </summary>
        private static bool IsCompletedState(string state)
        {
            return state == "Imported" || state == "Duplicate";
        }

        [Serializable]
        private sealed class UploadManifest
        {
            public List<UploadEntry> entries = new List<UploadEntry>();
        }

        [Serializable]
        private sealed class UploadEntry
        {
            public string uploadId;
            public string name;
            public string path;
            public long size;
            public long lastWriteUtcTicks;
            public string sha256;
            public string state;
            public string error;
            public bool approved;
        }

        [Serializable]
        private sealed class InitRequest
        {
            public string uploadId;
            public string name;
            public long size;
        }

        [Serializable]
        private sealed class InitResponse
        {
            public string fileId = string.Empty;
            public string status = string.Empty;
            public string uploadUrl = string.Empty;
        }

        [Serializable]
        private sealed class CompleteResponse
        {
            public string status = string.Empty;
        }

        private sealed class RequestResult
        {
            public bool success;
            public long responseCode;
            public string body;
        }

        private sealed class ImportResult
        {
            public bool completed;
            public bool terminal;
            public string status;
            public string message;
        }
    }
}
