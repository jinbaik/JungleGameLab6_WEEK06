using System.Text;

using UnityEngine;
using UnityEngine.UI;

namespace Game.Tournament
{
    public sealed class TournamentView : MonoBehaviour
    {
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

        [Header("Discard")]
        [SerializeField] private GameObject _discardPanel;
        [SerializeField] private TournamentActionButton[] _discardButtons;
        [SerializeField] private TournamentActionButton _confirmButton;
        [SerializeField] private TournamentActionButton _cancelButton;

        [Header("Navigation")]
        [SerializeField] private TournamentActionButton _continueButton;
        [SerializeField] private TournamentActionButton _restartButton;
        private TournamentController _controller;

        void Update()
        {
            if (_controller == null) return;
            float remaining = _controller.PlayerBonusRemaining;
            _bonusStatus.gameObject.SetActive(_controller.IsBattleActive && remaining > 0f);
            if (remaining > 0f) _bonusStatus.text = $"점수 ×2   {remaining:0.0}초";
        }

        /// <summary>controller의 상태와 버튼의 C# 이벤트를 연결하고 현재 토너먼트 화면을 표시한다.</summary>
        public void Bind(TournamentController controller)
        {
            _controller = controller;
            controller.Changed += Refresh;
            foreach (TournamentActionButton button in _buyButtons) button.Clicked += Buy;
            _rerollButton.Clicked += Reroll;
            foreach (TournamentActionButton button in _discardButtons) button.Clicked += Discard;
            _confirmButton.Clicked += Confirm;
            _cancelButton.Clicked += Cancel;
            _continueButton.Clicked += Continue;
            _restartButton.Clicked += Restart;
        }

        /// <summary>입력값 없이 현재 진행, 잔액, 상품과 폐기 선택을 읽어 연결된 UI 상태를 갱신한다.</summary>
        private void Refresh()
        {
            TournamentPhase phase = _controller.Phase;
            _header.text = $"TOURNAMENT  {_controller.Round} / {_controller.Settings.RoundCount}    재화 {_controller.Currency:N0}";
            _header.gameObject.SetActive(phase == TournamentPhase.Battle || phase == TournamentPhase.Preparing);
            _panel.SetActive(phase != TournamentPhase.Battle && phase != TournamentPhase.Preparing);
            _panelTitle.text = phase == TournamentPhase.Shop ? "키캡 상점" : phase == TournamentPhase.Discard ? "키캡 보유 정리" : "대전 결과";
            _wallet.text = $"재화  {_controller.Currency:N0}";
            _shopPanel.SetActive(phase == TournamentPhase.Shop);
            _rerollButton.gameObject.SetActive(phase == TournamentPhase.Shop);
            _rerollButton.SetState($"리롤\n{_controller.Settings.RerollCost} 재화\n{_controller.RerollsRemaining} / {_controller.Settings.RerollLimit}회", _controller.CanReroll);
            _discardPanel.SetActive(phase == TournamentPhase.Discard);
            _message.text = _controller.Message;
            _continueButton.gameObject.SetActive(phase != TournamentPhase.Discard);
            _continueButton.SetState(phase == TournamentPhase.Shop ? "다음 대전" : phase == TournamentPhase.Finished ? "작업실로" : "계속", true);
            _restartButton.gameObject.SetActive(phase == TournamentPhase.Finished);
            _restartButton.SetState("다시 시작", true);
            TournamentInventory inventory = phase == TournamentPhase.Discard ? _controller.PendingInventory : _controller.Inventory;
            StringBuilder summary = new StringBuilder($"특수 키캡 {inventory.Total} / {_controller.Settings.Capacity}    기본 {Mathf.Max(0, _controller.Settings.Capacity - inventory.Total)}\n");
            for (int type = 1; type <= 5; type++) summary.Append($"{GetName((SpecialKeycapType)type)} {inventory.GetCount((SpecialKeycapType)type)}   ");
            _inventory.text = summary.ToString();
            if (phase == TournamentPhase.Shop)
                for (int index = 0; index < _buyButtons.Length; index++)
                {
                    SpecialKeycapType type = _controller.GetOffer(index);
                    string sold = _controller.IsPurchased(index) ? "구매 완료" : $"{_controller.Settings.GetPrice(type)} 재화";
                    _offerCards[index].Show(type, _controller.Settings.GetBundleSize(type));
                    _buyButtons[index].SetState(sold, _controller.CanBuy(index));
                }
            if (phase == TournamentPhase.Discard)
            {
                _message.text += $"\n추가 폐기 {_controller.RequiredDiscards}개";
                for (int index = 0; index < _discardButtons.Length; index++)
                {
                    SpecialKeycapType type = (SpecialKeycapType)(index + 1);
                    _discardButtons[index].SetState($"{GetName(type)} ({inventory.GetCount(type)})\n1개 폐기", _controller.RequiredDiscards > 0 && inventory.GetCount(type) > 0);
                }
                _confirmButton.SetState("구매 확정", _controller.RequiredDiscards == 0);
                _cancelButton.SetState("구매 취소", true);
            }
        }

