using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

using Game.Logging;
using KeyboardModeling;

namespace Game.Tournament
{
    [DisallowMultipleComponent]
    public sealed class TournamentController : MonoBehaviour
    {
        private const string WORKSHOP_SCENE_PATH = "Assets/Scenes/WorkShop2.unity";
        private static TournamentController _returnPauseOwner;
        private static int _returnResumeFrame = -1;

        public static bool IsReturnPaused => _returnPauseOwner != null
            && (_returnPauseOwner._returnPaused || Time.frameCount <= _returnResumeFrame);

        public static event Action<TournamentController, bool, string> PlaytestShopStateChanged;
        public static event Action<TournamentController, string> PlaytestInventorySnapshotRequested;
        public static event Action<TournamentController, TournamentShopObservation> PlaytestShopAction;

        [Header("Configuration")]
        [SerializeField] private TournamentSettings _settings;
        [SerializeField] private TournamentKeyLayout _keyLayout;
        [SerializeField] private TournamentView _view;
        [SerializeField] private KeyboardCustomizationStorage _keyboardStorage;
        [SerializeField] private KeyboardPartCatalog _partCatalog;
        [SerializeField] private TournamentShopWorkspace _workspace;
        [Header("Runtime")]
        private GameManager _manager;
        private EnemyTypingController _enemy;
        private TournamentInventory _inventory;
        private TournamentInventory _enemyInventory;
        private List<KeyboardPartDefinition> _catalog;
        private readonly KeyboardPartDefinition[] _offers = new KeyboardPartDefinition[3];
        private readonly bool[] _purchased = new bool[3];
        private int _offerGeneration;
        private int _rerollsUsed;
        private int _outcome;
        private bool _initialized;
        private bool _workshopReturnStarted;
        private bool _returnPaused;
        private bool _abandoned;
        private float _returnTimeScale;
        private bool _suddenDeath;
        private string _message;
        private long _enemyCurrency;
        private KeycapDefinition _selectedKeycap;

        public TournamentPhase Phase { get; private set; }
        public string PlaytestRunId { get; private set; }
        public string PlaytestBattleKind => _suddenDeath ? "SuddenDeath" : "TimedRound";
        public int Round { get; private set; }
        public long Currency { get; private set; }
        public long EnemyCurrency => _enemyCurrency;
        public TournamentResult Result { get; private set; }
        public bool IsBattleActive => !_workshopReturnStarted && Phase == TournamentPhase.Battle && _manager.IsTimerRunning;
        public TournamentInventory Inventory => _inventory;
        public KeycapDefinition SelectedKeycap => _selectedKeycap;
        public TournamentInventory PlaytestEnemyInventory => _enemyInventory;
        public TournamentSettings Settings => _settings;
        public string Message => _message;
        public int RerollsRemaining => Mathf.Max(0, _settings.RerollLimit - _rerollsUsed);
        public bool CanReroll => Phase == TournamentPhase.Shop && RerollsRemaining > 0 && Currency >= _settings.RerollCost;
        public event Action Changed;
        public event Action<TournamentResult> Completed;
        public event Action<TournamentResult> WorkshopReturnRequested;

        /// <summary>manager와 enemy를 연결하고 생성 및 진행 이벤트를 구독한다. 화면과 작업대는 연결된 씬 인스턴스를 사용한다.</summary>
        public void Initialize(GameManager manager, EnemyTypingController enemy)
        {
            _manager = manager;
            _enemy = enemy;
            _manager.TournamentKeyboardCreated += ConfigureKeyboard;
            _manager.TournamentBattleFinished += HandleBattleFinished;
            GameManager.PlaytestRoundStarted += HandleRoundStarted;
            _workspace.KeyPressed += HandleShopKeyPressed;
            _initialized = true;
            _view.Bind(this);
        }

