using System.Collections.Generic;
using System.Globalization;

using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

using Game.Economy;
using Game.Session;

namespace Game.UI
{
    public sealed class WalletHudController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private Text _balanceLabel;
        [SerializeField] private RectTransform _toastRoot;
        [SerializeField] private WalletHudToastView _toastPrefab;
        private MonitorCoinRain _coinRain;

        [Header("Toast Pool")]
        [SerializeField, Range(1, 8)] private int _maxVisible = 4;
        private ObjectPool<WalletHudToastView> _pool;
        private List<WalletHudToastView> _active;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float _duration = 1.2f;
        [SerializeField, Min(0f)] private float _riseDistance = 14f;
        [SerializeField, Range(0f, 0.95f)] private float _fadeStart = 0.55f;
        [SerializeField, Min(36f)] private float _rowSpacing = 42f;
        [SerializeField] private Color _toastColor = new Color(1f, 0.85f, 0.25f);

        [Header("Runtime State")]
        private Wallet _wallet;
        private long _lastBalance;

        void Awake()
        {
            _coinRain = FindAnyObjectByType<MonitorCoinRain>();
            _active = new List<WalletHudToastView>(_maxVisible);
            _pool = new ObjectPool<WalletHudToastView>(CreateView, ActivateView, ReturnView, DestroyView, false, _maxVisible, _maxVisible);
            for (int index = 0; index < _maxVisible; index++) _active.Add(_pool.Get());
            ClearToasts();
        }

        void OnEnable()
        {
            if (_coinRain != null) _coinRain.CoinSpawned += ShowGain;
            if (_wallet != null) BindWallet();
        }

        void Start()
        {
            _wallet = _gameSession.Wallet;
            BindWallet();
        }

        void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                WalletHudToastView view = _active[index];
                if (view.Tick(deltaTime, _active.Count - 1 - index, _rowSpacing)) continue;
                _active.RemoveAt(index);
                _pool.Release(view);
            }
        }

        void OnDisable()
        {
            if (_coinRain != null) _coinRain.CoinSpawned -= ShowGain;
            if (_wallet != null) _wallet.BalanceChanged -= OnBalanceChanged;
            ClearToasts();
        }

        void OnDestroy()
        {
            _pool?.Dispose();
        }

        /// <summary>
        /// 현재 Session 지갑의 잔액 변경 이벤트를 구독한다.
        /// 초기 잔액을 HUD에 반영하되 이미 보유한 금액에 대한 획득 알림은 생성하지 않는다.
        /// </summary>
        private void BindWallet()
        {
            _wallet.BalanceChanged += OnBalanceChanged;
            _lastBalance = _wallet.Balance;
            RefreshBalance(_lastBalance);
        }

        /// <summary>
        /// balance를 실제 지갑 잔액으로 즉시 표시하며 코인 연출이 있는 씬의 획득 알림은 생성 이벤트에 맡긴다.
        /// 코인 연출이 없는 씬에서는 증가분을 int로 변환하여 기존 획득 알림을 표시한다.
        /// </summary>
        private void OnBalanceChanged(long balance)
        {
            long gained = balance - _lastBalance;
            _lastBalance = balance;
            RefreshBalance(balance);
            if (_coinRain == null && gained > 0) ShowGain(checked((int)gained));
        }

        /// <summary>
        /// balance를 천 단위 구분자가 있는 현재 보유 금액으로 표시한다.
        /// _balanceLabel의 텍스트를 지갑 잔액과 동기화한다.
        /// </summary>
        private void RefreshBalance(long balance)
        {
            _balanceLabel.text = balance.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 생성된 코인의 단위 금액 amount를 합산 없이 개별 획득 알림으로 표시한다.
        /// 풀에서 알림을 대여하며 용량이 가득 차면 가장 오래된 알림을 재사용한다.
        /// </summary>
        private void ShowGain(int amount)
        {
            if (_active.Count == _maxVisible)
            {
                WalletHudToastView oldest = _active[0];
                _active.RemoveAt(0);
                _pool.Release(oldest);
            }
            WalletHudToastView view = _pool.Get();
            view.Begin(amount, _duration, _riseDistance, _fadeStart, _toastColor);
            _active.Add(view);
            for (int index = 0; index < _active.Count; index++)
                _active[index].Tick(0f, _active.Count - 1 - index, _rowSpacing);
        }

        /// <summary>
        /// 표시 중인 HUD 알림을 초기화하고 풀로 반환한다.
        /// 활성 목록을 비워 다시 활성화할 때 이전 알림이 남지 않게 한다.
        /// </summary>
        private void ClearToasts()
        {
            if (_pool == null) return;
            for (int index = _active.Count - 1; index >= 0; index--) _pool.Release(_active[index]);
            _active.Clear();
        }

        /// <summary>
        /// toastPrefab을 HUD 전용 루트 아래에 생성한다.
        /// _maxVisible개 사전 생성에 사용할 새 WalletHudToastView를 반환한다.
        /// </summary>
        private WalletHudToastView CreateView()
        {
            return Instantiate(_toastPrefab, _toastRoot);
        }

        /// <summary>
        /// 풀에서 대여한 view를 활성화한다.
        /// 새 획득량을 표시할 수 있도록 GameObject 상태를 변경한다.
        /// </summary>
        private void ActivateView(WalletHudToastView view)
        {
            view.gameObject.SetActive(true);
        }

        /// <summary>
        /// view의 수량과 연출을 초기화한 후 비활성화한다.
        /// 재사용 항목에 이전 획득량이나 애니메이션 상태가 남지 않도록 한다.
        /// </summary>
        private void ReturnView(WalletHudToastView view)
        {
            view.ResetForPool();
            view.gameObject.SetActive(false);
        }

        /// <summary>
        /// 풀 정리 대상 view의 GameObject를 제거한다.
        /// 씬 종료 시 HUD 알림의 Unity 리소스를 해제한다.
        /// </summary>
        private void DestroyView(WalletHudToastView view)
        {
            Destroy(view.gameObject);
        }
    }
}
