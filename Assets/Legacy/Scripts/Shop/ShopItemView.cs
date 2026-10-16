using System;

using UnityEngine;
using UnityEngine.UI;

using Game.Upgrades;

namespace Game.Shop
{
    public sealed class ShopItemView : MonoBehaviour
    {
        private const int NAME_FONT_SIZE = 24;

        [Header("UI References")]
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Text _levelText;
        [SerializeField] private Text _costText;
        [SerializeField] private Text _buttonText;
        [SerializeField] private Button _purchaseButton;

        [Header("Runtime State")]
        private UpgradeDefinition _definition;
        private UpgradeService _upgrades;

        public event Action<UpgradeId> PurchaseRequested;

        void Awake()
        {
            LayoutElement layout = GetComponent<LayoutElement>();
            float prefabHeight = ((RectTransform)transform).rect.height;
            // 부모 레이아웃이 적용되기 전에 프리팹의 높이를 최소 높이로 보존한다.
            layout.minHeight = Mathf.Max(layout.minHeight, prefabHeight);
        }

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
        /// 이름만 24pt로 표시하고 모든 강화의 현재·다음 효과, 레벨, 다음 가격과 구매·승급 버튼의 상태를 변경한다.
        /// </summary>
        public void Refresh()
        {
            _nameText.fontSize = NAME_FONT_SIZE;
            _nameText.resizeTextForBestFit = false;
            _nameText.text = _definition.DisplayName;
            int level = _upgrades.GetLevel(_definition.Id);
            _descriptionText.text = UpgradeDescriptionFormatter.Format(_definition, level);
            bool isMaxLevel = level >= _definition.MaxLevel;
            PurchaseResult purchaseStatus = _upgrades.GetPurchaseStatus(_definition.Id);
            bool requiresAreaSmash = purchaseStatus == PurchaseResult.RequiresAreaSmash;

            _levelText.text = $"Lv {level}/{_definition.MaxLevel}";
            _costText.text = isMaxLevel ? string.Empty : requiresAreaSmash ? "Area Smash Lv 1" : $"{_definition.GetCost(level):N0}";
            _buttonText.text = isMaxLevel ? "MAX" : requiresAreaSmash ? "LOCKED" : "Buy";
            _purchaseButton.interactable = purchaseStatus == PurchaseResult.Success;

            if (_definition.Id == UpgradeId.HandPower)
            {
                _levelText.text = $"Lv {_definition.GetHandStageLevel(level)}/{_definition.GetHandStageMaxLevel(level)}";
                _nameText.text = _definition.GetHandName(level);
                if (!isMaxLevel && _definition.IsHandPromotion(level + 1))
                    _buttonText.text = "Promote";
            }
            else if (_definition.Id == UpgradeId.AutoClick)
            {
                int promotion = _definition.AutoPromotionLevel;
                int refillStart = _definition.AutoRefillStartLevel;
                _nameText.text = _definition.GetAutoAttackName(level);

                if (level >= refillStart)
                {
                    int stageLevel = level - refillStart + 1;
                    int stageMax = _definition.MaxLevel - refillStart + 1;

                    _levelText.text = $"Lv {stageLevel}/{stageMax}";
                }
                else
                {
                    int stageLevel = level >= promotion ? level - promotion + 1 : level;
                    int stageMax = level >= promotion ? refillStart - promotion : promotion - 1;

                    _levelText.text = $"Lv {stageLevel}/{stageMax}";

                    if (level + 1 == refillStart)
                    {
                        _buttonText.text = "Promote";
                    }
                    else if (!isMaxLevel && level + 1 == promotion)
                    {
                        _buttonText.text = "Promote";
                    }
                }
            }
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