        /// <summary>선택 프리셋에서 상품만 추출하고 양측을 기본 부품과 빈 재화로 초기화한다. 작업실 원본과 저장 파일은 유지한다.</summary>
        public void BeginTournament()
        {
            PlaytestShopStateChanged?.Invoke(this, false, "Restart");
            PlaytestRunId = Guid.NewGuid().ToString("N");
            _inventory = new TournamentInventory(_settings.Capacity);
            _enemyInventory = new TournamentInventory(_settings.Capacity);
            Currency = 0;
            _enemyCurrency = 0;
            Result = null;
            _workshopReturnStarted = false;
            _abandoned = false;
            Round = 1;
            KeyboardCustomizationState selection = TournamentKeyboardSession.GetSelection();
            if (selection == null) _keyboardStorage.TryReadCustomization(out selection);
            if (selection == null) selection = _keyboardStorage.GetDefaultCustomization();
            PlaytestResearchSession.BeginTournament(PlaytestRunId, selection,
                TournamentKeyboardSession.HasSelection ? "TournamentSelection" : "TournamentFallback");
            _catalog = _partCatalog.CreatePool(selection);
            TournamentKeyboardSession.Begin();
            TournamentKeyboardSession.UpdateCurrent(selection);
            PlaytestInventorySnapshotRequested?.Invoke(this, "TournamentStart");
            BeginBattle(false);
        }

        /// <summary>suddenDeath에 따라 대전 시간과 로봇 속도를 준비한다. 현재 구매 부품의 대미지를 로봇에 합산한다.</summary>
        private void BeginBattle(bool suddenDeath)
        {
            _selectedKeycap = null;
            _workspace.Hide();
            _suddenDeath = suddenDeath;
            Phase = TournamentPhase.Preparing;
            _message = suddenDeath ? "연장전 · 먼저 완파하면 승리" : $"{Round} / {_settings.RoundCount} 대전 준비";
            RobotRound robot = _settings.GetRound(Round);
            _enemy.ConfigureTournamentAttack(robot.AttacksPerSecond, robot.Damage + _enemyInventory.DamageBonus);
            _manager.BeginTournamentBattle(_settings.BattleDuration, suddenDeath);
            Changed?.Invoke();
        }

        /// <summary>source가 연결된 매니저이면 Battle 상태로 변경한다. 양측 보유 구성의 스냅샷을 알린다.</summary>
        private void HandleRoundStarted(GameManager source)
        {
            if (source != _manager) return;
            Phase = TournamentPhase.Battle;
            PlaytestInventorySnapshotRequested?.Invoke(this, "BattleStart");
            Changed?.Invoke();
        }

        /// <summary>keyboard에 isEnemy 소유자의 고정 구성과 능력을 복원한다. 보충 때도 같은 키 위치와 부품 보너스를 적용한다.</summary>
        private void ConfigureKeyboard(GameObject keyboard, bool isEnemy)
        {
            TournamentInventory inventory = isEnemy ? _enemyInventory : _inventory;
            KeyboardCustomization custom = keyboard.GetComponent<KeyboardCustomization>();
            custom.PrepareForBattle();
            _keyboardStorage.ApplyCustomization(TournamentKeyboardSession.GetCurrent(), custom);
            custom.PrepareForBattle();
            inventory.ConfigureLayout(custom, _catalog, _partCatalog.DefaultKeycap);
            keyboard.GetComponent<KeyboardAttackController>().ConfigurePartDamage(inventory.DamageBonus);
            keyboard.GetComponent<TournamentKeyboardEffects>().Initialize(this, inventory,
                isEnemy ? _manager.PlaytestEnemyScore : _manager.PlaytestPlayerScore, _keyLayout, _settings.KeycapHealth);
        }

        /// <summary>score를 기존 보상 비율로 재화로 환산해 반환한다. 음수 점수는 보상을 지급하지 않는다.</summary>
        private long GetReward(long score)
        {
            return (long)(Math.Max(0, score) * (decimal)_settings.CurrencyRewardMultiplier / _settings.ScorePerCurrency);
        }

