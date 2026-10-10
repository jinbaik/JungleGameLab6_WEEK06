using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using Game.Economy;
using Game.Session;
using KeyboardModeling;

[DisallowMultipleComponent]
public sealed class MonitorCoinRain : MonoBehaviour
{
    // UI 좌표 100을 물리 좌표 1로 변환한다.
    private const float PHYSICS_SCALE = 0.01f;
    private static readonly int[] _coinAmounts = { 1000, 100, 50, 10 };

    [Header("References")]
    [SerializeField] private GameSession _gameSession;
    [SerializeField] private MiniGameScreenView _screenView;
    [SerializeField] private RectTransform _background;
    [SerializeField] private PhysicsMaterial2D _collisionMaterial;
    private RectTransform _canvasRoot;
    private readonly Vector3[] _backgroundCorners = new Vector3[4];
    private Wallet _wallet;
    private long _lastBalance;
    private PlayableGraph _animationGraph;
    private AnimationClipPlayable[] _coinPlayables;

    [Header("Coin Assets")]
    [SerializeField] private GameObject _goldPrefab;
    [SerializeField] private AnimationClip _goldAnimation;
    [SerializeField] private GameObject _silverPrefab;
    [SerializeField] private AnimationClip _silverAnimation;
    [SerializeField] private GameObject _ironPrefab;
    [SerializeField] private AnimationClip _ironAnimation;
    [SerializeField] private GameObject _copperPrefab;
    [SerializeField] private AnimationClip _copperAnimation;
    private GameObject[] _coinPrefabs;
    private AnimationClip[] _coinAnimations;

    [Header("Live Options")]
    [SerializeField] private bool _effectEnabled = true;
    [SerializeField, Range(8, 64)] private int _poolSize = 24;
    [SerializeField, Min(0.01f)] private float _spawnInterval = 0.1f;
    private readonly Queue<(int Type, int Count)> _pendingCoins = new Queue<(int Type, int Count)>();
    private readonly int[] _nextCoins = new int[4];
    private int _pendingType;
    private int _pendingCount;
    private float _spawnTimer;
    public event Action<int> CoinSpawned;

    [Header("Canvas Drop")]
    [SerializeField, Min(1f)] private float _coinSize = 28f;
    [SerializeField, Min(0.1f)] private float _lifetime = 3f;
    [SerializeField, Min(0f)] private float _blinkRemainingTime = 1f;
    [SerializeField, Min(0.01f)] private float _blinkFadeDuration = 0.1f;
    [SerializeField, Range(0f, 1f)] private float _screenWidth = 1f;
    [SerializeField, Min(0f)] private float _gravity = 500f;
    [SerializeField, Min(0f)] private float _initialFallSpeed = 70f;
    private RectTransform _poolRoot;
    private Image[] _coins;
    private Rigidbody2D[] _bodies;
    private CircleCollider2D[] _coinColliders;
    private BoxCollider2D[] _walls;
    private Scene _physicsScene;
    private PhysicsScene2D _physicsWorld;
    private Transform _physicsRoot;
    private float[] _ages;
    private float[] _animationOffsets;
    private bool CanShowCoins => _effectEnabled && _canvasRoot.gameObject.activeInHierarchy;

    /// <summary>
    /// Inspector의 화면, 프리팹과 애니메이션 참조로 코인 연출을 초기화한다.
    /// UI 루트와 독립 물리 씬을 만들고 배경 영역에 맞춘 뒤 재사용할 코인 풀을 생성한다.
    /// </summary>
    void Awake()
    {
        _coinPrefabs = new[] { _goldPrefab, _silverPrefab, _ironPrefab, _copperPrefab };
        _coinAnimations = new[] { _goldAnimation, _silverAnimation, _ironAnimation, _copperAnimation };
        CreatePoolRoot();
        CreatePhysicsWorld();
        FitPoolToBackground();
        CreateCoinPool();
    }

    /// <summary>
    /// _gameSession의 지갑을 가져와 현재 잔액부터 수입 감지를 시작한다.
    /// _wallet을 저장하고 잔액 변경 이벤트를 구독한다.
    /// </summary>
    void Start()
    {
        _wallet = _gameSession.Wallet;
        ConnectWallet();
    }

