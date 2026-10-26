using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Tournament
{
    public sealed class TournamentView : MonoBehaviour
    {
        private const float PANEL_SLIDE_SPEED = 2200f;

        [Header("Panels")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private Text _header;
        [SerializeField] private Text _message;
        [SerializeField] private Text _inventory;
        [SerializeField] private Text _panelTitle;
        [SerializeField] private Text _wallet;
        [SerializeField] private Text _bonusStatus;
        [Header("Shop")]
        [SerializeField] private GameObject _shopPanel;
        [SerializeField] private TournamentActionButton[] _buyButtons;
        [SerializeField] private TournamentOfferCard[] _offerCards;
        [SerializeField] private TournamentActionButton _rerollButton;
        [SerializeField] private GameObject _discardPanel;
        [Header("Shop Layout")]
        [SerializeField] private GameObject _shopFrame;
        [SerializeField] private TournamentActionButton _shopToggleButton;
        [SerializeField] private Text _shopHint;
        [SerializeField] private TournamentActionButton _shopContinueButton;
        [Header("Inventory Layout")]
        [SerializeField] private GameObject _inventoryFrame;
        [SerializeField] private TournamentActionButton _inventoryToggleButton;
        [SerializeField] private Text _inventorySummary;
        [SerializeField] private RectTransform _inventoryGrid;
        [SerializeField] private TournamentInventoryItemView _inventoryItemPrefab;
        [SerializeField] private KeyboardCustomization _shopKeyboard;
        [Header("Tooltip")]
        [SerializeField] private GameObject _tooltipPanel;
        [SerializeField] private RectTransform _tooltipRect;
        [SerializeField] private Text _tooltipText;
        [Header("Navigation")]
        [SerializeField] private TournamentActionButton _continueButton;
        [SerializeField] private TournamentActionButton _restartButton;
        [Header("Workshop Return Confirmation")]
        [SerializeField] private GameObject _returnConfirmation;
        [SerializeField] private TournamentActionButton _confirmReturnButton;
        [SerializeField] private TournamentActionButton _cancelReturnButton;
        [SerializeField] private TournamentActionButton _exitButton;
        [SerializeField] private Text _returnMessage;
        private bool _returnSubmitting;
        private bool _abandonReturn;
        private readonly Dictionary<KeyboardPartDefinition, TournamentInventoryItemView> _inventoryItems = new Dictionary<KeyboardPartDefinition, TournamentInventoryItemView>();
        private readonly Dictionary<KeyboardPartDefinition, Texture2D> _thumbnails = new Dictionary<KeyboardPartDefinition, Texture2D>();
        private readonly List<KeyboardPartDefinition> _inventoryOrder = new List<KeyboardPartDefinition>();
        private TournamentController _controller;
        private Image _background;
        private Color _backgroundColor;
        private RectTransform _panelRect;
        private RectTransform _shopFrameRect;
        private RectTransform _inventoryFrameRect;
        private float _shopOpenX;
        private float _inventoryOpenX;
        private string _runId;
        private string _repeatOpportunityRunId;
        private bool _shopExpanded = true;
        private bool _inventoryExpanded = true;

        /// <summary>controller와 상점 버튼 및 인벤토리 UI의 C# 이벤트를 연결한다. 패널 위치와 기존 배경색을 보관한다.</summary>
        public void Bind(TournamentController controller)
        {
            if (_controller != null) return;
            _controller = controller;
            _returnConfirmation.SetActive(false);
            _background = _panel.GetComponent<Image>();
            _backgroundColor = _background.color;
            _panelRect = _panel.transform as RectTransform;
            _shopFrameRect = _shopFrame.transform as RectTransform;
            _inventoryFrameRect = _inventoryFrame.transform as RectTransform;
            _shopOpenX = _shopFrameRect.anchoredPosition.x;
            _inventoryOpenX = _inventoryFrameRect.anchoredPosition.x;
            _bonusStatus.gameObject.SetActive(false);
            _discardPanel.SetActive(false);
            _tooltipPanel.SetActive(false);
            controller.Changed += Refresh;
            foreach (TournamentActionButton button in _buyButtons) button.Clicked += Buy;
            _rerollButton.Clicked += Reroll;
            _shopToggleButton.Clicked += ToggleShop;
            _inventoryToggleButton.Clicked += ToggleInventory;
            _shopContinueButton.Clicked += Continue;
            _continueButton.Clicked += Continue;
            _restartButton.Clicked += Restart;
            _confirmReturnButton.Clicked += ConfirmWorkshopReturn;
            _cancelReturnButton.Clicked += CancelWorkshopReturn;
            _exitButton.Clicked += RequestAbandonReturn;
        }

        /// <summary>현재 진행 단계와 재고·장착 상태를 읽어 화면을 갱신한다. 상점에서 두 프레임의 펼침 상태를 반영한다.</summary>
        private void Refresh()
        {
            if (_runId != _controller.PlaytestRunId)
            {
                ResetInventoryItems();
                _runId = _controller.PlaytestRunId;
                _shopExpanded = true;
                _inventoryExpanded = true;
                _returnSubmitting = false;
                _returnConfirmation.SetActive(false);
            }

            TournamentPhase phase = _controller.Phase;
            bool shop = phase == TournamentPhase.Shop;
            _exitButton.gameObject.SetActive(phase == TournamentPhase.Battle || phase == TournamentPhase.Preparing || shop);
            _exitButton.SetState("나가기", !_returnSubmitting && !_returnConfirmation.activeSelf);
            _header.text = $"TOURNAMENT {_controller.Round} / {_controller.Settings.RoundCount} · 재화 {_controller.Currency:N0}";
            _header.gameObject.SetActive(phase == TournamentPhase.Battle || phase == TournamentPhase.Preparing);
            _panel.SetActive(phase != TournamentPhase.Battle && phase != TournamentPhase.Preparing);
            _background.color = shop ? new Color(0f, 0f, 0f, 0f) : _backgroundColor;
            _panelTitle.text = shop ? "부품 상점 · 작업대" : "대전 결과";
            _wallet.text = $"재화 {_controller.Currency:N0} · 로봇 {_controller.EnemyCurrency:N0}";
            _shopFrame.SetActive(shop);
            _inventoryFrame.SetActive(shop);
            _shopToggleButton.gameObject.SetActive(shop);
            _shopToggleButton.SetState(_shopExpanded ? "<" : ">", true);
            _inventoryToggleButton.gameObject.SetActive(shop);
            _inventoryToggleButton.SetState(_inventoryExpanded ? ">" : "<", true);
            _shopPanel.SetActive(shop);
            _rerollButton.gameObject.SetActive(shop);
            _rerollButton.SetState($"리롤 {_controller.Settings.RerollCost} 재화\n남은 {_controller.RerollsRemaining}회", _controller.CanReroll);
            _message.gameObject.SetActive(!shop);
            _message.text = _controller.Message;
            _shopHint.gameObject.SetActive(shop);
            _shopHint.text = _controller.Message;
            _inventory.gameObject.SetActive(!shop);
            _inventory.color = new Color(0.25f, 0.2f, 0.18f);
            StringBuilder summary = new StringBuilder($"해금 키캡 {_controller.Inventory.Total} / {_controller.Settings.Capacity} · 직접 타격 +{_controller.Inventory.DamageBonus}\n");
            foreach (KeyValuePair<KeyboardPartDefinition, int> item in _controller.Inventory.Counts)
                summary.Append($"{item.Key.DisplayName} ×{item.Value}  ");
            _inventory.text = summary.ToString();
            _inventorySummary.text = $"해금 키캡 {_controller.Inventory.Total} / {_controller.Settings.Capacity}\n직접 타격 +{_controller.Inventory.DamageBonus}";
            _continueButton.gameObject.SetActive(!shop);
            _continueButton.SetState(phase == TournamentPhase.Finished ? "작업실로" : "계속", !_returnSubmitting);
            _shopContinueButton.gameObject.SetActive(shop);
            _shopContinueButton.SetState("다음 대전", true);
            _restartButton.gameObject.SetActive(phase == TournamentPhase.Finished);
            _restartButton.SetState("다시 시작", !_returnSubmitting);
            if (phase == TournamentPhase.Finished && _repeatOpportunityRunId != _controller.PlaytestRunId && _panel.activeInHierarchy)
            {
                _repeatOpportunityRunId = _controller.PlaytestRunId;
                Game.Logging.PlaytestResearchSession.Emit("RepeatOpportunityPresented", "WorkshopReturnOrRestart");
            }

            if (!shop)
            {
                _tooltipPanel.SetActive(false);
                return;
            }
            for (int index = 0; index < _buyButtons.Length; index++)
            {
                KeyboardPartDefinition part = _controller.GetOffer(index);
                _offerCards[index].Show(part);
                _buyButtons[index].SetState(_controller.IsPurchased(index) ? "구매 완료"
                    : part != null ? $"{part.Price} 재화" : "상품 없음", _controller.CanBuy(index));
            }
            RefreshInventoryItems();
        }

        /// <summary>구매 재고의 part마다 프리팹 아이템을 배치하고 수량·선택 상태를 갱신한다. 기존 아이템 인스턴스는 재사용한다.</summary>
        private void RefreshInventoryItems()
        {
            foreach (KeyValuePair<KeyboardPartDefinition, int> entry in _controller.Inventory.Counts)
            {
                KeyboardPartDefinition part = entry.Key;
                if (_inventoryItems.ContainsKey(part)) continue;
                TournamentInventoryItemView item = Instantiate(_inventoryItemPrefab, _inventoryGrid);
                item.name = "Inventory_" + part.Id;
                item.LeftClicked += ActivateInventoryItem;
                item.RightClicked += UnequipInventoryItem;
                item.Hovered += ShowTooltip;
                item.HoverEnded += HideTooltip;
                int index = _inventoryOrder.Count;
                RectTransform itemRect = item.GetComponent<RectTransform>();
                itemRect.anchoredPosition = new Vector2(38f + index % 6 * 66f, -43f - index / 6 * 72f);
                _inventoryOrder.Add(part);
                _inventoryItems.Add(part, item);
                _thumbnails.Add(part, TournamentPartThumbnailRenderer.Render(part, _shopKeyboard));
            }
            foreach (KeyboardPartDefinition part in _inventoryOrder)
                _inventoryItems[part].SetState(part, _controller.Inventory.GetCount(part),
                    _controller.Inventory.GetEquippedCount(part), _controller.SelectedKeycap == part, _thumbnails[part]);
        }

        /// <summary>현재 인벤토리 아이템과 생성한 썸네일을 해제한다. 새 토너먼트의 표시 상태를 빈 목록으로 만든다.</summary>
        private void ResetInventoryItems()
        {
            foreach (TournamentInventoryItemView item in _inventoryItems.Values)
                if (item != null)
                {
                    item.LeftClicked -= ActivateInventoryItem;
                    item.RightClicked -= UnequipInventoryItem;
                    item.Hovered -= ShowTooltip;
                    item.HoverEnded -= HideTooltip;
                    Destroy(item.gameObject);
                }
            foreach (Texture2D texture in _thumbnails.Values)
                if (texture != null) Destroy(texture);
            _inventoryItems.Clear();
            _inventoryOrder.Clear();
            _thumbnails.Clear();
        }

        /// <summary>part의 저장된 이름·효과와 실제 부품 종류를 툴팁에 표시한다. 마우스 위치는 Update에서 반영한다.</summary>
        private void ShowTooltip(KeyboardPartDefinition part)
        {
            _tooltipText.text = $"{part.DisplayName}\n{part.Description}\n{GetPartType(part)}";
            _tooltipPanel.SetActive(true);
        }

        /// <summary>현재 아이템 툴팁을 숨긴다. 인벤토리와 게임 상태는 변경하지 않는다.</summary>
        private void HideTooltip()
        {
            _tooltipPanel.SetActive(false);
        }

        /// <summary>part의 정의 타입을 UI용 이름으로 반환한다. 상품 설명 내용은 변경하지 않는다.</summary>
        private string GetPartType(KeyboardPartDefinition part)
        {
            if (part is KeycapDefinition) return "키캡";
            if (part is SwitchDefinition) return "스위치";
            if (part is PlateDefinition) return "보강판";
            return "하우징";
        }

        /// <summary>index 슬롯의 구매를 요청한다. 성공 시 컨트롤러가 인벤토리 화면을 갱신한다.</summary>
        private void Buy(int index) { _controller.Buy(index); }

        /// <summary>index는 사용하지 않고 유료 상품 재추첨을 요청한다. 실제 비용 검사는 컨트롤러가 수행한다.</summary>
        private void Reroll(int index) { _controller.Reroll(); }

        /// <summary>index는 사용하지 않고 종료 단계에서는 복귀 확인창만 연다. 다른 단계의 진행은 기존 컨트롤러에 전달한다.</summary>
        private void Continue(int index)
        {
            if (_returnSubmitting || _returnConfirmation.activeSelf) return;
            if (_controller.Phase == TournamentPhase.Finished)
            {
                OpenReturnConfirmation(false);
                return;
            }
            _controller.Continue();
        }

        /// <summary>index는 사용하지 않고 전투·상점 단계의 중도 포기 확인을 요청한다. 기존 결과 화면의 정상 복귀는 유지한다.</summary>
        private void RequestAbandonReturn(int index)
        {
            if (_returnSubmitting || _returnConfirmation.activeSelf || TournamentController.IsReturnPaused) return;
            TournamentPhase phase = _controller.Phase;
            if (phase == TournamentPhase.Battle || phase == TournamentPhase.Preparing || phase == TournamentPhase.Shop)
                OpenReturnConfirmation(true);
        }

        /// <summary>abandon으로 정상 복귀와 포기 문구를 선택해 기존 확인창을 연다. 정지에 성공한 경우에만 표시하고 전투 데이터는 변경하지 않는다.</summary>
        private void OpenReturnConfirmation(bool abandon)
        {
            if (!_controller.PauseForWorkshopReturn()) return;
            _abandonReturn = abandon;
            _returnMessage.text = "전투에서 해금·강화한 능력과 전투용 재화는 초기화됩니다.\n키보드는 전투에 들어오기 전 모습으로 돌아갑니다."
                + (abandon ? "\n중도에 나가면 이번 토너먼트의 골드 보상을 받을 수 없습니다." : "");
            _tooltipPanel.SetActive(false);
            _returnConfirmation.SetActive(true);
            _confirmReturnButton.SetState("확인", true);
            _cancelReturnButton.SetState("뒤로", true);
            _exitButton.SetState("나가기", false);
        }

        /// <summary>index는 사용하지 않고 확인창의 복귀를 한 번 확정한다. 모든 복귀 버튼을 잠근 뒤 기존 복귀 처리를 호출한다.</summary>
        private void ConfirmWorkshopReturn(int index)
        {
            if (_returnSubmitting || !_returnConfirmation.activeSelf) return;
            _returnSubmitting = true;
            _confirmReturnButton.SetState("확인", false);
            _cancelReturnButton.SetState("뒤로", false);
            _continueButton.SetState("작업실로", false);
            _restartButton.SetState("다시 시작", false);
            if (_abandonReturn) _controller.AbandonToWorkshop();
            else _controller.ReturnToWorkshop();
        }

        /// <summary>index는 사용하지 않고 복귀 확인창만 닫는다. 토너먼트 상태와 키보드 원본은 변경하지 않는다.</summary>
        private void CancelWorkshopReturn(int index)
        {
            if (_returnSubmitting) return;
            _returnConfirmation.SetActive(false);
            _controller.ReleaseReturnPause(false);
            _exitButton.SetState("나가기", true);
        }

        /// <summary>index는 사용하지 않고 종료된 토너먼트 재시작을 요청한다. 원본 작업실 데이터는 유지한다.</summary>
        private void Restart(int index)
        {
            if (_returnSubmitting || _returnConfirmation.activeSelf) return;
            _controller.Restart();
        }

        /// <summary>index는 사용하지 않고 왼쪽 상점 프레임의 펼침 상태를 바꾼다. 아이템 거래 상태는 유지한다.</summary>
        private void ToggleShop(int index)
        {
            _shopExpanded = !_shopExpanded;
            _tooltipPanel.SetActive(false);
            Refresh();
        }

        /// <summary>index는 사용하지 않고 오른쪽 인벤토리 프레임의 펼침 상태를 바꾼다. 재고와 선택 상태는 유지한다.</summary>
        private void ToggleInventory(int index)
        {
            _inventoryExpanded = !_inventoryExpanded;
            _tooltipPanel.SetActive(false);
            Refresh();
        }

        /// <summary>part 아이템의 좌클릭을 컨트롤러에 전달한다. 장착 결과는 Changed 이벤트로 갱신한다.</summary>
        private void ActivateInventoryItem(KeyboardPartDefinition part) { _controller.ActivateInventoryItem(part); }

        /// <summary>part 아이템의 우클릭을 컨트롤러에 전달한다. 마지막 키캡 또는 전체 부품의 해제 결과를 갱신한다.</summary>
        private void UnequipInventoryItem(KeyboardPartDefinition part) { _controller.UnequipInventoryItem(part); }

        /// <summary>패널 너비와 펼침 상태로 양쪽 프레임의 목표 위치를 계산한다. 프레임에 붙은 토글 버튼도 함께 이동한다.</summary>
        private void SlideFrames()
        {
            float panelHalfWidth = _panelRect.rect.width * 0.5f;
            float shopTarget = _shopExpanded ? _shopOpenX : -panelHalfWidth - _shopFrameRect.rect.width * 0.5f;
            float inventoryTarget = _inventoryExpanded ? _inventoryOpenX : panelHalfWidth + _inventoryFrameRect.rect.width * 0.5f;
            Vector2 shopPosition = _shopFrameRect.anchoredPosition;
            Vector2 inventoryPosition = _inventoryFrameRect.anchoredPosition;
            shopPosition.x = Mathf.MoveTowards(shopPosition.x, shopTarget, PANEL_SLIDE_SPEED * Time.deltaTime);
            inventoryPosition.x = Mathf.MoveTowards(inventoryPosition.x, inventoryTarget, PANEL_SLIDE_SPEED * Time.deltaTime);
            _shopFrameRect.anchoredPosition = shopPosition;
            _inventoryFrameRect.anchoredPosition = inventoryPosition;
        }

        void Update()
        {
            if (_shopFrame.activeInHierarchy) SlideFrames();
            if (!_tooltipPanel.activeSelf || Mouse.current == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_panelRect, InGameCursor.Position, null, out Vector2 point);
            Vector2 size = _tooltipRect.rect.size;
            Rect bounds = _panelRect.rect;
            _tooltipRect.anchoredPosition = new Vector2(
                Mathf.Clamp(point.x + 18f, bounds.xMin, bounds.xMax - size.x),
                Mathf.Clamp(point.y - 18f, bounds.yMin + size.y, bounds.yMax));
        }

        void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.ReleaseReturnPause(true);
                _controller.Changed -= Refresh;
                foreach (TournamentActionButton button in _buyButtons) button.Clicked -= Buy;
                _rerollButton.Clicked -= Reroll;
                _shopToggleButton.Clicked -= ToggleShop;
                _inventoryToggleButton.Clicked -= ToggleInventory;
                _shopContinueButton.Clicked -= Continue;
                _continueButton.Clicked -= Continue;
                _restartButton.Clicked -= Restart;
                _confirmReturnButton.Clicked -= ConfirmWorkshopReturn;
                _cancelReturnButton.Clicked -= CancelWorkshopReturn;
                _exitButton.Clicked -= RequestAbandonReturn;
            }
            ResetInventoryItems();
        }
    }
}