        /// <summary>outcome과 양측 점수로 결과와 독립 재화를 확정한다. 무승부 연장전은 종료 후 점수로 정산하여 중복 지급하지 않는다.</summary>
        private void HandleBattleFinished(int outcome)
        {
            if (Result != null || _workshopReturnStarted) return;
            _outcome = outcome;
            long playerScore = _manager.PlaytestPlayerScore.Score;
            long enemyScore = _manager.PlaytestEnemyScore.Score;
            Phase = TournamentPhase.Result;
            _message = $"플레이어 {playerScore:N0}점 : 로봇 {enemyScore:N0}점";
            if (outcome == 0) _message += "\n무승부 · 새 키보드로 완파 대결";
            else if (outcome > 0 && Round < _settings.RoundCount)
            {
                long reward = GetReward(playerScore);
                Currency += reward;
                _enemyCurrency += GetReward(enemyScore);
                _message += $"\n승리 · 재화 +{reward:N0}";
            }
            else
            {
                int rank = outcome > 0 ? 1 : Round == _settings.RoundCount ? 2 : Round == 4 ? 3 : 4;
                Result = new TournamentResult(rank, _settings.GetGold(rank), playerScore, enemyScore);
                Phase = TournamentPhase.Finished;
                _message += $"\n토너먼트 종료 · {rank}위 · 작업실 보상 {Result.Gold}골드";
                PlaytestResearchSession.FinishTournament(outcome > 0 ? "Victory" : "Defeat");
                Completed?.Invoke(Result);
            }
            Changed?.Invoke();
        }

        /// <summary>현재 단계에 따라 연장전, 상점, 다음 대전 또는 작업실 복귀를 요청한다. 상점에서는 미리보기 구성을 전투 상태로 확정한다.</summary>
        public void Continue()
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (Phase == TournamentPhase.Result)
            {
                if (_outcome == 0) BeginBattle(true);
                else OpenShop();
            }
            else if (Phase == TournamentPhase.Shop)
            {
                KeyboardCustomizationState confirmed = _workspace.Capture();
                TournamentKeyboardSession.UpdateCurrent(confirmed);
                PlaytestResearchSession.CaptureConfiguration(confirmed, "ShopConfirmed");
                PlaytestInventorySnapshotRequested?.Invoke(this, "ShopExit");
                PlaytestShopStateChanged?.Invoke(this, false, "NextBattle");
                Round++;
                BeginBattle(false);
            }
            else if (Phase == TournamentPhase.Finished)
            {
                ReturnToWorkshop();
            }
        }

        /// <summary>종료된 토너먼트의 재고와 임시 구성을 비우고 Workshop2로 복귀한다. Result는 복귀 이벤트에 전달한다.</summary>
        public void ReturnToWorkshop()
        {
            if (_workshopReturnStarted || (!_abandoned && Phase != TournamentPhase.Finished)) return;
            _workshopReturnStarted = true;
            _manager.StopTournamentForReturn();
            _workspace.Hide();
            PlaytestResearchSession.Emit("WorkshopReturnRequested", "ContinueButton");
            PlaytestInventorySnapshotRequested?.Invoke(this, "BeforeTournamentReset");
            Currency = 0;
            _enemyCurrency = 0;
            _inventory = new TournamentInventory(_settings.Capacity);
            _enemyInventory = new TournamentInventory(_settings.Capacity);
            _selectedKeycap = null;
            TournamentKeyboardSession.UpdateCurrent(_keyboardStorage.GetDefaultCustomization());
            TournamentKeyboardSession.RequestWorkshopReturn();
            ReleaseReturnPause(true);
            if (WorkshopReturnRequested != null) WorkshopReturnRequested.Invoke(Result);
            else SceneManager.LoadScene(WORKSHOP_SCENE_PATH);
        }

        /// <summary>현재 시간 배율을 보관하고 복귀 확인창의 정지를 시작한다. 중복 요청이나 다른 도움말 정지 중에는 false를 반환한다.</summary>
        public bool PauseForWorkshopReturn()
        {
            if (IsReturnPaused || _workshopReturnStarted || Game.Tutorials.FirstEncounterTutorial.IsBlocking) return false;
            _returnTimeScale = Time.timeScale;
            _returnPauseOwner = this;
            _returnPaused = true;
            Time.timeScale = 0f;
            return true;
        }

        /// <summary>leaving이 false이면 원래 시간 배율로 돌아가고 한 프레임 동안 남은 입력을 차단한다. 복귀 확정 시에는 작업실에 정지를 남기지 않는다.</summary>
        public void ReleaseReturnPause(bool leaving)
        {
            if (_returnPauseOwner != this) return;
            _returnPaused = false;
            _returnResumeFrame = Time.frameCount + 1;
            Time.timeScale = leaving && _returnTimeScale <= 0f ? 1f : _returnTimeScale;
            if (leaving) _returnPauseOwner = null;
        }