    /// <summary>
    /// 재활성화 시 이미 저장된 지갑을 사용해 잔액 변경 이벤트를 다시 구독한다.
    /// Start 이전에는 연결하지 않으며 비활성 동안 발생한 수입은 재생하지 않는다.
    /// </summary>
    void OnEnable()
    {
        if (_wallet != null) ConnectWallet();
    }

    /// <summary>
    /// 지갑 이벤트 구독을 해제하고 모든 코인의 UI 표시와 물리 시뮬레이션을 끈다.
    /// 풀 인스턴스는 유지하여 재활성화 후 다시 사용한다.
    /// </summary>
    void OnDisable()
    {
        if (_wallet != null) _wallet.BalanceChanged -= HandleBalanceChanged;
        ClearCoins();
    }

    /// <summary>
    /// 생성한 애니메이션 그래프와 UI 루트를 제거하고 코인 전용 물리 씬을 해제한다.
    /// 물리 씬에 분리해 보관한 Rigidbody와 벽도 함께 정리한다.
    /// </summary>
    void OnDestroy()
    {
        if (_animationGraph.IsValid()) _animationGraph.Destroy();
        if (_poolRoot != null) Destroy(_poolRoot.gameObject);
        if (_physicsScene.IsValid() && _physicsScene.isLoaded) SceneManager.UnloadSceneAsync(_physicsScene);
    }

