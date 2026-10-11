using System;

using UnityEngine;
using UnityEngine.UI;

using Game.Upgrades;
using KeyboardModeling;

namespace Game.Shop
{
    public sealed class ShopItemView : MonoBehaviour
    {
        private const int DESCRIPTION_FONT_SIZE = 18;
        private const float HORIZONTAL_PADDING = 14f;
        private const float TOP_PADDING = 8f;
        private const float SECTION_SPACING = 8f;
        private const float FOOTER_HEIGHT = 40f;
        private const float BOTTOM_PADDING = 14f;

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
        private RectTransform _itemRect;
        private LayoutElement _layoutElement;
        private float _minimumHeight;
        private bool _isUpdatingLayout;

        public event Action<UpgradeId> PurchaseRequested;

        void OnDestroy()
        {
            _purchaseButton.onClick.RemoveListener(OnPurchaseClicked);
        }

        void OnRectTransformDimensionsChange()
        {
            if (_definition != null && !_isUpdatingLayout)
            {
                ConfigureLayout();
            }
        }

        /// <summary>
        /// definition과 upgrades를 사용하여 강화 제목, 설명과 구매 상태를 표시한다.
        /// 구매 버튼을 연결하고 화면에서 사용할 강화 설정과 서비스를 보관한다.
        /// </summary>
        public void Initialize(UpgradeDefinition definition, UpgradeService upgrades)
        {
            _itemRect = (RectTransform)transform;
            _layoutElement = GetComponent<LayoutElement>();
            _minimumHeight = Mathf.Max(_itemRect.rect.height, _layoutElement.minHeight, _layoutElement.preferredHeight);
            _definition = definition;
            _upgrades = upgrades;
            _nameText.text = definition.DisplayName;
            _descriptionText.fontSize = DESCRIPTION_FONT_SIZE;
            _descriptionText.resizeTextForBestFit = false;
            _purchaseButton.onClick.AddListener(OnPurchaseClicked);
            Refresh();
        }

        /// <summary>
        /// 보관 중인 강화 설정과 현재 레벨 및 잔액을 사용하여 화면을 갱신한다.
        /// 현재·다음 효과 설명, 항목 높이, 레벨, 가격과 구매 버튼의 상태를 변경한다.
        /// </summary>
        public void Refresh()
        {
            int level = _upgrades.GetLevel(_definition.Id);
            bool isMaxLevel = level >= _definition.MaxLevel;
            PurchaseResult purchaseStatus = _upgrades.GetPurchaseStatus(_definition.Id);
            bool requiresAreaSmash = purchaseStatus == PurchaseResult.RequiresAreaSmash;

            _levelText.text = $"Lv {level}/{_definition.MaxLevel}";
            _costText.text = isMaxLevel ? string.Empty : requiresAreaSmash ? "Area Smash Lv 1" : $"{_definition.GetCost(level):N0}";
            _buttonText.text = isMaxLevel ? "MAX" : requiresAreaSmash ? "LOCKED" : "Buy";
            _purchaseButton.interactable = purchaseStatus == PurchaseResult.Success;

            _descriptionText.text = FormatDescription(level, isMaxLevel);
            ConfigureLayout();
        }

        /// <summary>
        /// level과 isMaxLevel로 현재 효과와 구매 후 다음 효과의 설명을 만든다.
        /// 최대 레벨에서는 현재 효과만 반환하며 품질과 피버의 적용 시점도 표시한다.
        /// </summary>
        private string FormatDescription(int level, bool isMaxLevel)
        {
            if (_definition.Id == UpgradeId.RareKeycapQuality)
            {
                string distribution = $"Current: {FormatRarityDistribution(level)}";
                if (!isMaxLevel)
                {
                    distribution += $"\nNext: {FormatRarityDistribution(level + 1)}";
                }
                return distribution + "\n(Next keyboard)";
            }

            string effect = FormatUpgradeEffect(level);
            if (!isMaxLevel)
            {
                effect += $" -> {FormatUpgradeEffect(level + 1)}";
            }
            if (_definition.Id == UpgradeId.FeverDuration || _definition.Id == UpgradeId.FeverDamageMultiplier)
            {
                effect += " (Next fever)";
            }
            return effect;
        }

