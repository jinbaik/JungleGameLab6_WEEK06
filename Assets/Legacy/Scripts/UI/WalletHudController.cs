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
        private const float REWARD_AGGREGATION_INTERVAL = 0.2f;
        private const float TOAST_ROW_PADDING = 8f;

        [Header("References")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private Text _balanceLabel;
        [SerializeField] private RectTransform _toastRoot;
        [SerializeField] private WalletHudToastView _toastPrefab;

        [Header("Toast Pool")]
        [SerializeField, Range(1, 8)] private int _maxVisible = 4;
        private ObjectPool<WalletHudToastView> _pool;
        private List<WalletHudToastView> _active;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float _duration = 1.2f;
        [SerializeField, Min(0f)] private float _riseDistance = 14f;
        [SerializeField, Range(0f, 0.95f)] private float _fadeStart = 0.55f;
        [SerializeField, Min(48f)] private float _rowSpacing = 56f;
        [SerializeField] private Color _toastColor = new Color(1f, 0.85f, 0.25f);

        [Header("Reward Aggregation")]
        private long _pendingNormalAmount;
        private long _pendingDoubleAmount;
        private float _batchDeadline;
        private bool _isBatchPending;

        [Header("Runtime State")]
        private Wallet _wallet;

        void Awake()
        {
            _active = new List<WalletHudToastView>(_maxVisible);
            _pool = new ObjectPool<WalletHudToastView>(CreateView, ActivateView, ReturnView, DestroyView, false, _maxVisible, _maxVisible);
            for (int index = 0; index < _maxVisible; index++) _active.Add(_pool.Get());
            ClearToasts();
        }

        void OnEnable()
        {
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
            float rowSpacing = GetRowSpacing();
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                WalletHudToastView view = _active[index];
                if (view.Tick(deltaTime, _active.Count - 1 - index, rowSpacing)) continue;
                _active.RemoveAt(index);
                _pool.Release(view);
            }

            if (_isBatchPending && Time.unscaledTime >= _batchDeadline)
                FlushPendingGains();
        }

        void OnDisable()
        {
            if (_wallet != null)
            {
                _wallet.BalanceChanged -= OnBalanceChanged;
                _wallet.IncomeReceived -= OnIncomeReceived;
            }
            ClearToasts();
        }

        void OnDestroy()
        {
            _pool?.Dispose();
        }

        /// <summary>
        /// 현재 Session 지갑의 잔액 변경과 수입 이벤트를 구독한다.
        /// 초기 잔액을 HUD에 반영하되 이미 보유한 금액에 대한 획득 알림은 생성하지 않는다.
        /// </summary>
        private void BindWallet()
        {
            _wallet.BalanceChanged += OnBalanceChanged;
            _wallet.IncomeReceived += OnIncomeReceived;
            RefreshBalance(_wallet.Balance);
        }

        /// <summary>
        /// balance를 실제 지갑 잔액으로 즉시 표시한다.
        /// 잔액 변경 이벤트로 숫자를 갱신하며 획득 알림은 수입 이벤트에서 별도로 합산한다.
        /// </summary>
        private void OnBalanceChanged(long balance)
        {
            RefreshBalance(balance);
        }

        /// <summary>
        /// 지급된 amount를 isDoubleReward에 따라 일반 또는 두 배 보상 합계에 더한다.
        /// 첫 수입부터 0.2초의 마감 시각을 확정하고 이후 수입은 시각을 연장하지 않으며 만료된 합계는 먼저 표시한다.
        /// </summary>
        private void OnIncomeReceived(long amount, bool isDoubleReward)
        {
            if (_isBatchPending && Time.unscaledTime >= _batchDeadline)
                FlushPendingGains();

            if (!_isBatchPending)
            {
                _isBatchPending = true;
                _batchDeadline = Time.unscaledTime + REWARD_AGGREGATION_INTERVAL;
            }

            if (isDoubleReward)
                _pendingDoubleAmount = checked(_pendingDoubleAmount + amount);
            else
                _pendingNormalAmount = checked(_pendingNormalAmount + amount);
        }

        /// <summary>
        /// 대기 중인 일반·두 배 최종 지급액을 각각 하나의 HUD 알림으로 표시한다.
        /// 저장한 합산액을 사용하며 마감 시각과 대기 상태를 먼저 비워 다음 수입을 새 주기로 처리한다.
        /// </summary>
        private void FlushPendingGains()
        {
            long normalAmount = _pendingNormalAmount;
            long doubleAmount = _pendingDoubleAmount;
            ClearPendingGains();

            if (normalAmount > 0) ShowGain(normalAmount, false);
            if (doubleAmount > 0) ShowGain(doubleAmount, true);
        }

        /// <summary>
        /// 입력값 없이 대기 중인 보상 합계와 마감 시각을 초기화한다.
        /// 일반·두 배 합산액을 0으로 설정하고 합산 대기 상태를 해제한다.
        /// </summary>
        private void ClearPendingGains()
        {
            _pendingNormalAmount = 0;
            _pendingDoubleAmount = 0;
            _batchDeadline = 0f;
            _isBatchPending = false;
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
        /// 합산한 amount와 isDoubleReward를 하나의 획득 알림으로 표시한다.
        /// 풀에서 알림을 대여하여 지급액과 강조 상태를 전달하고 용량이 가득 차면 가장 오래된 알림을 재사용한다.
        /// </summary>
        private void ShowGain(long amount, bool isDoubleReward)
        {
            if (_active.Count == _maxVisible)
            {
                WalletHudToastView oldest = _active[0];
                _active.RemoveAt(0);
                _pool.Release(oldest);
            }
            WalletHudToastView view = _pool.Get();
            view.Begin(amount, _duration, _riseDistance, _fadeStart, _toastColor, isDoubleReward);
            _active.Add(view);
            float rowSpacing = GetRowSpacing();
            for (int index = 0; index < _active.Count; index++)
                _active[index].Tick(0f, _active.Count - 1 - index, rowSpacing);
        }

        /// <summary>
        /// 활성 토스트의 높이와 상승 거리로 겹침을 피할 공통 행 간격을 계산한다.
        /// _rowSpacing과 표시 높이를 사용해 기본 간격 이상의 값을 반환하며 표시 상태는 변경하지 않는다.
        /// </summary>
        private float GetRowSpacing()
        {
            float spacing = _rowSpacing;
            for (int index = 0; index < _active.Count; index++)
                spacing = Mathf.Max(spacing, _active[index].DisplayHeight + _riseDistance + TOAST_ROW_PADDING);
            return spacing;
        }

        /// <summary>
        /// 표시 중인 HUD 알림을 풀로 반환하고 대기 중인 보상 합계도 초기화한다.
        /// 활성 목록과 합산 대기 상태를 비워 다시 활성화할 때 이전 알림이 남지 않게 한다.
        /// </summary>
        private void ClearToasts()
        {
            ClearPendingGains();
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
