using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

using Game.Economy;
using Game.Session;
using KeyboardModeling;

[DisallowMultipleComponent]
public sealed class MonitorCoinRain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameSession _gameSession;
    [SerializeField] private MiniGameScreenView _screenView;
    [SerializeField] private RectTransform _background;
    [SerializeField] private GameObject _coinPrefab;
    [SerializeField] private AnimationClip _coinAnimation;
    private RectTransform _canvasRoot;
    private readonly Vector3[] _backgroundCorners = new Vector3[4];
    private Wallet _wallet;
    private long _lastBalance;
    private PlayableGraph _animationGraph;
    private AnimationClipPlayable[] _coinPlayables;

    [Header("Live Options")]
    [SerializeField] private bool _effectEnabled = true;
    [SerializeField, Range(8, 64)] private int _poolSize = 24;
    [SerializeField, Min(1)] private long _amountPerCoin = 10;
    [SerializeField, Range(1, 12)] private int _maximumCoinsPerIncome = 6;

    [Header("Canvas Drop")]
    [SerializeField, Min(1f)] private float _coinSize = 28f;
    [SerializeField, Min(0.1f)] private float _lifetime = 2f;
    [SerializeField, Range(0f, 1f)] private float _screenWidth = 1f;
    [SerializeField, Min(0f)] private float _gravity = 500f;
    [SerializeField, Min(0f)] private float _initialFallSpeed = 70f;
    private RectTransform _poolRoot;
    private Image[] _coins;
    private Vector2[] _velocities;
    private float[] _ages;
    private float[] _animationOffsets;
    private int _nextCoin;
    private bool CanShowCoins => _effectEnabled && _canvasRoot.gameObject.activeInHierarchy;

    void Awake()
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
        FitPoolToBackground();
        _coins = new Image[_poolSize];
        _coinPlayables = new AnimationClipPlayable[_poolSize];
        _animationGraph = PlayableGraph.Create("MonitorCoinAnimation");
        _animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _velocities = new Vector2[_poolSize];
        _ages = new float[_poolSize];
        _animationOffsets = new float[_poolSize];
        for (int index = 0; index < _coins.Length; index++)
        {
            GameObject coin = Instantiate(_coinPrefab, _poolRoot);
            coin.layer = _canvasRoot.gameObject.layer;
            _coins[index] = coin.GetComponent<Image>();
            _coins[index].raycastTarget = false;
            _coinPlayables[index] = AnimationClipPlayable.Create(_animationGraph, _coinAnimation);
            _coinPlayables[index].SetSpeed(0);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_animationGraph, "Coin" + index, coin.GetComponent<Animator>());
            output.SetSourcePlayable(_coinPlayables[index]);
            coin.SetActive(false);
        }
        _animationGraph.Play();
    }

    void Start()
    {
        _wallet = _gameSession.Wallet;
        ConnectWallet();
    }

    void OnEnable()
    {
        if (_wallet != null) ConnectWallet();
    }

    void OnDisable()
    {
        if (_wallet != null) _wallet.BalanceChanged -= HandleBalanceChanged;
        ClearCoins();
    }

    void OnDestroy()
    {
        if (_animationGraph.IsValid()) _animationGraph.Destroy();
        if (_poolRoot != null) Destroy(_poolRoot.gameObject);
    }

    void Update()
    {
        if (!CanShowCoins) { ClearCoins(); return; }
        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        bool animated = false;
        for (int index = 0; index < _coins.Length; index++)
        {
            Image coin = _coins[index];
            if (!coin.gameObject.activeSelf) continue;
            _ages[index] += Time.unscaledDeltaTime;
            if (_ages[index] >= _lifetime)
            {
                coin.gameObject.SetActive(false);
                continue;
            }
            _velocities[index] += Vector2.down * (_gravity * deltaTime);
            Vector2 nextPosition = coin.rectTransform.anchoredPosition + _velocities[index] * deltaTime;
            float bottom = _poolRoot.rect.yMin + coin.rectTransform.rect.height * 0.5f;
            if (nextPosition.y < bottom)
            {
                coin.gameObject.SetActive(false);
                continue;
            }
            coin.rectTransform.anchoredPosition = nextPosition;
            _coinPlayables[index].SetTime((_ages[index] + _animationOffsets[index]) % _coinAnimation.length);
            animated = true;
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, _ages[index] / _lifetime));
            float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((nextPosition.y - bottom) / _coinSize));
            coin.color = new Color(1f, 1f, 1f, fade * edgeFade);
        }
        if (animated) _animationGraph.Evaluate(0f);
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
    /// 일반, 해킹 및 타이핑 상태 모두 허용하며 숨긴 캔버스에서 발생한 수입은 나중에 재생하지 않는다.
    /// </summary>
    private void HandleBalanceChanged(long balance)
    {
        long income = balance - _lastBalance;
        _lastBalance = balance;
        if (!CanShowCoins || income <= 0) return;
        long units = _amountPerCoin > 0 ? _amountPerCoin : 1;
        long count = income / units + (income % units > 0 ? 1 : 0);
        int visibleCount = count > _maximumCoinsPerIncome ? _maximumCoinsPerIncome : (int)count;
        for (int index = 0; index < visibleCount; index++) PlayCoin();
    }

    /// <summary>
    /// 미니게임 캔버스의 최상단 랜덤 가로 위치에서 다음 코인을 수직으로 낙하시킨다.
    /// 가로 이동이 없는 속도와 랜덤 시작 프레임을 설정하고 UI 전용 클립을 Image에서 직접 재생한다.
    /// </summary>
    private void PlayCoin()
    {
        int index = _nextCoin;
        _nextCoin = (_nextCoin + 1) % _coins.Length;
        Image coin = _coins[index];
        FitPoolToBackground();
        float size = _coinSize * Random.Range(0.85f, 1.15f);
        float horizontalRange = Mathf.Max(0f, _poolRoot.rect.width * 0.5f - size * 0.5f - 2f) * _screenWidth;
        coin.rectTransform.anchoredPosition = new Vector2(Random.Range(-horizontalRange, horizontalRange), _poolRoot.rect.yMax - size * 0.5f - 2f);
        coin.rectTransform.sizeDelta = Vector2.one * size;
        coin.rectTransform.localScale = Vector3.one;
        coin.rectTransform.localRotation = Quaternion.identity;
        _velocities[index] = Vector2.down * Random.Range(_initialFallSpeed * 0.5f, _initialFallSpeed);
        _ages[index] = 0f;
        _animationOffsets[index] = Random.Range(0f, _coinAnimation.length);
        coin.gameObject.SetActive(true);
        _coinPlayables[index].SetTime(_animationOffsets[index]);
        _animationGraph.Evaluate(0f);
        coin.color = Color.white;
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
    }
    /// <summary>
    /// 풀에 남아 있는 코인 연출을 모두 숨긴다.
    /// 화면 전환, 옵션 해제 또는 비활성화 시 인스턴스는 유지하여 재사용한다.
    /// </summary>
    private void ClearCoins()
    {
        if (_coins == null) return;
        foreach (Image coin in _coins)
            if (coin != null) coin.gameObject.SetActive(false);
    }
}