        /// <summary>진행 중인 토너먼트를 보상 결과 없이 포기한다. 정상 완료 이벤트 없이 중단을 기록하고 기존 공통 복귀 처리를 한 번 실행한다.</summary>
        public void AbandonToWorkshop()
        {
            if (_workshopReturnStarted || Phase == TournamentPhase.Finished) return;
            _abandoned = true;
            Result = null;
            PlaytestResearchSession.InterruptTournament("Abandoned");
            ReturnToWorkshop();
        }

        /// <summary>플레이어와 로봇에게 독립된 3개 상품을 추첨한다. 로봇은 제시된 순서에서 살 수 있는 상품만 구매한다.</summary>
        private void OpenShop()
        {
            Phase = TournamentPhase.Shop;
            _selectedKeycap = null;
            _rerollsUsed = 0;
            _offerGeneration = 0;
            PlaytestShopStateChanged?.Invoke(this, true, "ShopOpened");
            PlaytestInventorySnapshotRequested?.Invoke(this, "ShopOpened");
            _message = "키캡 구매 후 같은 부품의 키를 눌러 해금·강화하세요. 최대 3단계입니다.";
            RollOffers();
            var enemyShown = new HashSet<KeyboardPartDefinition>();
            for (int i = 0; i < _offers.Length; i++)
            {
                KeyboardPartDefinition part = DrawOffer(_enemyInventory, enemyShown);
                long before = _enemyCurrency;
                int levelBefore = part == null || part is KeycapDefinition ? -1 : _enemyInventory.GetPartLevel(part);
                int availableBefore = part != null ? _enemyInventory.GetAvailableCount(part) : -1;
                Observe("OfferShown", i, part, before, false, true);
                if (part != null && _enemyCurrency >= part.Price && _enemyInventory.TryAdd(part, _keyLayout))
                {
                    _enemyCurrency -= part.Price;
                    Observe("PurchaseConfirmed", i, part, before, true, true,
                        levelBefore: levelBefore, availableBefore: availableBefore);
                }
            }
            _workspace.Show(_keyboardStorage, _inventory, _partCatalog.DefaultKeycap);
            Changed?.Invoke();
        }

        /// <summary>inventory가 보유하거나 shown에 이미 제시한 전체 부품을 제외해 상품 하나를 추첨한다. 키캡 중복은 허용하고 후보가 없으면 null을 반환하며 제시된 전체 부품을 기록한다.</summary>
        private KeyboardPartDefinition DrawOffer(TournamentInventory inventory, HashSet<KeyboardPartDefinition> shown)
        {
            var candidates = new List<KeyboardPartDefinition>();
            foreach (KeyboardPartDefinition part in _catalog)
            {
                if (!inventory.CanPurchase(part, _keyLayout) || (!(part is KeycapDefinition) && shown.Contains(part))) continue;
                candidates.Add(part);
            }
            if (candidates.Count == 0) return null;
            KeyboardPartDefinition selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            if (!(selected is KeycapDefinition)) shown.Add(selected);
            return selected;
        }

        /// <summary>상품 세대를 증가시키고 3개 슬롯을 새로 추첨한다. 구매한 부품과 고정 배치는 유지한다.</summary>
        private void RollOffers()
        {
            _offerGeneration++;
            var shown = new HashSet<KeyboardPartDefinition>();
            for (int i = 0; i < _offers.Length; i++)
            {
                _offers[i] = DrawOffer(_inventory, shown);
                _purchased[i] = false;
                Observe("OfferShown", i, _offers[i], Currency, false, false);
            }
        }

