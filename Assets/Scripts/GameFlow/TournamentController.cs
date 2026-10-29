using System;
using System.Collections.Generic;

using UnityEngine;

using KeyboardModeling;

namespace Game.Tournament
{
    [DisallowMultipleComponent]
    public sealed class TournamentController : MonoBehaviour
    {
        public static event Action<TournamentController, bool, string> PlaytestShopStateChanged;
        public static event Action<TournamentController, string> PlaytestInventorySnapshotRequested;
        public static event Action<TournamentController, TournamentShopObservation> PlaytestShopAction;

        [Header("Configuration")]
        [SerializeField] private TournamentSettings _settings;
        [SerializeField] private TournamentKeyLayout _keyLayout;
        [SerializeField] private Material[] _decalMaterials;
        [SerializeField] private TournamentView _view;

        [Header("Runtime")]
        private GameManager _manager;
        private EnemyTypingController _enemy;
        private TournamentInventory _inventory;
        private TournamentInventory _enemyInventory;
        private TournamentInventory _pendingInventory;
        private readonly SpecialKeycapType[] _offers = new SpecialKeycapType[3];
        private readonly bool[] _purchased = new bool[3];
        private readonly List<SpecialKeycapType> _catalog = new List<SpecialKeycapType>();
        private readonly int[] _pendingDiscards = new int[6];
        private int _offerGeneration;
        private string _shopTransactionId;
        private int _pendingOffer = -1;
        private int _rerollsUsed;
        private int _outcome;
        private long _lastPlayerScore;
        private long _lastEnemyScore;
        private bool _initialized;
        private bool _suddenDeath;
        private string _message;

        public TournamentPhase Phase { get; private set; }
        public string PlaytestRunId { get; private set; }
        public string PlaytestBattleKind => _suddenDeath ? "SuddenDeath" : "TimedRound";
        public int Round { get; private set; }
        public long Currency { get; private set; }
        public TournamentResult Result { get; private set; }
        public bool IsBattleActive => Phase == TournamentPhase.Battle && _manager.IsTimerRunning;
        public TournamentInventory Inventory => _inventory;
        public TournamentInventory PlaytestEnemyInventory => _enemyInventory;
        public TournamentInventory PendingInventory => _pendingInventory;
        public TournamentSettings Settings => _settings;
        public string Message => _message;
        public int RequiredDiscards => _pendingInventory == null ? 0 : Mathf.Max(0, _pendingInventory.Total - _settings.Capacity);
        public float PlayerBonusRemaining => _manager.PlaytestPlayerScore.BonusRemaining;
        public int RerollsRemaining => Mathf.Max(0, _settings.RerollLimit - _rerollsUsed);
        public bool CanReroll => Phase == TournamentPhase.Shop && RerollsRemaining > 0 && Currency >= _settings.RerollCost;
        public event Action Changed;
        public event Action<TournamentResult> Completed;
        public event Action<TournamentResult> WorkshopReturnRequested;

        /// <summary>manager와 enemy를 대전 공급자로 연결하고 진행, 생성 및 화면 이벤트를 구독한다.</summary>
        public void Initialize(GameManager manager, EnemyTypingController enemy)
        {
            _manager = manager;
            _enemy = enemy;
            _manager.TournamentKeyboardCreated += ConfigureKeyboard;
            _manager.TournamentBattleFinished += HandleBattleFinished;
            GameManager.PlaytestRoundStarted += HandleRoundStarted;
            _initialized = true;
            if (_catalog.Count == 0) SetPresetCatalog(null);
            _view.Bind(this);
        }

        /// <summary>types를 작업실 프리셋의 상점 유형으로 복사하며 null이면 PoC 5종을 사용한다.</summary>
        public void SetPresetCatalog(IEnumerable<SpecialKeycapType> types)
        {
            _catalog.Clear();
            if (types != null)
                foreach (SpecialKeycapType type in types)
                    if (type != SpecialKeycapType.None && !_catalog.Contains(type)) _catalog.Add(type);
            if (_catalog.Count == 0)
                for (int type = 1; type <= 5; type++) _catalog.Add((SpecialKeycapType)type);
        }

        /// <summary>기본 구성으로 토너먼트 실행 상태를 생성하고 첫 대전을 준비하며 작업실 데이터는 변경하지 않는다.</summary>
        public void BeginTournament()
        {
            PlaytestShopStateChanged?.Invoke(this, false, "Restart");
            if (PlaytestRunId != null) PlaytestInventorySnapshotRequested?.Invoke(this, "BeforeRestart");
            PlaytestRunId = Guid.NewGuid().ToString("N");
            _shopTransactionId = null;
            _inventory = new TournamentInventory(_settings.Capacity);
            _enemyInventory = new TournamentInventory(_settings.Capacity);
            Currency = 0;
            Result = null;
            Round = 1;
            PlaytestInventorySnapshotRequested?.Invoke(this, "TournamentStart");
            BeginBattle(false);
        }