        /// <summary>
        /// level과 상품 종류에 해당하는 강화 수치를 표시 문자열로 변환한다.
        /// 추가 피해, 반경, 자동 공격력·속도 또는 피버 효과를 반환하며 강화 상태는 변경하지 않는다.
        /// </summary>
        private string FormatUpgradeEffect(int level)
        {
            switch (_definition.Id)
            {
                case UpgradeId.SmashDamage:
                case UpgradeId.AreaDamage:
                    return $"Damage bonus +{_definition.GetDamageBonus(level)}";
                case UpgradeId.AreaSmash:
                    return $"Radius {_definition.GetAreaRadius(level):0.##}";
                case UpgradeId.AutoClick:
                    return $"Damage {_definition.GetAutoAttackDamage(level)} / {_definition.GetAutoAttackSpeed(level):0.##} hits/s";
                case UpgradeId.FeverDuration:
                case UpgradeId.FeverDamageMultiplier:
                    return FormatFeverEffect(level);
                default:
                    return _definition.Description;
            }
        }

        /// <summary>
        /// level과 현재 피버 강화 정의로 상점에 표시할 지속시간 또는 피해 배율을 만든다.
        /// 다음 피버부터 적용되는 효과 문구를 반환하며 강화 상태는 변경하지 않는다.
        /// </summary>
        private string FormatFeverEffect(int level)
        {
            return _definition.Id == UpgradeId.FeverDuration
                ? $"{_definition.GetFeverDuration(level):0.#}s"
                : $"x{_definition.GetFeverDamageMultiplier(level):0.0}";
        }

        /// <summary>
        /// 현재 항목 너비와 제목·설명 내용을 사용하여 줄바꿈에 필요한 높이를 계산한다.
        /// 제목 폰트는 유지하고 텍스트 영역, 구매 정보 위치와 항목의 최소·선호 높이를 변경한다.
        /// </summary>
        private void ConfigureLayout()
        {
            _isUpdatingLayout = true;
            float textWidth = Mathf.Max(1f, _itemRect.rect.width - HORIZONTAL_PADDING * 2f);
            float titleHeight = GetTextHeight(_nameText, textWidth);
            float descriptionHeight = GetTextHeight(_descriptionText, textWidth);
            float descriptionTop = TOP_PADDING + titleHeight + SECTION_SPACING;
            float footerTop = descriptionTop + descriptionHeight + SECTION_SPACING;
            float itemHeight = Mathf.Max(_minimumHeight, footerTop + FOOTER_HEIGHT + BOTTOM_PADDING);

            SetTextRect(_nameText, TOP_PADDING, titleHeight);
            SetTextRect(_descriptionText, descriptionTop, descriptionHeight);
            _levelText.rectTransform.anchoredPosition = new Vector2(_levelText.rectTransform.anchoredPosition.x, -footerTop - 7f);
            _costText.rectTransform.anchoredPosition = new Vector2(_costText.rectTransform.anchoredPosition.x, -footerTop);
            RectTransform buttonRect = (RectTransform)_purchaseButton.transform;
            buttonRect.anchoredPosition = new Vector2(buttonRect.anchoredPosition.x, -footerTop);
            if (!Mathf.Approximately(_layoutElement.preferredHeight, itemHeight))
            {
                _layoutElement.minHeight = itemHeight;
                _layoutElement.preferredHeight = itemHeight;
            }
            _itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, itemHeight);
            _isUpdatingLayout = false;
        }

        /// <summary>
        /// text의 현재 폰트와 width를 사용하여 줄바꿈을 포함한 표시 높이를 측정한다.
        /// 폰트 크기를 줄이지 않고 글자가 잘리지 않도록 여백을 더한 높이를 반환한다.
        /// </summary>
        private float GetTextHeight(Text text, float width)
        {
            TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(width, 0f));
            float preferredHeight = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
            return Mathf.Ceil(Mathf.Max(text.fontSize, preferredHeight)) + 8f;
        }

        /// <summary>
        /// text, top과 height를 사용하여 항목 안의 텍스트 영역을 배치한다.
        /// 좌우 여백을 유지하면서 위쪽 위치와 표시 높이를 변경한다.
        /// </summary>
        private void SetTextRect(Text text, float top, float height)
        {
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(HORIZONTAL_PADDING, -top - height);
            textRect.offsetMax = new Vector2(-HORIZONTAL_PADDING, -top);
        }

        /// <summary>
        /// level에 해당하는 일반·희귀·영웅·전설 등급의 최종 확률을 조회한다.
        /// 각 확률을 백분율 표시 문자열로 반환하며 구매 상태는 변경하지 않는다.
        /// </summary>
        private string FormatRarityDistribution(int level)
        {
            KeycapRarityDistribution distribution = _definition.GetRarityDistribution(level);
            return $"Common {distribution.GetProbability(KeycapRarity.Common) * 100f:0.##}% / Rare {distribution.GetProbability(KeycapRarity.Rare) * 100f:0.##}% / " +
                $"Epic {distribution.GetProbability(KeycapRarity.Epic) * 100f:0.##}% / Legendary {distribution.GetProbability(KeycapRarity.Legendary) * 100f:0.##}%";
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