    /// <summary>
    /// 활성 코인의 수명, 깜빡임, 애니메이션과 물리 위치를 프레임마다 갱신한다.
    /// unscaledDeltaTime을 사용해 게임 시간 배율과 독립적으로 진행하며 숨긴 화면의 코인은 모두 정리한다.
    /// </summary>
    void Update()
    {
        if (!CanShowCoins) { ClearCoins(); return; }
        bool animated = false;
        for (int index = 0; index < _coins.Length; index++)
        {
            Image coin = _coins[index];
            if (!coin.gameObject.activeSelf) continue;
            _ages[index] += Time.unscaledDeltaTime;
            if (HideExpiredCoin(index)) continue;
            _coinPlayables[index].SetTime((_ages[index] + _animationOffsets[index]) % _coinAnimations[index / _poolSize].length);
            animated = true;
            UpdateCoinBlink(index);
        }
        ProcessSpawnQueue(Time.unscaledDeltaTime);
        SimulateCoins(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        if (animated) _animationGraph.Evaluate(0f);
        SyncCoinPositions();
    }

    /// <summary>
    /// _screenView의 RectTransform 아래에 코인 표시용 UI 루트를 생성한다.
    /// 중앙 앵커와 기본 배율을 설정하고 마지막 자식으로 배치해 기존 화면 위에 코인을 표시한다.
    /// </summary>
    private void CreatePoolRoot()
    {
        _canvasRoot = _screenView.GetComponent<RectTransform>();
        _poolRoot = new GameObject("MonitorCoinPool", typeof(RectTransform)).GetComponent<RectTransform>();
        _poolRoot.SetParent(_canvasRoot, false);
        _poolRoot.gameObject.layer = _canvasRoot.gameObject.layer;
        _poolRoot.pivot = new Vector2(0.5f, 0.5f);
        _poolRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _poolRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _poolRoot.offsetMin = Vector2.zero;
        _poolRoot.offsetMax = Vector2.zero;
        _poolRoot.localScale = Vector3.one;
        _poolRoot.localRotation = Quaternion.identity;
        _poolRoot.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// 4종 코인 프리팹을 종류마다 _poolSize만큼 생성하고 Image, Collider와 애니메이션을 배열에 저장한다.
    /// 물리 자식은 전용 씬으로 분리하며 생성 직후 UI와 시뮬레이션을 꺼 재사용 대기 상태로 둔다.
    /// </summary>
    private void CreateCoinPool()
    {
        int totalCount = _poolSize * _coinPrefabs.Length;
        _coins = new Image[totalCount];
        _coinPlayables = new AnimationClipPlayable[totalCount];
        _animationGraph = PlayableGraph.Create("MonitorCoinAnimation");
        _animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _bodies = new Rigidbody2D[totalCount];
        _coinColliders = new CircleCollider2D[totalCount];
        _ages = new float[totalCount];
        _animationOffsets = new float[totalCount];
        for (int index = 0; index < _coins.Length; index++)
        {
            int type = index / _poolSize;
            GameObject coin = Instantiate(_coinPrefabs[type], _poolRoot);
            coin.layer = _canvasRoot.gameObject.layer;
            _coins[index] = coin.GetComponent<Image>();
            _coins[index].raycastTarget = false;
            Rigidbody2D body = coin.GetComponentInChildren<Rigidbody2D>();
            body.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(body.gameObject, _physicsScene);
            body.transform.SetParent(_physicsRoot, false);
            body.name = "CoinPhysics_" + index;
            body.simulated = false;
            _bodies[index] = body;
            _coinColliders[index] = body.GetComponent<CircleCollider2D>();
            _coinColliders[index].sharedMaterial = _collisionMaterial;
            _coinPlayables[index] = AnimationClipPlayable.Create(_animationGraph, _coinAnimations[type]);
            _coinPlayables[index].SetSpeed(0);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_animationGraph, "Coin" + index, coin.GetComponent<Animator>());
            output.SetSourcePlayable(_coinPlayables[index]);
            coin.SetActive(false);
        }
        _animationGraph.Play();
    }

    /// <summary>
    /// 코인 전용 독립 2D 물리 씬과 바닥 및 좌우 벽 Collider를 생성한다.
    /// _collisionMaterial을 벽에 적용하고 씬, 물리 월드와 벽 배열을 저장해 다른 씬의 충돌과 분리한다.
    /// </summary>
    private void CreatePhysicsWorld()
    {
        _physicsScene = SceneManager.CreateScene("MonitorCoinPhysics_" + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        _physicsWorld = _physicsScene.GetPhysicsScene2D();
        _physicsRoot = new GameObject("MonitorCoinPhysics").transform;
        SceneManager.MoveGameObjectToScene(_physicsRoot.gameObject, _physicsScene);
        _walls = new BoxCollider2D[3];
        string[] names = { "Floor", "LeftWall", "RightWall" };
        for (int index = 0; index < _walls.Length; index++)
        {
            GameObject wall = new GameObject(names[index]);
            wall.transform.SetParent(_physicsRoot, false);
            _walls[index] = wall.AddComponent<BoxCollider2D>();
            _walls[index].sharedMaterial = _collisionMaterial;
        }
    }

    /// <summary>
    /// 배경의 현재 Rect와 좌표 변환 배율을 사용해 물리 바닥과 좌우 벽을 배치한다.
    /// 각 BoxCollider2D의 위치와 크기를 갱신한다.
    /// </summary>
    private void FitPhysicsBoundaries()
    {
        Rect bounds = _poolRoot.rect;
        float thickness = 0.1f;
        _walls[0].transform.localPosition = new Vector3(bounds.center.x * PHYSICS_SCALE, bounds.yMin * PHYSICS_SCALE - thickness * 0.5f, 0f);
        _walls[0].size = new Vector2(bounds.width * PHYSICS_SCALE + thickness * 2f, thickness);
        _walls[1].transform.localPosition = new Vector3(bounds.xMin * PHYSICS_SCALE - thickness * 0.5f, bounds.center.y * PHYSICS_SCALE, 0f);
        _walls[2].transform.localPosition = new Vector3(bounds.xMax * PHYSICS_SCALE + thickness * 0.5f, bounds.center.y * PHYSICS_SCALE, 0f);
        _walls[1].size = _walls[2].size = new Vector2(thickness, bounds.height * PHYSICS_SCALE + thickness * 2f);
    }

    /// <summary>
    /// deltaTime을 작은 시간 간격으로 나눠 코인 전용 Unity 2D 물리를 진행한다.
    /// 중력을 힘으로 적용하고 Rigidbody2D 위치를 갱신하며 충돌은 엔진이 처리한다.
    /// </summary>
    private void SimulateCoins(float deltaTime)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime * 120f));
        float stepTime = deltaTime / steps;
        for (int step = 0; step < steps; step++)
        {
            foreach (Rigidbody2D body in _bodies)
                if (body.simulated) body.AddForce(Vector2.down * (_gravity * PHYSICS_SCALE * body.mass));
            _physicsWorld.Simulate(stepTime);
        }
    }

    /// <summary>
    /// 활성 Rigidbody2D의 최종 위치를 PHYSICS_SCALE로 변환하여 UI 코인에 반영한다.
    /// 애니메이션 평가 후 호출하여 화면 위치를 물리 위치와 일치시킨다.
    /// </summary>
    private void SyncCoinPositions()
    {
        for (int index = 0; index < _coins.Length; index++)
            if (_bodies[index].simulated) _coins[index].rectTransform.anchoredPosition = _bodies[index].position / PHYSICS_SCALE;
    }

    /// <summary>
    /// index 코인의 나이를 수명과 비교하여 만료된 코인을 비활성화한다.
    /// 수명이 끝나면 UI와 Rigidbody 시뮬레이션을 끄고 true를 반환하며, 아직 남아 있으면 false를 반환한다.
    /// </summary>
    private bool HideExpiredCoin(int index)
    {
        if (_ages[index] < _lifetime) return false;
        _coins[index].gameObject.SetActive(false);
        _bodies[index].simulated = false;
        return true;
    }

    /// <summary>
    /// index 코인의 남은 수명이 설정된 시간 이하면 알파를 반복해서 내리고 올린다.
    /// _blinkFadeDuration마다 알파를 1에서 0, 다시 1로 변경하며 깜빡임 시작 전에는 알파를 1로 유지한다.
    /// </summary>
    private void UpdateCoinBlink(int index)
    {
        float blinkAge = _ages[index] - Mathf.Max(0f, _lifetime - _blinkRemainingTime);
        float alpha = blinkAge < 0f ? 1f : 1f - Mathf.PingPong(blinkAge / _blinkFadeDuration, 1f);
        Color color = _coins[index].color;
        color.a = alpha;
        _coins[index].color = color;
    }

    /// <summary>
    /// 현재 지갑 잔액을 기준으로 수입 알림을 연결한다.
    /// BalanceChanged를 구독하고 초기 잔액이나 비활성 동안의 과거 수입은 재생하지 않는다.
    /// </summary>
    private void ConnectWallet()
    {
        _lastBalance = _wallet.Balance;
        _wallet.BalanceChanged += HandleBalanceChanged;
    }

    /// <summary>
    /// balance의 증가분을 활성 미니게임 캔버스 안에서 코인으로 표시한다.
    /// 증가분을 int로 변환해 단위별 대기열에 추가하며 감소한 잔액은 연출하지 않는다.
    /// </summary>
    private void HandleBalanceChanged(long balance)
    {
        long income = balance - _lastBalance;
        _lastBalance = balance;
        if (!CanShowCoins || income <= 0) return;
        QueueIncome(checked((int)income));
    }

    /// <summary>
    /// amount를 1000, 100, 50, 10 순서로 분해해 종류와 개수를 생성 대기열에 저장한다.
    /// 획득량은 10의 배수로 처리하며 지갑 잔액은 변경하지 않는다.
    /// </summary>
    private void QueueIncome(int amount)
    {
        for (int type = 0; type < _coinAmounts.Length; type++)
        {
            int count = amount / _coinAmounts[type];
            amount %= _coinAmounts[type];
            if (count > 0) _pendingCoins.Enqueue((type, count));
        }
    }

    /// <summary>
    /// deltaTime으로 생성 간격을 계산하고 대기열의 코인을 한 개씩 순서대로 표시한다.
    /// 생성한 코인의 단위 금액을 CoinSpawned로 전달하며 같은 종류의 남은 개수를 차감한다.
    /// </summary>
    private void ProcessSpawnQueue(float deltaTime)
    {
        if (_pendingCount == 0 && _pendingCoins.Count == 0) { _spawnTimer = 0f; return; }
        _spawnTimer -= deltaTime;
        if (_spawnTimer > 0f) return;
        if (_pendingCount == 0)
        {
            (int type, int count) = _pendingCoins.Dequeue();
            _pendingType = type;
            _pendingCount = count;
        }
        if (!PlayCoin(_pendingType)) return;
        _pendingCount--;
        _spawnTimer = _spawnInterval;
        CoinSpawned?.Invoke(_coinAmounts[_pendingType]);
    }

    /// <summary>
    /// 미니게임 캔버스의 최상단 랜덤 가로 위치에서 다음 코인을 수직으로 낙하시킨다.
    /// type의 풀에서 빈 코인을 찾아 초기화하며 생성하면 true, 풀이 모두 사용 중이면 false를 반환한다.
    /// </summary>
    private bool PlayCoin(int type)
    {
        int checkedCount = 0;
        while (checkedCount < _poolSize && _coins[type * _poolSize + _nextCoins[type]].gameObject.activeSelf)
        {
            _nextCoins[type] = (_nextCoins[type] + 1) % _poolSize;
            checkedCount++;
        }
        if (checkedCount == _poolSize) return false;
        int index = type * _poolSize + _nextCoins[type];
        _nextCoins[type] = (_nextCoins[type] + 1) % _poolSize;
        Image coin = _coins[index];
        FitPoolToBackground();
        float size = _coinSize * UnityEngine.Random.Range(0.85f, 1.15f);
        // 코인 반지름과 여백을 제외한 배경 너비 안에서 생성 X 좌표를 무작위로 선택한다.
        float horizontalRange = Mathf.Max(0f, _poolRoot.rect.width * 0.5f - size * 0.5f - 2f) * _screenWidth;
        Vector2 spawnPosition = new Vector2(UnityEngine.Random.Range(-horizontalRange, horizontalRange), _poolRoot.rect.yMax - size * 0.5f - 2f);
        coin.rectTransform.anchoredPosition = spawnPosition;
        coin.rectTransform.sizeDelta = Vector2.one * size;
        coin.rectTransform.localScale = Vector3.one;
        coin.rectTransform.localRotation = Quaternion.identity;
        Rigidbody2D body = _bodies[index];
        // 풀에서 재사용할 물리 코인의 위치와 속도를 UI 생성 위치에 맞춰 초기화한다.
        body.simulated = false;
        Vector2 physicsPosition = spawnPosition * PHYSICS_SCALE;
        body.transform.SetPositionAndRotation(new Vector3(physicsPosition.x, physicsPosition.y, 0f), Quaternion.identity);
        _coinColliders[index].radius = size * 0.5f * PHYSICS_SCALE;
        body.simulated = true;
        body.position = physicsPosition;
        body.rotation = 0f;
        body.linearVelocity = Vector2.down * (UnityEngine.Random.Range(_initialFallSpeed * 0.5f, _initialFallSpeed) * PHYSICS_SCALE);
        body.angularVelocity = 0f;
        _ages[index] = 0f;
        _animationOffsets[index] = UnityEngine.Random.Range(0f, _coinAnimations[type].length);
        coin.gameObject.SetActive(true);
        _coinPlayables[index].SetTime(_animationOffsets[index]);
        _animationGraph.Evaluate(0f);
        SyncCoinPositions();
        coin.color = Color.white;
        return true;
    }

    /// <summary>
    /// Background의 실제 모서리를 캔버스 로컬 좌표로 변환해 코인 표시 영역을 맞춘다.
    /// 배경의 위치, 크기와 배율을 사용하여 풀의 중심과 Rect 크기를 설정하며 매니저의 위치는 변경하지 않는다.
    /// </summary>
    private void FitPoolToBackground()
    {
        _background.GetWorldCorners(_backgroundCorners);
        Vector3 minimum = _canvasRoot.InverseTransformPoint(_backgroundCorners[0]);
        Vector3 maximum = minimum;
        for (int index = 1; index < _backgroundCorners.Length; index++)
        {
            Vector3 point = _canvasRoot.InverseTransformPoint(_backgroundCorners[index]);
            minimum = Vector3.Min(minimum, point);
            maximum = Vector3.Max(maximum, point);
        }
        _poolRoot.sizeDelta = new Vector2(maximum.x - minimum.x, maximum.y - minimum.y);
        _poolRoot.localPosition = (minimum + maximum) * 0.5f;
        FitPhysicsBoundaries();
    }

    /// <summary>
    /// 풀에 남아 있는 코인 연출을 모두 숨긴다.
    /// 화면 전환, 옵션 해제 또는 비활성화 시 대기열과 타이머를 비우고 인스턴스는 유지하여 재사용한다.
    /// </summary>
    private void ClearCoins()
    {
        _pendingCoins.Clear();
        _pendingCount = 0;
        _spawnTimer = 0f;
        if (_coins == null) return;
        for (int index = 0; index < _coins.Length; index++)
        {
            if (_coins[index] != null) _coins[index].gameObject.SetActive(false);
            if (_bodies[index] != null) _bodies[index].simulated = false;
        }
    }
}