        /// <summary>suddenDeath로 일반 대전과 연장전을 구분하여 로봇 구성 및 양쪽 키보드를 준비한다.</summary>
        private void BeginBattle(bool suddenDeath)
        {
            _suddenDeath = suddenDeath;
            Phase = TournamentPhase.Preparing;
            _message = suddenDeath ? "연장전 · 새 키보드를 먼저 완파하면 승리" : $"{Round} / {_settings.RoundCount} 대전 준비";
            RobotRound robot = _settings.GetRound(Round);

            _enemy.ConfigureTournamentAttack(robot.AttacksPerSecond, robot.Damage);
            _manager.BeginTournamentBattle(_settings.BattleDuration, suddenDeath);
            Changed?.Invoke();
        }

        /// <summary>source가 현재 대전이면 준비 화면을 종료하고 진행 상태를 Battle로 변경한다.</summary>
        private void HandleRoundStarted(GameManager source)
        {
            if (source != _manager) return;
            Phase = TournamentPhase.Battle;
            PlaytestInventorySnapshotRequested?.Invoke(this, "BattleStart");
            Changed?.Invoke();
        }

        /// <summary>keyboard와 isEnemy로 소유자별 보유 키캡, 체력, 점수 및 임시 표시를 생성한다.</summary>
        private void ConfigureKeyboard(GameObject keyboard, bool isEnemy)
        {
            KeyboardScoreCombo score = isEnemy ? _manager.PlaytestEnemyScore : _manager.PlaytestPlayerScore;
            keyboard.GetComponent<TournamentKeyboardEffects>().Initialize(this,
                isEnemy ? _enemyInventory : _inventory, score, _keyLayout, _settings.KeycapHealth, _decalMaterials);
        }

        /// <summary>outcome으로 승패를 기록하고 승리 재화를 지급하거나 최종 순위와 골드를 확정한다.</summary>
        private void HandleBattleFinished(int outcome)
        {
            _outcome = outcome;
            _lastPlayerScore = _manager.PlaytestPlayerScore.Score;
            _lastEnemyScore = _manager.PlaytestEnemyScore.Score;
            Phase = TournamentPhase.Result;
            string scores = $"플레이어 {_lastPlayerScore:N0}점  :  로봇 {_lastEnemyScore:N0}점";
            if (outcome == 0)
            {
                _message = $"무승부\n{scores}\n새 키보드로 완파 대결을 진행합니다.";
            }
            else if (outcome > 0 && Round < _settings.RoundCount)
            {
                long earned = (long)(_lastPlayerScore * (decimal)_settings.CurrencyRewardMultiplier / _settings.ScorePerCurrency);
                Currency += earned;
                _message = $"{Round}대전 {(_suddenDeath ? "연장전 완파 " : "")}승리!\n{scores}\n토너먼트 재화 +{earned:N0}";
            }
            else
            {
                int rank = outcome > 0 ? 1 : Round == _settings.RoundCount ? 2 : Round == 4 ? 3 : 4;
                Result = new TournamentResult(rank, _settings.GetGold(rank), _lastPlayerScore, _lastEnemyScore);
                Phase = TournamentPhase.Finished;
                _message = $"토너먼트 종료 · {(rank <= 3 ? rank + "위" : "순위권 밖")}\n{scores}\n작업실 보상 {Result.Gold}골드";
                PlaytestInventorySnapshotRequested?.Invoke(this, "BeforeTournamentReset");
                Currency = 0;
                _inventory = new TournamentInventory(_settings.Capacity);
                _enemyInventory = new TournamentInventory(_settings.Capacity);
                _pendingInventory = null;
                Completed?.Invoke(Result);
            }
            Changed?.Invoke();
        }

        /// <summary>현재 결과 상태에 따라 연장전, 상점 또는 다음 대전을 시작하고 화면을 갱신한다.</summary>
        public void Continue()
        {
            if (Phase == TournamentPhase.Result)
            {
                if (_outcome == 0) BeginBattle(true);
                else OpenShop();
            }
            else if (Phase == TournamentPhase.Shop)
            {
                PlaytestShopStateChanged?.Invoke(this, false, "NextBattle");
                Round++;
                BeginBattle(false);
            }
            else if (Phase == TournamentPhase.Finished)
            {
                if (WorkshopReturnRequested != null) WorkshopReturnRequested.Invoke(Result);
                else { _message = "작업실 연결 대기\n골드 보상은 최종 결과에 보관됩니다.\n다시 시작으로 새 토너먼트를 플레이할 수 있습니다."; Changed?.Invoke(); }
            }
        }