        /// <summary>설정된 비용을 차감해 상품을 다시 추첨한다. 보유 재화와 남은 리롤 횟수가 부족하면 유지한다.</summary>
        public void Reroll()
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (!CanReroll)
            {
                Observe("RerollFailed", -1, null, Currency, false, false,
                    reason: Phase != TournamentPhase.Shop ? "NotInShop" : RerollsRemaining <= 0 ? "RerollLimit" : "InsufficientCurrency");
                return;
            }
            long before = Currency;
            if (Game.Tutorials.FirstEncounterTutorial.Request(Game.Tutorials.TutorialTopic.Reroll, Reroll)) return;
            Currency -= _settings.RerollCost;
            _rerollsUsed++;
            Observe("Reroll", -1, null, before, true, false);
            RollOffers();
            Changed?.Invoke();
        }

        /// <summary>index 슬롯의 부품을 반환한다. 추첨이나 구매 상태는 변경하지 않는다.</summary>
        public KeyboardPartDefinition GetOffer(int index) { return _offers[index]; }
        /// <summary>index 슬롯의 구매 완료 여부를 반환한다. 현재 상품 상태를 유지한다.</summary>
        public bool IsPurchased(int index) { return _purchased[index]; }
        /// <summary>index 상품의 잔액과 키캡 한도 및 전체 부품 중복을 검사한다. 게임 상태는 변경하지 않는다.</summary>
        public bool CanBuy(int index)
        {
            return Phase == TournamentPhase.Shop && index >= 0 && index < _offers.Length && !_purchased[index]
                && _offers[index] != null && Currency >= _offers[index].Price && _inventory.CanPurchase(_offers[index], _keyLayout);
        }

        /// <summary>index 상품을 구매 재고에 보관한다. 잔액과 수량을 검사하고 장착 상태는 유지한다.</summary>
        public void Buy(int index)
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (!CanBuy(index))
            {
                KeyboardPartDefinition offer = index >= 0 && index < _offers.Length ? _offers[index] : null;
                string reason = Phase != TournamentPhase.Shop ? "NotInShop" : index < 0 || index >= _offers.Length ? "InvalidSlot"
                    : _purchased[index] ? "AlreadyPurchased" : offer == null ? "EmptyOffer"
                    : Currency < offer.Price ? "InsufficientCurrency" : "PurchaseNotAllowed";
                Observe("PurchaseFailed", index, offer, Currency, false, false, reason: reason);
                return;
            }
            KeyboardPartDefinition part = _offers[index];
            int levelBefore = part is KeycapDefinition ? -1 : _inventory.GetPartLevel(part);
            int availableBefore = _inventory.GetAvailableCount(part);
            if (!_inventory.TryPurchase(part, _keyLayout))
            {
                Observe("PurchaseFailed", index, part, Currency, false, false, reason: "PurchaseNotAllowed");
                return;
            }
            long before = Currency;
            Currency -= part.Price;
            _purchased[index] = true;
            Observe("PurchaseConfirmed", index, part, before, true, false,
                levelBefore: levelBefore, availableBefore: availableBefore);
            _workspace.RefreshParts(_inventory, _partCatalog.DefaultKeycap);
            _message = part is KeycapDefinition ? "구매 완료 · 3개 위치에 한 번씩 해금·강화할 수 있습니다."
                : $"{part.DisplayName} 능력 {_inventory.GetPartLevel(part)} / 3단계";
            Changed?.Invoke();
        }

        /// <summary>part 인벤토리 아이템의 좌클릭을 처리한다. 키캡은 선택 상태를 바꾸고 다른 부품은 장착 상태를 갱신한다.</summary>
        public void ActivateInventoryItem(KeyboardPartDefinition part)
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (Phase != TournamentPhase.Shop || part == null || _inventory.GetCount(part) == 0) return;
            if (part is KeycapDefinition keycap)
            {
                KeycapDefinition previous = _selectedKeycap;
                _selectedKeycap = _selectedKeycap == keycap || !_inventory.CanApplyKeycap(keycap) ? null : keycap;
                if (previous != null && previous != _selectedKeycap)
                    Observe("InventoryDeselected", -1, previous, Currency, false, false, success: true, reason: "SelectionChanged");
                Observe(_selectedKeycap != null ? "InventorySelected" : "InventorySelectionCleared", -1, part,
                    Currency, false, false, success: _selectedKeycap != null || previous == keycap,
                    reason: _selectedKeycap != null || previous == keycap ? "" : "NoApplicableTarget");
                _message = _selectedKeycap != null ? "동일한 키캡 위치를 눌러 능력을 해금·강화하세요." : "키캡 선택을 해제했습니다.";
            }
            else
            {
                bool applied = _inventory.TryEquipPart(part);
                Observe("InventoryInspected", -1, part, Currency, false, false, success: applied);
                if (!applied) return;
                _workspace.RefreshParts(_inventory, _partCatalog.DefaultKeycap);
                _message = "부품 능력이 적용되어 있습니다.";
            }
            Changed?.Invoke();
        }

        /// <summary>part 인벤토리 아이템의 우클릭을 처리한다. 키캡은 마지막 배치를, 다른 부품은 해당 슬롯을 해제한다.</summary>
        public void UnequipInventoryItem(KeyboardPartDefinition part)
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (Phase != TournamentPhase.Shop || part == null) return;
            Key removedKey = Key.None;
            int availableBefore = _inventory.GetAvailableCount(part);
            bool removed = part is KeycapDefinition keycap
                ? _inventory.TryUnequipLatestKeycap(keycap, out removedKey)
                : _inventory.TryUnequipPart(part);
            int levelAfter = removedKey != Key.None ? _inventory.GetKeycapLevel(removedKey) : _inventory.GetPartLevel(part);
            Observe(removed ? "UpgradeCancelled" : "UpgradeCancelFailed", -1, part, Currency, false, false,
                success: removed, reason: removed ? "" : "NoCancellableUpgrade", key: removedKey,
                levelBefore: removed ? levelAfter + 1 : -1, availableBefore: availableBefore);
            if (!removed) return;
            _workspace.RefreshParts(_inventory, _partCatalog.DefaultKeycap);
            _message = "마지막 능력 단계를 취소하고 사용 횟수를 돌려놓았습니다.";
            Changed?.Invoke();
        }

        /// <summary>상점 키보드의 key 입력에 선택된 키캡을 장착한다. 남은 수량이 없으면 선택을 해제하고 작업대 외형을 갱신한다.</summary>
        private void HandleShopKeyPressed(Key key)
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (Phase != TournamentPhase.Shop || _selectedKeycap == null) return;
            KeycapDefinition selected = _selectedKeycap;
            int levelBefore = _inventory.GetKeycapLevel(key);
            int availableBefore = _inventory.GetAvailableCount(selected);
            bool applied = _inventory.TryEquipKeycap(selected, key, _keyLayout);
            string reason = "";
            if (!applied)
            {
                reason = !_inventory.InstalledKeycaps.TryGetValue(key, out var installed) ? "MissingTarget"
                    : installed != selected ? "PartMismatch" : levelBefore >= TournamentInventory.MaxLevel ? "MaxLevel"
                    : availableBefore <= 0 ? "NoRemainingUses" : "BundleTargetAlreadyUsed";
            }
            Observe(applied ? (levelBefore == 0 ? "AbilityUnlocked" : "AbilityUpgraded") : "AbilityApplyFailed",
                -1, selected, Currency, false, false, success: applied, reason: reason, key: key,
                levelBefore: levelBefore, availableBefore: availableBefore);
            if (!applied)
            {
                _message = "동일한 부품의 위치만 선택할 수 있습니다. 한 묶음은 같은 키에 한 번만 적용됩니다. 최대 3단계입니다.";
                Changed?.Invoke();
                return;
            }
            _workspace.RefreshParts(_inventory, _partCatalog.DefaultKeycap);
            if (!_inventory.CanApplyKeycap(_selectedKeycap))
            {
                _selectedKeycap = null;
                Observe("InventoryDeselected", -1, selected, Currency, false, false, success: true, reason: "NoApplicableTarget");
            }
            _message = $"{key} · {_inventory.GetKeycapLevel(key)} / 3단계"
                + (_selectedKeycap != null ? " · 같은 부품의 다음 키를 선택하세요." : " · 해금·강화 완료");
            Changed?.Invoke();
        }

        /// <summary>최종 결과 화면에서만 씬을 재시작한다. 작업실 선택 원본과 저장 파일은 유지한다.</summary>
        public void Restart()
        {
            if (IsReturnPaused || _workshopReturnStarted) return;
            if (Phase != TournamentPhase.Finished) return;
            PlaytestResearchSession.Emit("TournamentRestartRequested", "RestartButton");
            _manager.RestartGame();
        }

        /// <summary>
        /// action과 slot의 part 동작을 소유자별 재화와 성공 여부 및 reason으로 전달한다.
        /// key와 levelBefore 및 availableBefore를 현재 상태와 함께 보관하며 구매 및 능력 상태는 변경하지 않는다.
        /// </summary>
        private void Observe(string action, int slot, KeyboardPartDefinition part, long before, bool confirmed, bool enemy,
            bool? success = null, string reason = "", Key key = Key.None, int levelBefore = -1, int availableBefore = -1)
        {
            TournamentInventory inventory = enemy ? _enemyInventory : _inventory;
            int levelAfter = inventory == null || part == null ? -1 : key != Key.None ? inventory.GetKeycapLevel(key)
                : part is KeycapDefinition ? -1 : inventory.GetPartLevel(part);
            PlaytestShopAction?.Invoke(this, new TournamentShopObservation(action, slot, _offerGeneration,
                part != null ? part.Id : "", part != null ? part.Quantity : 0,
                action == "Reroll" || action == "RerollFailed" ? _settings.RerollCost
                    : part != null && (action == "OfferShown" || action == "PurchaseConfirmed" || action == "PurchaseFailed") ? part.Price : 0,
                before, enemy ? _enemyCurrency : Currency, confirmed, Guid.NewGuid().ToString("N"), enemy,
                part, success ?? (confirmed ? (bool?)true : action.EndsWith("Failed") ? false : (bool?)null),
                reason, key, levelBefore, levelAfter, availableBefore,
                inventory != null && part != null ? inventory.GetAvailableCount(part) : -1));
        }

        void OnDestroy()
        {
            ReleaseReturnPause(true);
            PlaytestShopStateChanged?.Invoke(this, false, "SceneExit");
            if (!_initialized) return;
            if (!string.IsNullOrEmpty(PlaytestRunId) && Phase != TournamentPhase.Finished && !_abandoned)
                PlaytestResearchSession.InterruptTournament("SceneExit");
            _manager.TournamentKeyboardCreated -= ConfigureKeyboard;
            _manager.TournamentBattleFinished -= HandleBattleFinished;
            GameManager.PlaytestRoundStarted -= HandleRoundStarted;
            _workspace.KeyPressed -= HandleShopKeyPressed;
        }

        /// <summary>새 플레이 실행에서 정적 로그 이벤트를 초기화한다. 이전 실행의 관찰자 참조를 제거한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaytestObservers()
        {
            _returnPauseOwner = null;
            _returnResumeFrame = -1;
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
        public string Type { get; }
        public int Quantity { get; }
        public long Price { get; }
        public long CurrencyBefore { get; }
        public long CurrencyAfter { get; }
        public bool Confirmed { get; }
        public string TransactionId { get; }
        public bool IsEnemy { get; }
        public KeyboardPartDefinition Part { get; }
        public bool? Success { get; }
        public string Reason { get; }
        public Key Key { get; }
        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int AvailableBefore { get; }
        public int AvailableAfter { get; }
        /// <summary>
        /// action과 slot 및 part의 상품과 재화 관찰값을 생성한다.
        /// enemy와 transactionId 및 success, reason, key, 단계와 잔여 횟수 입력을 불변 상태로 보관한다.
        /// </summary>
        public TournamentShopObservation(string action, int slot, int generation, string type, int quantity,
            long price, long before, long after, bool confirmed, string transactionId, bool enemy,
            KeyboardPartDefinition part = null, bool? success = null, string reason = "", Key key = Key.None,
            int levelBefore = -1, int levelAfter = -1, int availableBefore = -1, int availableAfter = -1)
        {
            Action = action; Slot = slot; Generation = generation; Type = type; Quantity = quantity;
            Price = price; CurrencyBefore = before; CurrencyAfter = after; Confirmed = confirmed;
            TransactionId = transactionId; IsEnemy = enemy;
            Part = part; Success = success; Reason = reason; Key = key;
            LevelBefore = levelBefore; LevelAfter = levelAfter;
            AvailableBefore = availableBefore; AvailableAfter = availableAfter;
        }
    }
}
