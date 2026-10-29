using System;
using System.Collections;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEngine.UI;

using Game.Tournament;
using Game.UI;
using KeyboardModeling;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public static event Action<GameManager> PlaytestRoundStarted;
    public static event Action<GameManager, string, bool> PlaytestRoundEnded;
    public static event Action PlaytestLogFolderRequested;
    public static event Action<GameManager, GameObject, bool> PlaytestKeyboardReady;

    [Header("Spawner References")]
    [SerializeField] private KeyboardSpawner _keyboardSpawner;
    [SerializeField] private Transform _keyboardSpawnPoint;

    [Header("Enemy Spawner References")]
    [SerializeField] private KeyboardSpawner _enemyKeyboardSpawner;
    [SerializeField] private Transform _enemyKeyboardSpawnPoint;
    [SerializeField] private EnemyTypingController _enemyTypingController;

    [Header("Game Settings")]
    [SerializeField] private bool _spawnOnStart = true;
    [SerializeField] private bool _animateFirstSpawn = true;
    [SerializeField, Min(0f)] private float _respawnDelay = 0.3f;

    [Header("Battle Manual Input")]
    [SerializeField, Min(1)] private int _maximumManualKeys = 8;

    [Header("Round Timer Settings")]
    [SerializeField] private bool _timerEnabled = true;
    [SerializeField, Min(1f)] private float _roundTimeLimit = 60f;

    [Header("Tournament")]
    [SerializeField] private TournamentController _tournamentController;
    private bool _battlePreparing;
    private bool _isSuddenDeath;
    private bool _playerReady;
    private bool _enemyReady;
    private int _playerClearFrame = -1;
    private int _enemyClearFrame = -1;

    [Header("Result UI")]
    [SerializeField] private GameObject _resultPanel;
    [SerializeField] private Text _resultText;
    [SerializeField] private Text _resultTitleText;
    [SerializeField] private Text _resultScoreText;
    [SerializeField] private PlaytestLogFolderButton _resultLogFolderButton;

    [Header("콤보 HUD")]
    [SerializeField] private KeyboardScoreCombo _comboHUD;
    [SerializeField] private KeyboardScoreCombo _enemyScoreCombo;
    [Header("시간 UI")]
    [SerializeField] private Text _timeTexts;
    private Color _defaultTimeTextColor = Color.white;

    [Header("Runtime State")]
    private GameObject _activeKeyboard;
    private KeyboardDestruction _activeDestruction;
    private Coroutine _respawnRoutine;
    private float _remainingTime;
    private float _elapsedRoundTime;
    private bool _isTimerRunning;
    private bool _playtestRoundActive;

    [Header("Enemy Runtime State")]
    private GameObject _activeEnemyKeyboard;
    private KeyboardDestruction _activeEnemyDestruction;
    private Coroutine _enemyRespawnRoutine;

    public GameObject ActiveKeyboard => _activeKeyboard;
    public GameObject ActiveEnemyKeyboard => _activeEnemyKeyboard;
    public float RemainingTime => _remainingTime;
    public float ElapsedRoundTime => _elapsedRoundTime;
    public float RoundTimeLimit => _roundTimeLimit;
    public bool IsTimerRunning => _isTimerRunning;
    public TournamentController PlaytestTournament => _tournamentController;
    public KeyboardScoreCombo PlaytestPlayerScore => _comboHUD;
    public KeyboardScoreCombo PlaytestEnemyScore => _enemyScoreCombo;
    public string PlaytestStageName => _tournamentController == null ? "TimedRound"
        : $"Tournament/Round{_tournamentController.Round}/{(_isSuddenDeath ? "SuddenDeath" : "TimedRound")}";

    public event Action<float> TimerUpdated;
    public event Action RoundTimeOut;
    public event Action<float> RoundCleared;
    public event Action<GameObject, bool> TournamentKeyboardCreated;
    public event Action<int> TournamentBattleFinished;

    /// <summary>
    /// 새 실행의 관찰용 라운드와 로그 폴더 이벤트 구독을 초기화한다.
    /// 게임 상태는 변경하지 않고 이전 실행의 로깅 구독만 제거한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPlaytestObservers()
    {
        PlaytestRoundStarted = null;
        PlaytestRoundEnded = null;
        PlaytestLogFolderRequested = null;
        PlaytestKeyboardReady = null;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // 플레이어 콤보 HUD 컴포넌트를 탐색하여 연결한다.
        if (_comboHUD == null)
        {
            Game.UI.KeyboardScoreComboHud hud = FindFirstObjectByType<Game.UI.KeyboardScoreComboHud>();
            if (hud != null)
            {
                _comboHUD = hud.GetComponent<KeyboardScoreCombo>();
            }
            else
            {
                _comboHUD = FindFirstObjectByType<KeyboardScoreCombo>();
            }
        }

        // 로봇의 점수 및 콤보를 추적하는 컴포넌트를 탐색하여 연결한다.
        if (_enemyScoreCombo == null)
        {
            EnemyScoreComboBinding enemyBinding = FindFirstObjectByType<EnemyScoreComboBinding>();
            if (enemyBinding != null)
            {
                _enemyScoreCombo = enemyBinding.GetComponent<KeyboardScoreCombo>();
            }
        }

        if (_enemyTypingController == null)
        {
            _enemyTypingController = FindFirstObjectByType<EnemyTypingController>();
        }

        if (_enemyKeyboardSpawnPoint == null)
        {
            GameObject placementObj = GameObject.Find("KeyboardPlacement");
            if (placementObj != null)
            {
                _enemyKeyboardSpawnPoint = placementObj.transform;
            }
            else if (_enemyTypingController != null)
            {
                _enemyKeyboardSpawnPoint = _enemyTypingController.transform;
            }
        }

        // 로봇 전용 스포너가 할당되지 않은 경우 플레이어 스포너 설정을 복제하여 생성한다.
        if (_enemyKeyboardSpawner == null && _keyboardSpawner != null)
        {
            GameObject enemySpawnerObj = Instantiate(_keyboardSpawner.gameObject, transform);
            enemySpawnerObj.name = "EnemyKeyboardSpawner";
            _enemyKeyboardSpawner = enemySpawnerObj.GetComponent<KeyboardSpawner>();
        }

        // 로봇의 기존 자체 공급 및 라운드 제어 컴포넌트를 비활성화하여 충돌을 방지한다.
        EnemyKeyboardProvider enemyProvider = FindFirstObjectByType<EnemyKeyboardProvider>();
        if (enemyProvider != null)
        {
            enemyProvider.enabled = false;
        }

        EnemyRoundController enemyRound = FindFirstObjectByType<EnemyRoundController>();
        if (enemyRound != null)
        {
            enemyRound.enabled = false;
        }

        EnemyScoreComboBinding activeBinding = FindFirstObjectByType<EnemyScoreComboBinding>();
        if (activeBinding != null)
        {
            activeBinding.enabled = false;
        }

        if (_enemyScoreCombo != null)
        {
            _enemyScoreCombo.SetTrackingEnabled(true);
        }

        if (_resultPanel != null)
        {
            _resultPanel.SetActive(false);
        }

        _defaultTimeTextColor = _timeTexts.color;
        UpdateTimerText(_roundTimeLimit);
    }

    void OnEnable()
    {
        if (_resultLogFolderButton != null) _resultLogFolderButton.Clicked += RequestPlaytestLogFolder;
    }

    void OnDisable()
    {
        if (_resultLogFolderButton != null) _resultLogFolderButton.Clicked -= RequestPlaytestLogFolder;
    }

    void Start()
    {
        if (_keyboardSpawner != null)
        {
            _keyboardSpawner.KeyboardSpawned += HandleKeyboardSpawned;
            _keyboardSpawner.KeyboardCreated += HandlePlayerKeyboardCreated;
        }

        if (_enemyKeyboardSpawner != null)
        {
            _enemyKeyboardSpawner.KeyboardSpawned += HandleEnemyKeyboardSpawned;
            _enemyKeyboardSpawner.KeyboardCreated += HandleEnemyKeyboardCreated;
        }

        if (_tournamentController != null)
        {
            _tournamentController.Initialize(this, _enemyTypingController);
            _tournamentController.BeginTournament();
        }
        else if (_spawnOnStart)
        {
            SpawnKeyboard(_animateFirstSpawn);
            SpawnEnemyKeyboard(_animateFirstSpawn);
        }
    }

    void Update()
    {
        if (Game.Tournament.TournamentController.IsReturnPaused) return;
        if (!_timerEnabled || !_isTimerRunning)
        {
            return;
        }

        // 게임 플레이 전체 경과 시간과 남은 제한 시간(카운트다운)을 갱신한다.
        _elapsedRoundTime += Time.deltaTime;
        if (_isSuddenDeath)
        {
            _timeTexts.text = "연장전";
            _enemyTypingController.Tick(Time.deltaTime);
            return;
        }
        _remainingTime = Mathf.Max(0f, _remainingTime - Time.deltaTime);
        TimerUpdated?.Invoke(_remainingTime);
        UpdateTimerText(_remainingTime);

        // 타이머 가동 중일 때 로봇의 타건 처리를 갱신한다.
        if (_enemyTypingController != null)
        {
            _enemyTypingController.Tick(Time.deltaTime);
        }

        if (_remainingTime <= 0f)
        {
            HandleTimeOut();
        }
    }

    void LateUpdate()
    {
        if (Game.Tournament.TournamentController.IsReturnPaused) return;
        if (!_isSuddenDeath || !_isTimerRunning || (_playerClearFrame < 0 && _enemyClearFrame < 0)) return;
        int result = _playerClearFrame < 0 ? -1 : _enemyClearFrame < 0 ? 1
            : _playerClearFrame == _enemyClearFrame ? 0 : _playerClearFrame < _enemyClearFrame ? 1 : -1;
        FreezeTournamentBattle("SuddenDeath");
        TournamentBattleFinished?.Invoke(result);
    }

    void OnDestroy()
    {
        NotifyPlaytestRoundEnded("SceneExit", false);
        if (_keyboardSpawner != null)
        {
            _keyboardSpawner.KeyboardSpawned -= HandleKeyboardSpawned;
            _keyboardSpawner.KeyboardCreated -= HandlePlayerKeyboardCreated;
        }

        if (_enemyKeyboardSpawner != null)
        {
            _enemyKeyboardSpawner.KeyboardSpawned -= HandleEnemyKeyboardSpawned;
            _enemyKeyboardSpawner.KeyboardCreated -= HandleEnemyKeyboardCreated;
        }

        UnsubscribeKeyboardDestruction();
        UnsubscribeEnemyKeyboardDestruction();
    }

    /// <summary>
    /// 생성 직후 keyboard에 플레이어 수동 입력 제한을 설정하고 토너먼트 구성 적용을 알린다.
    /// _maximumManualKeys를 사용하며 입력 활성화와 등장 완료 전에 전투 키보드 상태를 확정한다.
    /// </summary>
    private void HandlePlayerKeyboardCreated(GameObject keyboard)
    {
        keyboard.GetComponent<KeyboardInputController>().ConfigureBattleManualInput(_maximumManualKeys);
        if (_tournamentController != null) TournamentKeyboardCreated?.Invoke(keyboard, false);
    }

    /// <summary>
    /// 생성 직후 keyboard에 로봇 토너먼트 구성을 적용하도록 알린다.
    /// 로봇 타건과 등장 완료 이벤트 전에 전투 키보드 상태를 확정한다.
    /// </summary>
    private void HandleEnemyKeyboardCreated(GameObject keyboard)
    {
        if (_tournamentController != null) TournamentKeyboardCreated?.Invoke(keyboard, true);
    }

    /// <summary>
    /// KeyboardSpawner에게 키보드 생성을 요청하고 완파 이벤트와 콤보 상태를 연결한다.
    /// withAnimation 애니메이션 적용 여부를 사용하며 생성된 키보드 게임오브젝트를 반환한다.
    /// </summary>
    public GameObject SpawnKeyboard(bool withAnimation = true)
    {
        UnsubscribeKeyboardDestruction();

        // 등록된 스폰 포인트를 기준으로 키보드 생성을 요청한다.
        _activeKeyboard = _keyboardSpawner.SpawnKeyboard(_keyboardSpawnPoint, withAnimation);

        if (_activeKeyboard != null)
        {
            if (AudioManager.instance != null)
                AudioManager.instance.SelectKeyboardEffects(_activeKeyboard.GetComponent<KeyboardCustomization>());
            _activeDestruction = _activeKeyboard.GetComponent<KeyboardDestruction>();
            if (_activeDestruction != null)
            {
                _activeDestruction.Breaking += HandleKeyboardBreaking;
                _activeDestruction.Finished += HandleKeyboardFinished;
            }

            // 생성된 키보드의 입력을 콤보 HUD에 연결하여 콤보 측정이 이어지도록 한다.
            if (_comboHUD != null)
            {
                KeyboardInputController inputController = _activeKeyboard.GetComponent<KeyboardInputController>();
                _comboHUD.Keyboard = inputController;
            }
        }

        return _activeKeyboard;
    }

    /// <summary>
    /// 게임 전체 제한 시간(카운트다운)과 경과 시간을 초기화하고 타이머 가동을 시작한다.
    /// _roundTimeLimit을 _remainingTime에 대입하고 _elapsedRoundTime을 0으로 초기화한 뒤 타이머를 활성화한다.
    /// </summary>
    public void StartRoundTimer()
    {
        if (!_timerEnabled)
        {
            return;
        }

        NotifyPlaytestRoundEnded("Restart", false);
        _remainingTime = _roundTimeLimit;
        _elapsedRoundTime = 0f;
        _isTimerRunning = true;

        if (_resultPanel != null)
        {
            _resultPanel.SetActive(false);
        }

        UpdateTimerText(_remainingTime);

        // 게임 제한 시간과 경과 시간을 초기화하고 타이머를 시작한다.
        Debug.Log($"[GameManager] Game Timer Started. Limit: {_roundTimeLimit:F1}s");
        TimerUpdated?.Invoke(_remainingTime);
        _playtestRoundActive = true;
        PlaytestRoundStarted?.Invoke(this);
    }

    /// <summary>
    /// 진행 중인 게임 타이머의 제한 시간(카운트다운) 및 경과 시간 측정을 정지한다.
    /// _isTimerRunning을 false로 변경하여 시간 갱신을 중단한다.
    /// </summary>
    public void StopRoundTimer()
    {
        _isTimerRunning = false;
        NotifyPlaytestRoundEnded("Stopped", false);
    }

    /// <summary>
    /// duration과 suddenDeath로 새 토너먼트 대전을 준비하고 이전 키보드 및 점수 상태를 정리한다.
    /// 양쪽 등장 완료 후 동시에 시작하며 연장전에서는 점수를 보존하고 콤보와 효과를 초기화한다.
    /// </summary>
    public void BeginTournamentBattle(float duration, bool suddenDeath)
    {
        _isTimerRunning = false;
        _battlePreparing = true;
        _isSuddenDeath = suddenDeath;
        _playerReady = false;
        _enemyReady = false;
        _playerClearFrame = -1;
        _enemyClearFrame = -1;
        _roundTimeLimit = duration;
        _remainingTime = duration;
        _elapsedRoundTime = 0f;
        UnsubscribeKeyboardDestruction();
        UnsubscribeEnemyKeyboardDestruction();
        _keyboardSpawner.CancelSpawn();
        _enemyKeyboardSpawner.CancelSpawn();
        _comboHUD.Keyboard = null;
        _enemyScoreCombo.Keyboard = null;
        if (_activeKeyboard != null) Destroy(_activeKeyboard);
        if (_activeEnemyKeyboard != null) Destroy(_activeEnemyKeyboard);
        if (suddenDeath)
        {
            _comboHUD.ResetBattleEffects();
            _enemyScoreCombo.ResetBattleEffects();
        }
        else
        {
            _comboHUD.ResetState();
            _enemyScoreCombo.ResetState();
        }
        _comboHUD.SetTrackingEnabled(false);
        _enemyScoreCombo.SetTrackingEnabled(false);
        _resultPanel.SetActive(false);
        SpawnKeyboard(true);
        SpawnEnemyKeyboard(true);
        UpdateTimerText(duration);
    }

    /// <summary>양쪽 준비 상태로 동시 시작을 판정하고 입력, 로봇 및 관찰용 대전 기록을 시작한다.</summary>
    private void TryStartTournamentBattle()
    {
        if (!_battlePreparing || !_playerReady || !_enemyReady) return;
        _battlePreparing = false;
        _comboHUD.SetTrackingEnabled(true);
        _enemyScoreCombo.SetTrackingEnabled(true);
        _comboHUD.SetTimerPaused(false);
        _enemyScoreCombo.SetTimerPaused(false);
        StartRoundTimer();
        _activeKeyboard.GetComponent<KeyboardInputController>().SetInputEnabled(true);
        _enemyTypingController.StartAttacking();
        PlaytestKeyboardReady?.Invoke(this, _activeKeyboard, false);
        PlaytestKeyboardReady?.Invoke(this, _activeEnemyKeyboard, true);
    }

    /// <summary>reason으로 대전을 마감하고 입력, 생성, 점수 수집 및 지연된 교체를 중단한다.</summary>
    private void FreezeTournamentBattle(string reason)
    {
        _isTimerRunning = false;
        _battlePreparing = false;
        _enemyTypingController.StopAttacking();
        _keyboardSpawner.CancelSpawn();
        _enemyKeyboardSpawner.CancelSpawn();
        if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
        if (_enemyRespawnRoutine != null) StopCoroutine(_enemyRespawnRoutine);
        _respawnRoutine = null;
        _enemyRespawnRoutine = null;
        if (_activeKeyboard != null) _activeKeyboard.GetComponent<KeyboardInputController>().SetInputEnabled(false);
        _comboHUD.SetTimerPaused(true);
        _enemyScoreCombo.SetTimerPaused(true);
        _comboHUD.SetTrackingEnabled(false);
        _enemyScoreCombo.SetTrackingEnabled(false);
        NotifyPlaytestRoundEnded(reason, true);
    }

    /// <summary>복귀 전에 타이머·AI·점수 수집과 모든 교체 코루틴을 종료한다. 활성 전투 키보드를 비활성화해 예약 효과와 늦은 타격 콜백을 취소한다.</summary>
    public void StopTournamentForReturn()
    {
        FreezeTournamentBattle("WorkshopReturn");
        StopAllCoroutines();
        UnsubscribeKeyboardDestruction();
        UnsubscribeEnemyKeyboardDestruction();
        if (_activeKeyboard != null) _activeKeyboard.SetActive(false);
        if (_activeEnemyKeyboard != null) _activeEnemyKeyboard.SetActive(false);
        enabled = false;
    }

    /// <summary>
    /// 키보드 스폰 및 커버 오픈이 완료되었을 때 최초 1회 게임 타이머를 구동하고 콤보 타이머를 재개한다.
    /// keyboard 생성 완료된 키보드 인스턴스를 전달받으며 초기 타이머 시작 및 콤보 타이머 일시정지를 해제한다.
    /// </summary>
    private void HandleKeyboardSpawned(GameObject keyboard)
    {
        if (_tournamentController != null)
        {
            bool wasPreparing = _battlePreparing;
            KeyboardInputController input = keyboard.GetComponent<KeyboardInputController>();
            _playerReady = true;
            input.SetInputEnabled(_isTimerRunning && !_battlePreparing);
            if (_isTimerRunning) _comboHUD.SetTimerPaused(false);
            TryStartTournamentBattle();
            if (!wasPreparing && _isTimerRunning) PlaytestKeyboardReady?.Invoke(this, keyboard, false);
            return;
        }
        // 제한 시간이 이미 만료된 상태라면 새 키보드의 입력을 차단하고 타이머를 시작하지 않는다.
        if (_remainingTime <= 0f && _elapsedRoundTime > 0f)
        {
            KeyboardInputController inputController = keyboard.GetComponent<KeyboardInputController>();
            if (inputController != null)
            {
                inputController.SetInputEnabled(false);
            }
            return;
        }

        // 최초 키보드 스폰 시점에만 게임 타이머를 가동하고, 이후 리스폰 시에는 시간을 리셋하지 않는다.
        if (!_isTimerRunning && _remainingTime == 0f && _elapsedRoundTime == 0f)
        {
            StartRoundTimer();
        }

        // 키보드 교환 연출이 완료되어 입력을 시작할 수 있으므로 콤보 타이머를 재개한다.
        if (_comboHUD != null)
        {
            _comboHUD.SetTimerPaused(false);
        }
        if (_isTimerRunning) PlaytestKeyboardReady?.Invoke(this, keyboard, false);
    }

    /// <summary>
    /// 키보드의 마지막 키캡이 파괴되어 적을 쓰러트린 순간 콤보 타이머를 일시정지하고 처치 이벤트를 알린다.
    /// 게임 타이머는 멈추지 않고 계속 유지하며, 현재 경과 시간과 남은 제한 시간 상태로 RoundCleared 이벤트를 호출한다.
    /// </summary>
    private void HandleKeyboardBreaking()
    {
        if (_isSuddenDeath && _isTimerRunning) _playerClearFrame = Time.frameCount;
        // 키보드 완파 연출 및 교환 완료 전까지 콤보 타이머 감소만 일시정지한다.
        if (_comboHUD != null)
        {
            _comboHUD.AddKeyboardBreakScore();
            _comboHUD.SetTimerPaused(true);
        }

        // 키보드 처치 시점의 경과 시간과 남은 제한 시간을 로그로 기록하고 클리어 이벤트를 발행한다.
        Debug.Log($"[GameManager] Keyboard Cleared! Elapsed Time: {_elapsedRoundTime:F2}s (Limit: {_roundTimeLimit:F1}s, Remaining: {_remainingTime:F2}s)");
        RoundCleared?.Invoke(_elapsedRoundTime);
    }

    /// <summary>
    /// 게임 제한 시간이 만료되었을 때 타이머를 정지하고 타임오버 이벤트 및 후속 처리 메서드를 호출한다.
    /// _isTimerRunning을 중단하고 RoundTimeOut 이벤트를 발행하며 OnTimeOut을 호출한다.
    /// </summary>
    private void HandleTimeOut()
    {
        _isTimerRunning = false;
        UpdateTimerText(0f);

        // 제한 시간 초과 로그를 출력하고 타임아웃 이벤트를 알린다.
        Debug.LogWarning($"[GameManager] Round Time Out! Limit of {_roundTimeLimit:F1}s reached.");
        RoundTimeOut?.Invoke();
        OnTimeOut();
    }

    /// <summary>
    /// 게임 제한 시간이 만료되었을 때 키보드 입력을 차단하고 점수를 비교하는 등 후속 처리를 진행한다.
    /// _activeKeyboard의 입력을 비활성화하고 리스폰 코루틴 및 콤보 타이머를 정지하며 최종 점수를 비교 출력한다.
    /// </summary>
    private void OnTimeOut()
    {
        if (_tournamentController != null)
        {
            int result = _comboHUD.Score.CompareTo(_enemyScoreCombo.Score);
            FreezeTournamentBattle("TimeOut");
            TournamentBattleFinished?.Invoke(result);
            return;
        }
        Debug.Log("라운드 종료");

        // 현재 활성화된 키보드가 존재하면 입력을 즉시 차단한다.
        if (_activeKeyboard != null)
        {
            KeyboardInputController inputController = _activeKeyboard.GetComponent<KeyboardInputController>();
            if (inputController != null)
            {
                inputController.SetInputEnabled(false);
            }
        }

        // 진행 중인 플레이어 키보드 리스폰이 있다면 중단한다.
        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        // 플레이어 콤보 타이머 감소를 정지하여 최종 상태를 보존한다.
        if (_comboHUD != null)
        {
            _comboHUD.SetTimerPaused(true);
        }

        // 로봇의 타건 및 리스폰도 함께 정지한다.
        if (_enemyTypingController != null)
        {
            _enemyTypingController.StopAttacking();
        }

        if (_enemyRespawnRoutine != null)
        {
            StopCoroutine(_enemyRespawnRoutine);
            _enemyRespawnRoutine = null;
        }

        if (_enemyScoreCombo != null)
        {
            _enemyScoreCombo.SetTimerPaused(true);
        }

        // 플레이어와 로봇의 점수를 비교하고 결과를 콘솔에 출력한다.
        NotifyPlaytestRoundEnded("TimeOut", true);
        CompareAndLogScores();
    }

    void OnApplicationQuit() { NotifyPlaytestRoundEnded("ApplicationQuit", false); }

    /// <summary>
    /// reason과 complete로 실제 대결 종료를 관찰자에게 한 번 알린다.
    /// 로깅용 실행 상태만 변경하고 점수와 게임 진행은 변경하지 않는다.
    /// </summary>
    private void NotifyPlaytestRoundEnded(string reason, bool complete)
    {
        if (!_playtestRoundActive) return;
        _playtestRoundActive = false;
        PlaytestRoundEnded?.Invoke(this, reason, complete);
    }

    /// <summary>
    /// 결과 화면의 로그 폴더 버튼 클릭을 관찰자에게 전달한다.
    /// 입력값 없이 C# 이벤트를 발생시키며 파일 열기 처리는 로거가 담당한다.
    /// </summary>
    private void RequestPlaytestLogFolder() { PlaytestLogFolderRequested?.Invoke(); }

    /// <summary>
    /// 플레이어와 로봇의 점수 및 콤보를 비교하여 승패 결과를 콘솔에 출력하고 결과 UI에 문구와 수치를 할당한다.
    /// _comboHUD와 _enemyScoreCombo의 점수를 비교하여 _resultPanel을 활성화하고 텍스트를 갱신한다.
    /// </summary>
    private void CompareAndLogScores()
    {
        long playerScore = _comboHUD != null ? _comboHUD.Score : 0L;
        int playerCombo = _comboHUD != null ? _comboHUD.Combo : 0;

        long enemyScore = _enemyScoreCombo != null ? _enemyScoreCombo.Score : 0L;
        int enemyCombo = _enemyScoreCombo != null ? _enemyScoreCombo.Combo : 0;

        string resultTitle;
        string resultMessage;
        if (playerScore > enemyScore)
        {
            resultTitle = "플레이어 승리!";
            resultMessage = "플레이어 승리 (Player Win)";
        }
        else if (playerScore < enemyScore)
        {
            resultTitle = "로봇 승리 (패배)";
            resultMessage = "로봇 승리 (Robot Win)";
        }
        else
        {
            resultTitle = "무 승 부";
            resultMessage = "무승부 (Draw)";
        }

        string resultScoreSummary = $"플레이어: {playerScore:N0}점 ({playerCombo} COMBO)\n로봇: {enemyScore:N0}점 ({enemyCombo} COMBO)";

        // 결과 UI 게임 오브젝트가 존재하면 활성화하고 문구와 점수 수치만 할당한다.
        ApplyResultUI(resultTitle, resultScoreSummary);

        // 플레이어와 로봇의 최종 점수 및 승패 결과를 로그로 기록한다.
        Debug.Log($"[GameManager] 결과 비교 - 플레이어: {playerScore}점 ({playerCombo}콤보) vs 로봇: {enemyScore}점 ({enemyCombo}콤보) => {resultMessage}");
    }

    /// <summary>
    /// 결과 UI 게임 오브젝트에 판정 문구 및 점수 상세 텍스트를 할당하고 패널을 활성화한다.
    /// title 승패 판정 타이틀과 scoreSummary 점수/콤보 요약 문자열을 받아 UI 컴포넌트에 반영한다.
    /// </summary>
    private void ApplyResultUI(string title, string scoreSummary)
    {
        _resultPanel.SetActive(true);
        _resultTitleText.text = title;
        _resultScoreText.text = scoreSummary;
        _resultText.text = $"{title}\n\n{scoreSummary}";
    }

    /// <summary>
    /// 남은 제한 시간을 전달받아 시간 UI 텍스트의 문자열과 색상을 갱신한다.
    /// remainingTime 남은 제한 시간을 입력받아 _timeTexts의 text 및 color를 변경한다.
    /// </summary>
    private void UpdateTimerText(float remainingTime)
    {
        float clampedTime = Mathf.Max(0f, remainingTime);
        int minutes = Mathf.FloorToInt(clampedTime / 60f);
        float seconds = clampedTime % 60f;

        // 1분 이상일 때는 분:초 형식으로 표시하고, 1분 미만일 때는 초.밀리초 형식으로 표시한다.
        if (minutes > 0)
        {
            _timeTexts.text = $"{minutes:00}:{seconds:00.00}";
        }
        else
        {
            _timeTexts.text = $"{seconds:00.00}";
        }

        // 제한 시간이 10초 이하이고 타이머가 가동 중일 때는 붉은색 경고 색상으로 전환한다.
        _timeTexts.color = clampedTime <= 10f && _isTimerRunning ? Color.red : _defaultTimeTextColor;
    }

    /// <summary>
    /// 현재 활성화된 키보드의 파괴 관련 이벤트 구독을 해제한다.
    /// _activeDestruction의 Breaking 및 Finished 이벤트를 해제하고 참조를 초기화한다.
    /// </summary>
    private void UnsubscribeKeyboardDestruction()
    {
        if (_activeDestruction != null)
        {
            _activeDestruction.Breaking -= HandleKeyboardBreaking;
            _activeDestruction.Finished -= HandleKeyboardFinished;
            _activeDestruction = null;
        }
    }

    /// <summary>
    /// 키보드 완파 연출 완료를 수신하여 새 키보드 리스폰 코루틴을 시작한다.
    /// _activeDestruction의 Finished 이벤트 발생 시 호출되며 리스폰 코루틴을 가동한다.
    /// </summary>
    private void HandleKeyboardFinished()
    {
        UnsubscribeKeyboardDestruction();
        if (_tournamentController != null && (!_isTimerRunning || _isSuddenDeath)) return;

        // 제한 시간이 이미 만료되었다면 새 키보드를 리스폰하지 않는다.
        if (_remainingTime <= 0f && _elapsedRoundTime > 0f)
        {
            return;
        }

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
        }

        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    /// <summary>
    /// 지연 시간 대기 후 이전 키보드를 정리하고 새 키보드를 스폰한다.
    /// _respawnDelay 대기 시간을 거쳐 슬라이드 애니메이션과 함께 키보드를 새로 스폰한다.
    /// </summary>
    private IEnumerator RespawnRoutine()
    {
        float timer = 0f;
        while (timer < _respawnDelay)
        {
            timer += Time.deltaTime;
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
        }

        // 이전 키보드 오브젝트가 남아 있다면 정리하고 새 키보드를 스폰한다.
        if (_activeKeyboard != null)
        {
            Destroy(_activeKeyboard);
            _activeKeyboard = null;
        }

        SpawnKeyboard(true);
        _respawnRoutine = null;
    }

    /// <summary>
    /// 로봇 키보드 생성을 요청하고 완파 이벤트 및 타건 컨트롤러를 등록한다.
    /// withAnimation 애니메이션 적용 여부를 사용하며 생성된 키보드 게임오브젝트를 반환한다.
    /// </summary>
    public GameObject SpawnEnemyKeyboard(bool withAnimation = true)
    {
        if (_enemyKeyboardSpawner == null || _enemyKeyboardSpawnPoint == null)
        {
            return null;
        }

        UnsubscribeEnemyKeyboardDestruction();

        // 등록된 로봇 스폰 포인트를 기준으로 키보드 생성을 요청한다.
        _activeEnemyKeyboard = _enemyKeyboardSpawner.SpawnKeyboard(_enemyKeyboardSpawnPoint, withAnimation);

        if (_activeEnemyKeyboard != null)
        {
            _activeEnemyDestruction = _activeEnemyKeyboard.GetComponent<KeyboardDestruction>();
            if (_activeEnemyDestruction == null)
            {
                _activeEnemyDestruction = _activeEnemyKeyboard.AddComponent<KeyboardDestruction>();
            }

            _activeEnemyDestruction.Breaking += HandleEnemyKeyboardBreaking;
            _activeEnemyDestruction.Finished += HandleEnemyKeyboardFinished;

            KeyboardInputController inputController = _activeEnemyKeyboard.GetComponent<KeyboardInputController>();
            if (inputController != null)
            {
                inputController.SetInputEnabled(false);
                inputController.enabled = false;
            }

            // 생성된 키보드를 로봇 타건 컨트롤러 및 스코어 콤보에 연결한다.
            if (_enemyTypingController != null)
            {
                _enemyTypingController.SetKeyboard(inputController);
            }

            if (_enemyScoreCombo != null)
            {
                _enemyScoreCombo.Keyboard = inputController;
            }
        }

        return _activeEnemyKeyboard;
    }

    /// <summary>
    /// 로봇 키보드 스폰 및 커버 오픈이 완료되었을 때 로봇 타건 및 콤보 타이머를 시작한다.
    /// keyboard 생성 완료된 키보드 인스턴스를 전달받으며 로봇 공격과 콤보 측정을 개시한다.
    /// </summary>
    private void HandleEnemyKeyboardSpawned(GameObject keyboard)
    {
        if (_tournamentController != null)
        {
            bool wasPreparing = _battlePreparing;
            KeyboardInputController input = keyboard.GetComponent<KeyboardInputController>();
            input.SetInputEnabled(false);
            input.enabled = false;
            _enemyReady = true;
            if (_isTimerRunning && !_battlePreparing)
            {
                _enemyTypingController.StartAttacking();
                _enemyScoreCombo.SetTimerPaused(false);
            }
            TryStartTournamentBattle();
            if (!wasPreparing && _isTimerRunning) PlaytestKeyboardReady?.Invoke(this, keyboard, true);
            return;
        }
        // 로봇 키보드는 플레이어의 실제 키보드 입력(Keyboard.current)을 받지 않도록 비활성화한다.
        KeyboardInputController inputController = keyboard.GetComponent<KeyboardInputController>();
        if (inputController != null)
        {
            inputController.SetInputEnabled(false);
            inputController.enabled = false;
        }

        // 제한 시간이 이미 만료된 상태라면 타건을 시작하지 않는다.
        if (_remainingTime <= 0f && _elapsedRoundTime > 0f)
        {
            return;
        }

        // 키보드 교환 연출이 완료되었으므로 로봇 타건과 콤보 타이머를 가동한다.
        if (_enemyTypingController != null)
        {
            _enemyTypingController.StartAttacking();
        }

        if (_enemyScoreCombo != null)
        {
            _enemyScoreCombo.SetTimerPaused(false);
        }
        if (_isTimerRunning) PlaytestKeyboardReady?.Invoke(this, keyboard, true);
    }

    /// <summary>
    /// 로봇 키보드의 마지막 키캡이 파괴된 순간 로봇 타건을 멈추고 콤보 타이머를 일시정지한다.
    /// 완파 연출 중에는 타건을 중단하고 교환 완료 전까지 콤보 타이머 감소를 정지한다.
    /// </summary>
    private void HandleEnemyKeyboardBreaking()
    {
        if (_isSuddenDeath && _isTimerRunning) _enemyClearFrame = Time.frameCount;
        // 완파 연출 중에는 로봇의 타건을 정지한다.
        if (_enemyTypingController != null)
        {
            _enemyTypingController.StopAttacking();
        }

        // 로봇 키보드 교환 완료 전까지 콤보 타이머 감소를 일시정지한다.
        if (_enemyScoreCombo != null)
        {
            _enemyScoreCombo.AddKeyboardBreakScore();
            _enemyScoreCombo.SetTimerPaused(true);
        }
    }

    /// <summary>
    /// 로봇 키보드 완파 연출 완료를 수신하여 새 로봇 키보드 리스폰 코루틴을 시작한다.
    /// _activeEnemyDestruction의 Finished 이벤트 발생 시 호출되며 리스폰 코루틴을 가동한다.
    /// </summary>
    private void HandleEnemyKeyboardFinished()
    {
        UnsubscribeEnemyKeyboardDestruction();
        if (_tournamentController != null && (!_isTimerRunning || _isSuddenDeath)) return;

        // 제한 시간이 이미 만료되었다면 새 키보드를 리스폰하지 않는다.
        if (_remainingTime <= 0f && _elapsedRoundTime > 0f)
        {
            return;
        }

        if (_enemyRespawnRoutine != null)
        {
            StopCoroutine(_enemyRespawnRoutine);
        }

        _enemyRespawnRoutine = StartCoroutine(EnemyRespawnRoutine());
    }

    /// <summary>
    /// 지연 시간 대기 후 이전 로봇 키보드를 정리하고 새 로봇 키보드를 스폰한다.
    /// _respawnDelay 대기 시간을 거쳐 슬라이드 애니메이션과 함께 키보드를 새로 스폰한다.
    /// </summary>
    private IEnumerator EnemyRespawnRoutine()
    {
        float timer = 0f;
        while (timer < _respawnDelay)
        {
            timer += Time.deltaTime;
            yield return null;
            while (Game.Tournament.TournamentController.IsReturnPaused) yield return null;
        }

        // 이전 로봇 키보드 오브젝트가 남아 있다면 정리하고 새 키보드를 스폰한다.
        if (_activeEnemyKeyboard != null)
        {
            Destroy(_activeEnemyKeyboard);
            _activeEnemyKeyboard = null;
        }

        SpawnEnemyKeyboard(true);
        _enemyRespawnRoutine = null;
    }

    /// <summary>
    /// 현재 활성화된 로봇 키보드의 파괴 관련 이벤트 구독을 해제한다.
    /// _activeEnemyDestruction의 Breaking 및 Finished 이벤트를 해제하고 참조를 초기화한다.
    /// </summary>
    private void UnsubscribeEnemyKeyboardDestruction()
    {
        if (_activeEnemyDestruction != null)
        {
            _activeEnemyDestruction.Breaking -= HandleEnemyKeyboardBreaking;
            _activeEnemyDestruction.Finished -= HandleEnemyKeyboardFinished;
            _activeEnemyDestruction = null;
        }
    }

    /// <summary>현재 씬을 다시 열어 게임 상태를 초기화하며 빌드 목록에 없는 작업 씬도 경로로 재시작한다.</summary>
    public void RestartGame()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.path);
    }
}