        /// <summary>상점 진입 시 중복 가능한 상품 3개를 생성하고 각 상품의 구매 상태를 초기화한다.</summary>
        private void OpenShop()
        {
            Phase = TournamentPhase.Shop;
            PlaytestShopStateChanged?.Invoke(this, true, "ShopOpened");
            _rerollsUsed = 0;
            _offerGeneration = 0;
            _message = "다음 대전을 위한 키캡을 선택하세요.";
            RollOffers();
            Changed?.Invoke();
        }

        /// <summary>카탈로그에서 상품 3개를 다시 추첨하고 각 상품의 구매 상태를 초기화하며 보유 키캡은 유지한다.</summary>
        private void RollOffers()
        {
            _offerGeneration++;
            for (int index = 0; index < _offers.Length; index++)
            {
                _offers[index] = _catalog[UnityEngine.Random.Range(0, _catalog.Count)];
                _purchased[index] = false;
            }
            for (int index = 0; index < _offers.Length; index++)
                ObserveShopAction("OfferPresented", index, _offers[index], _settings.GetBundleSize(_offers[index]),
                    _settings.GetPrice(_offers[index]), Currency, false);
        }

        /// <summary>상점에서 잔액과 횟수가 충분하면 리롤 비용을 차감하고 횟수를 소모하여 상품 3개를 갱신한다.</summary>
        public void Reroll()
        {
            if (!CanReroll) return;
            long currencyBefore = Currency;
            Currency -= _settings.RerollCost;
            _rerollsUsed++;
            RollOffers();
            ObserveShopAction("Reroll", -1, SpecialKeycapType.None, 0, _settings.RerollCost, currencyBefore, true);
            _message = $"상품을 갱신했습니다. · 리롤 남은 횟수 {RerollsRemaining}회";
            Changed?.Invoke();
        }

        /// <summary>index 상품의 유형을 반환하며 구매와 난수 상태는 변경하지 않는다.</summary>
        public SpecialKeycapType GetOffer(int index) => _offers[index];

        /// <summary>index 상품이 판매 중이고 잔액이 충분한지 반환하며 보유 상태는 변경하지 않는다.</summary>
        public bool CanBuy(int index)
        {
            return Phase == TournamentPhase.Shop && index >= 0 && index < _offers.Length
                && !_purchased[index] && Currency >= _settings.GetPrice(_offers[index]);
        }

        /// <summary>index 상품의 구매 완료 여부를 반환하며 제시된 상품 상태를 유지한다.</summary>
        public bool IsPurchased(int index) => _purchased[index];

        /// <summary>index 상품을 구매 후보에 추가하고 초과 수량이 있으면 폐기 선택으로 전환한다.</summary>
        public void Buy(int index)
        {
            if (!CanBuy(index)) return;
            Array.Clear(_pendingDiscards, 0, _pendingDiscards.Length);
            _shopTransactionId = Guid.NewGuid().ToString("N");
            _pendingOffer = index;
            _pendingInventory = new TournamentInventory(_inventory, _settings.Capacity + _settings.GetBundleSize(_offers[index]));
            _pendingInventory.TryAdd(_offers[index], _settings.GetBundleSize(_offers[index]));
            ObserveShopAction("PurchaseSelected", index, _offers[index], _settings.GetBundleSize(_offers[index]),
                _settings.GetPrice(_offers[index]), Currency, false);
            if (RequiredDiscards > 0)
            {
                Phase = TournamentPhase.Discard;
                _message = $"{_settings.Capacity}키 한도 초과 · 필요한 수만큼 폐기하세요.\n확인 전에는 재화와 기존 보유 구성이 변경되지 않습니다.";
                Changed?.Invoke();
            }
            else ConfirmPurchase();
        }

        /// <summary>type의 키캡 하나를 구매 후보에서 폐기하며 초과 수량을 모두 처리하면 추가 폐기를 막는다.</summary>
        public void Discard(SpecialKeycapType type)
        {
            if (Phase != TournamentPhase.Discard || RequiredDiscards == 0) return;
            bool removed = _pendingInventory.RemoveOne(type);
            if (removed)
            {
                _pendingDiscards[(int)type]++;
                ObserveShopAction("DiscardSelected", _pendingOffer, type, 1, 0, Currency, false);
            }
            Changed?.Invoke();
        }