        /// <summary>type의 한글 표시 이름을 반환하며 게임 상태를 변경하지 않는다.</summary>
        public static string GetName(SpecialKeycapType type)
        {
            switch (type)
            {
                case SpecialKeycapType.Explosion: return "폭발";
                case SpecialKeycapType.Bonus: return "보너스";
                case SpecialKeycapType.Row: return "가로";
                case SpecialKeycapType.Column: return "세로";
                case SpecialKeycapType.All: return "전체 타격";
                default: return "기본";
            }
        }

        /// <summary>type의 상점 효과 설명을 반환하며 표시용 문구 이외의 상태는 변경하지 않는다.</summary>
        public static string GetDescription(SpecialKeycapType type)
        {
            switch (type)
            {
                case SpecialKeycapType.Explosion: return "파괴: 주변 12칸에 피해 5";
                case SpecialKeycapType.Bonus: return "파괴: 3초간 점수 2배";
                case SpecialKeycapType.Row: return "파괴: 같은 행에 피해 3";
                case SpecialKeycapType.Column: return "파괴: 같은 열에 피해 3";
                default: return "파괴: 전체에 피해 2";
            }
        }

        /// <summary>index 상품 구매를 연결된 토너먼트에 요청한다.</summary>
        private void Buy(int index) { _controller.Buy(index); }
        /// <summary>index는 사용하지 않고 현재 상점 상품의 유료 리롤을 요청한다.</summary>
        private void Reroll(int index) { _controller.Reroll(); }
        /// <summary>index에 해당하는 유형의 키캡 하나를 구매 후보에서 폐기하도록 요청한다.</summary>
        private void Discard(int index) { _controller.Discard((SpecialKeycapType)(index + 1)); }
        /// <summary>index는 사용하지 않고 구매 후보의 확정을 요청한다.</summary>
        private void Confirm(int index) { _controller.ConfirmPurchase(); }
        /// <summary>index는 사용하지 않고 구매와 폐기 선택의 취소를 요청한다.</summary>
        private void Cancel(int index) { _controller.CancelPurchase(); }
        /// <summary>index는 사용하지 않고 현재 결과의 다음 진행을 요청한다.</summary>
        private void Continue(int index) { _controller.Continue(); }
        /// <summary>index는 사용하지 않고 종료된 토너먼트의 재시작을 요청한다.</summary>
        private void Restart(int index) { _controller.Restart(); }

        void OnDestroy()
        {
            if (_controller == null) return;
            _controller.Changed -= Refresh;
            foreach (TournamentActionButton button in _buyButtons) button.Clicked -= Buy;
            _rerollButton.Clicked -= Reroll;
            foreach (TournamentActionButton button in _discardButtons) button.Clicked -= Discard;
            _confirmButton.Clicked -= Confirm;
            _cancelButton.Clicked -= Cancel;
            _continueButton.Clicked -= Continue;
            _restartButton.Clicked -= Restart;
        }
    }
}
