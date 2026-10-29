using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using Game.Session;
using Game.Upgrades;

namespace Game.Shop
{
    public sealed class ShopView : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GameSession _gameSession;
        [SerializeField] private ShopItemView _itemPrefab;

        [Header("UI References")]
        [SerializeField] private GameObject _shopPanel;
        [SerializeField] private Transform _itemRoot;
        [SerializeField] private Text _balanceText;
        [SerializeField] private Text _toggleText;
        [SerializeField] private Button _toggleButton;
        [Tooltip("바탕화면 팝업에서는 버튼을 닫기 전용으로 사용합니다.")]
        [SerializeField] private bool _useWindowCloseButton;

        [Header("Input")]
        private bool _isInputEnabled = true;

        [Header("Gameplay")]
        [SerializeField] private KeyboardInteractionController _interactionController;

        [Header("Runtime State")]
        private readonly List<ShopItemView> _items = new List<ShopItemView>();

        public bool IsOpen => _shopPanel.activeSelf;

        public event Action<bool> OpenStateChanged;

        void Start()
        {
            IReadOnlyList<UpgradeDefinition> definitions = _gameSession.UpgradeDefinitions;

            for (int i = 0; i < definitions.Count; i++)
            {
                ShopItemView item = Instantiate(_itemPrefab, _itemRoot);
                item.Initialize(definitions[i], _gameSession.Upgrades);
                item.PurchaseRequested += OnPurchaseRequested;
                _items.Add(item);
            }

            _gameSession.Wallet.BalanceChanged += OnBalanceChanged;
            _gameSession.Upgrades.LevelChanged += OnLevelChanged;
            _toggleButton.onClick.AddListener(OnWindowButtonClicked);
            OnBalanceChanged(_gameSession.Wallet.Balance);
            SetOpen(IsOpen);
        }

        void OnDestroy()
        {
            _gameSession.Wallet.BalanceChanged -= OnBalanceChanged;
            _gameSession.Upgrades.LevelChanged -= OnLevelChanged;
            _toggleButton.onClick.RemoveListener(OnWindowButtonClicked);

            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].PurchaseRequested -= OnPurchaseRequested;
            }
        }

        /// <summary>
        /// 현재 패널의 표시 상태를 사용하여 상점을 열거나 닫는다.
        /// 상점 입력이 허용된 경우 재화와 강화 레벨을 유지하면서 패널 표시를 변경한다.
        /// </summary>
        public void Toggle()
        {
            if (_isInputEnabled)
            {
                SetOpen(!IsOpen);
            }
        }

        /// <summary>
        /// isOpen에 따라 상점 패널과 버튼 문구를 변경한다.
        /// 연결된 플레이 컨트롤러가 진입을 허용할 때만 상점을 열고,
        /// 표시 상태가 변경되면 OpenStateChanged로 알린다.
        /// </summary>
        public void SetOpen(bool isOpen)
        {
            bool wasOpen = IsOpen;

            if (isOpen && ((_useWindowCloseButton && !_isInputEnabled)
                || (_interactionController != null && !_interactionController.CanEnterShop)))
            {
                return;
            }

            _shopPanel.SetActive(isOpen);
            _toggleText.text = _useWindowCloseButton ? "닫기" : isOpen ? "닫기 [Tab]" : "상점 [Tab]";

            if (isOpen)
            {
                RefreshItems();
            }

            if (wasOpen != isOpen)
            {
                OpenStateChanged?.Invoke(isOpen);
            }
        }

        /// <summary>
        /// isEnabled로 상점 열기, 구매와 창 버튼 입력 허용 여부를 설정한다.
        /// 입력 상태와 버튼 상태를 변경하여 모니터 조작 종료 및 연출 중의 요청을 차단한다.
        /// </summary>
        public void SetInputEnabled(bool isEnabled)
        {
            _isInputEnabled = isEnabled;
            _toggleButton.interactable = isEnabled;
        }

        /// <summary>
        /// balance를 사용하여 보유 재화와 모든 강화 항목의 구매 가능 상태를 갱신한다.
        /// 화면 표시만 변경하며 지갑 잔액은 수정하지 않는다.
        /// </summary>
        private void OnBalanceChanged(long balance)
        {
            _balanceText.text = $"보유 재화: {balance:N0}";
            RefreshItems();
        }

        /// <summary>
        /// id와 level에 해당하는 강화 상태 변경을 받아 강화 목록을 갱신한다.
        /// 화면에서 현재 레벨과 다음 가격 및 구매 가능 여부를 다시 표시한다.
        /// </summary>
        private void OnLevelChanged(UpgradeId id, int level)
        {
            RefreshItems();
        }

        /// <summary>
        /// id로 선택한 강화의 구매를 서비스에 요청한다.
        /// 재화와 레벨 변경은 서비스에서 처리하고 변경 이벤트로 화면을 갱신한다.
        /// </summary>
        private void OnPurchaseRequested(UpgradeId id)
        {
            if (_isInputEnabled && IsOpen)
                _gameSession.Upgrades.TryPurchase(id);
        }

        /// <summary>
        /// 창 버튼의 클릭을 받아 입력이 허용된 상점의 표시 상태를 변경한다.
        /// 팝업에서는 닫기만 수행하고 기존 전용 씬에서는 Toggle 동작을 유지한다.
        /// </summary>
        private void OnWindowButtonClicked()
        {
            if (!_isInputEnabled)
                return;
            if (_useWindowCloseButton)
                SetOpen(false);
            else
                Toggle();
        }

        /// <summary>
        /// 생성된 강화 항목 목록을 사용하여 모든 항목의 표시를 갱신한다.
        /// 각 항목의 레벨, 가격 및 구매 버튼 상태를 변경한다.
        /// </summary>
        private void RefreshItems()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Refresh();
            }
        }
    }
}
