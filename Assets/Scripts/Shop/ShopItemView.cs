using System;

using UnityEngine;
using UnityEngine.UI;

using Game.Upgrades;

namespace Game.Shop
{
    public sealed class ShopItemView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _levelText;
        [SerializeField] private Text _costText;
        [SerializeField] private Text _buttonText;
        [SerializeField] private Button _purchaseButton;

        [Header("Runtime State")]
        private UpgradeDefinition _definition;
        private UpgradeService _upgrades;

        public event Action<UpgradeId> PurchaseRequested;

        void OnDestroy()
        {
            _purchaseButton.onClick.RemoveListener(OnPurchaseClicked);
        }

        /// <summary>
        /// definition과 upgrades를 사용하여 강화 이름과 구매 상태를 표시한다.
        /// 구매 버튼을 연결하고 화면에서 사용할 강화 설정과 서비스를 보관한다.
        /// </summary>
        public void Initialize(UpgradeDefinition definition, UpgradeService upgrades)
        {
            _definition = definition;
            _upgrades = upgrades;
            _nameText.text = definition.DisplayName;
            _purchaseButton.onClick.AddListener(OnPurchaseClicked);
            Refresh();
        }

        /// <summary>
        /// 보관 중인 강화 설정과 현재 레벨 및 잔액을 사용하여 화면을 갱신한다.
        /// 레벨, 다음 가격, 구매 버튼의 표시와 활성 상태를 변경한다.
        /// </summary>
        public void Refresh()
        {
            int level = _upgrades.GetLevel(_definition.Id);
            bool isMaxLevel = level >= _definition.MaxLevel;
            bool canPurchase = _upgrades.CanPurchase(_definition.Id);

            _levelText.text = $"Lv {level}/{_definition.MaxLevel}";
            _costText.text = isMaxLevel ? string.Empty : $"{_definition.GetCost(level):N0}";
            _buttonText.text = isMaxLevel ? "MAX" : "Buy";
            _purchaseButton.interactable = canPurchase;
        }

        /// <summary>
        /// 구매 버튼에서 선택한 강화의 ID를 PurchaseRequested로 전달한다.
        /// 보관 중인 강화 설정을 사용하며 재화와 레벨은 직접 변경하지 않는다.
        /// </summary>
        private void OnPurchaseClicked()
        {
            PurchaseRequested?.Invoke(_definition.Id);
        }
    }
}