        /// <summary>정리된 구매 후보를 확정하고 상품 가격을 차감하며 플레이어와 로봇의 보유 구성을 동일하게 갱신한다.</summary>
        public void ConfirmPurchase()
        {
            if (_pendingInventory == null || RequiredDiscards != 0) return;
            long currencyBefore = Currency;
            Currency -= _settings.GetPrice(_offers[_pendingOffer]);
            _purchased[_pendingOffer] = true;
            _inventory = new TournamentInventory(_pendingInventory, _settings.Capacity);
            _enemyInventory = new TournamentInventory(_inventory, _settings.Capacity);
            _pendingInventory = null;
            Phase = TournamentPhase.Shop;
            ObserveShopAction("PurchaseConfirmed", _pendingOffer, _offers[_pendingOffer], _settings.GetBundleSize(_offers[_pendingOffer]),
                _settings.GetPrice(_offers[_pendingOffer]), currencyBefore, true);
            for (int type = 1; type < _pendingDiscards.Length; type++)
                if (_pendingDiscards[type] > 0)
                    ObserveShopAction("DiscardConfirmed", _pendingOffer, (SpecialKeycapType)type,
                        _pendingDiscards[type], 0, Currency, true);
            _pendingOffer = -1;
            _shopTransactionId = null;
            _message = "구매 완료 · 다음 대전 시작부터 적용됩니다.";
            Changed?.Invoke();
        }

        /// <summary>미확정 구매와 폐기 선택을 취소하고 기존 잔액과 보유 구성을 보존한 채 상점으로 돌아간다.</summary>
        public void CancelPurchase()
        {
            if (Phase != TournamentPhase.Discard) return;
            ObserveShopAction("PurchaseCancelled", _pendingOffer, _offers[_pendingOffer], _settings.GetBundleSize(_offers[_pendingOffer]),
                _settings.GetPrice(_offers[_pendingOffer]), Currency, false);
            _pendingInventory = null;
            _pendingOffer = -1;
            _shopTransactionId = null;
            Phase = TournamentPhase.Shop;
            _message = "구매를 취소했습니다.";
            Changed?.Invoke();
        }

        /// <summary>최종 결과 화면에서만 현재 씬을 재시작하여 새 토너먼트를 시작한다.</summary>
        public void Restart()
        {
            if (Phase == TournamentPhase.Finished) _manager.RestartGame();
        }

        /// <summary>
        /// action과 상품 슬롯, 유형, 수량, 가격 및 변경 전 재화를 관찰 이벤트로 전달한다.
        /// 현재 세대와 거래 식별자 및 확정 후 재화를 복사하고 게임 상태는 변경하지 않는다.
        /// </summary>
        private void ObserveShopAction(string action, int slot, SpecialKeycapType type, int quantity,
            long price, long currencyBefore, bool confirmed)
        {
            PlaytestShopAction?.Invoke(this, new TournamentShopObservation(action, slot, _offerGeneration,
                type, quantity, price, currencyBefore, Currency, confirmed, _shopTransactionId));
        }

        void OnDestroy()
        {
            PlaytestShopStateChanged?.Invoke(this, false, "SceneExit");
            if (!_initialized) return;
            _manager.TournamentKeyboardCreated -= ConfigureKeyboard;
            _manager.TournamentBattleFinished -= HandleBattleFinished;
            GameManager.PlaytestRoundStarted -= HandleRoundStarted;
        }

        /// <summary>
        /// 새 실행에서 상점 관찰 이벤트를 초기화한다.
        /// 입력값 없이 이전 플레이 실행의 정적 구독을 제거한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaytestObservers()
        {
            PlaytestShopStateChanged = null;
            PlaytestInventorySnapshotRequested = null;
            PlaytestShopAction = null;
        }
    }

    public readonly struct TournamentShopObservation
    {
        public string Action { get; }
        public int Slot { get; }
        public int Generation { get; }
        public SpecialKeycapType Type { get; }
        public int Quantity { get; }
        public long Price { get; }
        public long CurrencyBefore { get; }
        public long CurrencyAfter { get; }
        public bool Confirmed { get; }
        public string TransactionId { get; }

        /// <summary>
        /// action, slot, generation과 상품 및 재화 값을 관찰 시점의 불변 상태로 저장한다.
        /// confirmed와 transactionId로 후보 선택과 실제 확정 및 취소를 구분한 이벤트를 생성한다.
        /// </summary>
        public TournamentShopObservation(string action, int slot, int generation, SpecialKeycapType type,
            int quantity, long price, long currencyBefore, long currencyAfter, bool confirmed, string transactionId)
        {
            Action = action;
            Slot = slot;
            Generation = generation;
            Type = type;
            Quantity = quantity;
            Price = price;
            CurrencyBefore = currencyBefore;
            CurrencyAfter = currencyAfter;
            Confirmed = confirmed;
            TransactionId = transactionId;
        }
    }
}
