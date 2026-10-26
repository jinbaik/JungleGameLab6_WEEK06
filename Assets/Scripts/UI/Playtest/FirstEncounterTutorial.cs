using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Logging;
using Game.Player;
using Game.Tournament;

namespace Game.Tutorials
{
    public enum TutorialTopic
    {
        // 저장된 카드 연결을 유지하도록 현재 숫자를 고정한다.
        Welcome = 0, Workbench = 1, EditControls = 2, KeycapEditing = 3, Image = 4,
        AssemblySwitch = 5, WholePart = 6, Artisan = 7, KeycapPuller = 26,

        // 조립과 파츠별 편집 안내다.
        PartsCatalog = 17, PartRemoval = 18, PartReplacement = 19,
        PartReturn = 20, PartCompatibility = 21,
        CaseEditing = 22, PlateEditing = 23, PCBEditing = 24, LEDEditing = 25,
        ScrewAssembly = 27, UpperCaseEditing = 28, AssemblyOrder = 29,
        WorkbenchPlaced = 30,
        ToolDrawer = 31, MailInbox = 32, RequestDelivery = 33, MonitorUse = 34,
        RequestResult = 35,

        // 전투 안내는 기존 값을 유지한다.
        Battle = 8, Replenishment = 9, Shop = 10, KeycapPurchase = 11, Unlock = 12,
        Reroll = 13, TournamentEnd = 14, AttackPreview = 15, TournamentEntry = 16,
    }

    [Serializable]
    public sealed class TutorialCard
    {
        [SerializeField] private TutorialTopic _topic;
        [SerializeField] private string _title;
        [SerializeField, TextArea(3, 7)] private string _body;
        [SerializeField] private string _controls;
        public TutorialTopic Topic => _topic;
        public string Title => _title;
        public string Body => _body;
        public string Controls => _controls;
    }

    [DefaultExecutionOrder(-25000)]
    public sealed class FirstEncounterTutorial : MonoBehaviour
    {
        private static readonly HashSet<TutorialTopic> _seenTopics = new HashSet<TutorialTopic>();
        private static FirstEncounterTutorial _instance;
        private static int _resumeFrame = -1;

        [Header("Authored UI")]
        [SerializeField] private GameObject _overlay;
        [SerializeField] private Text _eyebrow;
        [SerializeField] private Text _title;
        [SerializeField] private Text _body;
        [SerializeField] private Text _controls;
        [SerializeField] private Text _nextLabel;
        [SerializeField] private KeyboardEditButton _next;
        [SerializeField] private KeyboardEditButton _previous;
        [SerializeField] private KeyboardEditButton _close;
        [SerializeField] private KeyboardEditButton _help;

        [Header("Tutorial Content")]
        [SerializeField] private TutorialContent _content;

        [Header("Runtime")]
        private readonly Queue<PendingCard> _pending = new Queue<PendingCard>();
        private readonly HashSet<TutorialTopic> _scheduled = new HashSet<TutorialTopic>();
        // private readonly List<InputAction> _actions = new List<InputAction>();
        private PendingCard _current;
        // private float _savedTimeScale;
        private CursorLockMode _savedCursorLock;
        private bool _savedCursorVisible;
        private bool _paused;
        private bool _closing;
        private bool _browsing;
        private int _page;
        private int _closeFrame;
        private WorkshopKeycapDrawerTransition _drawerTransition;
        private MonitorMiniGameManager _monitorManager;
        private MonitorKeyboardHolder _monitorHolder;
        private readonly HashSet<string> _removedPartTypes = new HashSet<string>();

        // 안내 표시 중에는 게임 조작을 차단하지 않는다.
        // public static bool IsBlocking => (_instance != null && _instance._paused) || Time.frameCount <= _resumeFrame;
        public static bool IsBlocking => Time.frameCount <= _resumeFrame;
        public static bool IsVisible => _instance != null && _instance._paused;
        public bool CanOpenHelp => !Game.Workshop.WorkshopDeliveryTray.IsInputBlocked && !Game.Tournament.TournamentController.IsReturnPaused && !IsBlocking
            && !_paused && _pending.Count == 0 && Time.timeScale > 0f;
        private IReadOnlyList<TutorialCard> Cards => _content.Cards;

        private sealed class PendingCard
        {
            public TutorialTopic Topic { get; set; }
            public Action Completed { get; set; }
            public bool AllowHeldInput { get; set; }
        }

        /// <summary>새 실행의 정적 참조와 열람 항목을 초기화한다. 입력값 없이 모든 첫 조우 안내를 다시 표시할 수 있게 한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            _instance = null;
            _resumeFrame = -1;
            _seenTopics.Clear();
        }

        void Awake()
        {
            _instance = this;
            _overlay.SetActive(false);
        }

        void OnEnable()
        {
            _drawerTransition = FindFirstObjectByType<WorkshopKeycapDrawerTransition>();
            _monitorManager = FindFirstObjectByType<MonitorMiniGameManager>();
            _monitorHolder = FindFirstObjectByType<MonitorKeyboardHolder>();
            // if (_drawerTransition != null) _drawerTransition.TrayStateChanged += HandleDrawerStateChanged;
            // if (_monitorManager != null) _monitorManager.GameChanged += HandleMonitorGameChanged;
            // if (_monitorHolder != null) _monitorHolder.KeyboardChanged += HandleMonitorKeyboardChanged;
            // KeyboardRequestRuntime.Changed += HandleRequestChanged;
            _next.Clicked += HandleNext;
            _previous.Clicked += HandlePrevious;
            _close.Clicked += HandleClose;
            _help.Clicked += HandleHelp;
            // CustomizationTelemetry.Observed += HandleCustomization;
            // TournamentController.PlaytestShopStateChanged += HandleShop;
            // TournamentController.PlaytestShopAction += HandleShopAction;
            // PlaytestResearchSession.Observed += HandleResearch;
        }

        void Start()
        {
            // if (HasCard(TutorialTopic.AssemblyOrder))
            // {
                // Request(TutorialTopic.Welcome);
                // Request(TutorialTopic.Workbench);
            // }
            // else if (SceneManager.GetActiveScene().name == "WorkShop2") Request(TutorialTopic.Welcome);
            // else if (FindFirstObjectByType<TournamentController>() != null) Request(TutorialTopic.Battle);
        }

        void Update()
        {
            if (Game.Tournament.TournamentController.IsReturnPaused || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked) return;
            if (!_paused && Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                HandleHelp(_help);
            if (!_paused) return;
            // Time.timeScale = 0f;
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
            if (_closing && Time.frameCount > _closeFrame && InputsReleased()) FinishClose();
        }

        void LateUpdate()
        {
            if (Game.Tournament.TournamentController.IsReturnPaused || Game.Workshop.WorkshopDeliveryTray.IsInputBlocked) return;
            if (_paused)
            {
                // Time.timeScale = 0f;
                return;
            }
            if (Time.frameCount <= _resumeFrame || _pending.Count == 0 || Time.timeScale <= 0f) return;
            if (!_pending.Peek().AllowHeldInput && !InputsReleased()) return;
            _current = _pending.Dequeue();
            _browsing = false;
            Open(FindCard(_current.Topic));
        }

        void OnDisable()
        {
            // if (_drawerTransition != null) _drawerTransition.TrayStateChanged -= HandleDrawerStateChanged;
            // if (_monitorManager != null) _monitorManager.GameChanged -= HandleMonitorGameChanged;
            // if (_monitorHolder != null) _monitorHolder.KeyboardChanged -= HandleMonitorKeyboardChanged;
            // KeyboardRequestRuntime.Changed -= HandleRequestChanged;
            _next.Clicked -= HandleNext;
            _previous.Clicked -= HandlePrevious;
            _close.Clicked -= HandleClose;
            _help.Clicked -= HandleHelp;
            // CustomizationTelemetry.Observed -= HandleCustomization;
            // TournamentController.PlaytestShopStateChanged -= HandleShop;
            // TournamentController.PlaytestShopAction -= HandleShopAction;
            // PlaytestResearchSession.Observed -= HandleResearch;
            if (_paused)
            {
                PlaytestResearchSession.Emit("TutorialInterrupted", _title.text);
                RestorePause();
            }
            _pending.Clear();
            _scheduled.Clear();
            _removedPartTypes.Clear();
            if (_instance == this) _instance = null;
        }

        /// <summary>topic과 completed를 받지만 자동 안내를 중단한 동안 예약 상태를 변경하지 않고 false를 반환한다.</summary>
        public static bool Request(TutorialTopic topic, Action completed = null)
        {
            // if (_instance == null || _seenTopics.Contains(topic)) return false;
            // if (_instance._scheduled.Add(topic))
                // _instance._pending.Enqueue(new PendingCard { Topic = topic, Completed = completed });
            // else if (completed != null)
            // {
                // if (_instance._current != null && _instance._current.Topic == topic)
                    // _instance._current.Completed ??= completed;
                // else
                    // foreach (PendingCard card in _instance._pending)
                        // if (card.Topic == topic) card.Completed ??= completed;
            // }
            // return true;
            return false;
        }

        /// <summary>topic의 Inspector 안내 데이터를 찾아 반환한다. 등록되지 않은 항목은 구성 오류로 보고한다.</summary>
        private TutorialCard FindCard(TutorialTopic topic)
        {
            foreach (TutorialCard card in Cards) if (card.Topic == topic) return card;
            throw new InvalidOperationException("튜토리얼 카드 누락: " + topic);
        }

        /// <summary>card를 표시하고 기존 커서 상태를 보관한다. 시간과 게임 입력은 유지하며 안내 표시 상태만 활성화한다.</summary>
        private void Open(TutorialCard card)
        {
            // _savedTimeScale = Time.timeScale;
            _savedCursorLock = InGameCursor.LockState;
            _savedCursorVisible = InGameCursor.Visible;
            _paused = true;
            /*
            _actions.Clear();
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                if (string.Equals(action.actionMap?.name, "UI", StringComparison.OrdinalIgnoreCase)) continue;
                _actions.Add(action);
                action.Disable();
            }
            Time.timeScale = 0f;
            */
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
            _overlay.SetActive(true);
            Display(card);
            PlaytestResearchSession.Emit("TutorialShown", (_browsing ? "Help:" : "First:") + card.Topic);
        }

        /// <summary>card의 제목과 설명 및 조작을 기존 UI에 반영한다. 도움말에서는 페이지 이동 버튼을 표시한다.</summary>
        private void Display(TutorialCard card)
        {
            _eyebrow.text = _browsing ? "도움말  ·  " + (_page + 1) + " / " + Cards.Count : "처음 만나는 기능";
            _title.text = card.Title;
            _body.text = card.Body;
            _controls.text = card.Controls;
            _nextLabel.text = _browsing && _page < Cards.Count - 1 ? "다음" : "계속";
            _previous.gameObject.SetActive(_browsing && _page > 0);
            _close.gameObject.SetActive(_browsing);
            _next.interactable = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>button의 다음 요청으로 도움말 페이지를 넘기거나 첫 안내를 확인 처리한다. 읽은 항목은 현재 실행의 메모리에만 보관한다.</summary>
        private void HandleNext(KeyboardEditButton button)
        {
            if (_closing) return;
            if (_browsing && _page < Cards.Count - 1)
            {
                _page++;
                Display(Cards[_page]);
                return;
            }
            if (!_browsing)
            {
                _seenTopics.Add(_current.Topic);
            }
            BeginClose();
        }

        /// <summary>button의 이전 요청으로 도움말 페이지를 한 장 되돌린다. 저장된 첫 조우 상태는 바꾸지 않는다.</summary>
        private void HandlePrevious(KeyboardEditButton button)
        {
            if (_closing || !_browsing || _page == 0) return;
            _page--;
            Display(Cards[_page]);
        }

        /// <summary>button의 닫기 요청으로 도움말을 종료한다. 미열람 첫 조우 카드는 별도로 유지한다.</summary>
        private void HandleClose(KeyboardEditButton button)
        {
            if (!_closing) BeginClose();
        }

        /// <summary>button의 도움말 요청으로 첫 페이지를 연다. 다른 안내나 입력 해제 대기 중에는 중복으로 열지 않는다.</summary>
        private void HandleHelp(KeyboardEditButton button)
        {
            if (!CanOpenHelp) return;
            _page = 0;
            _browsing = true;
            Open(Cards[0]);
        }

        /// <summary>확인 버튼을 비활성화하고 입력 해제를 기다린다. 게임을 정지하지 않고 안내의 닫기 대기 상태만 변경한다.</summary>
        private void BeginClose()
        {
            _closing = true;
            _closeFrame = Time.frameCount;
            _next.interactable = false;
            _controls.text = "누르고 있는 키와 마우스 버튼에서 손을 떼면 안내가 닫힙니다.";
        }

        /// <summary>누른 키와 마우스 버튼이 모두 해제됐는지 반환한다. 입력을 소비하거나 게임 상태를 변경하지 않는다.</summary>
        private static bool InputsReleased()
        {
            if (Keyboard.current != null)
                foreach (var key in Keyboard.current.allKeys) if (key.isPressed) return false;
            Mouse mouse = Mouse.current;
            return mouse == null || (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed);
        }

        /// <summary>입력 해제 후 안내를 닫고 보관한 게임 상태를 복원한다. 첫 조우 완료 콜백은 복원한 뒤 한 번 실행한다.</summary>
        private void FinishClose()
        {
            Action completed = !_browsing ? _current.Completed : null;
            string topic = _browsing ? "Help" : _current.Topic.ToString();
            if (!_browsing) _scheduled.Remove(_current.Topic);
            _current = null;
            _overlay.SetActive(false);
            RestorePause();
            _resumeFrame = Time.frameCount + 1;
            PlaytestResearchSession.Emit("TutorialClosed", topic);
            completed?.Invoke();
        }

        /// <summary>보관한 커서 상태를 복원하고 안내 표시와 닫기 대기를 해제한다. 현재 시간 배율과 게임 입력은 변경하지 않는다.</summary>
        private void RestorePause()
        {
            /*
            Time.timeScale = _savedTimeScale;
            foreach (InputAction action in _actions) action.Enable();
            _actions.Clear();
            */
            InGameCursor.LockState = _savedCursorLock;
            InGameCursor.Visible = _savedCursorVisible;
            _paused = false;
            _closing = false;
        }

        /// <summary>observation의 실제 편집 진입과 기능 선택에 맞는 안내를 예약한다. 자동 복원이나 실패 동작은 안내 조건으로 쓰지 않는다.</summary>
        private void HandleCustomization(CustomizationObservation observation)
        {
            if (observation.Type == "PartChanged" && observation.Details == "DrawerTransition") return;
            if (HasCard(TutorialTopic.AssemblyOrder))
            {
                HandleAssemblyCustomization(observation);
                return;
            }
            bool panelOpened = observation.Type == "PartChanged" || observation.Type == "PartPanelOpened";
            if (panelOpened && observation.Part == "Keycap") Request(TutorialTopic.KeycapEditing);
            if (panelOpened && observation.Part == "CustomImage") Request(TutorialTopic.Image);
            if (observation.Type == "CustomizationTestModeChanged" && observation.Element == "AttackEnabled") Request(TutorialTopic.AttackPreview);
            if (observation.Type == "ElementPicked" && observation.KeycapKind == "Artisan") Request(TutorialTopic.Artisan);
        }

        /// <summary>
        /// topic 카드가 현재 연결된 콘텐츠에 등록되어 있는지 반환한다.
        /// 다른 콘텐츠에서 제공하지 않는 조립 안내를 요청하지 않도록 데이터 존재 여부만 검사한다.
        /// </summary>
        private bool HasCard(TutorialTopic topic)
        {
            foreach (TutorialCard card in Cards)
                if (card.Topic == topic) return true;
            return false;
        }

        /// <summary>
        /// 현재 콘텐츠에 등록된 topic만 첫 조우 안내 대기열에 추가한다.
        /// allowHeldInput이면 입력 해제를 기다리지 않도록 설정하고 해당 안내를 대기열 맨 앞으로 옮긴다.
        /// </summary>
        private void RequestAvailable(TutorialTopic topic, bool allowHeldInput = false)
        {
            if (!HasCard(topic) || !Request(topic) || !allowHeldInput) return;
            PendingCard[] pending = _pending.ToArray();
            PendingCard immediate = Array.Find(pending, card => card.Topic == topic);
            if (immediate == null) return;
            immediate.AllowHeldInput = true;
            _pending.Clear();
            _pending.Enqueue(immediate);
            foreach (PendingCard card in pending)
                if (card != immediate) _pending.Enqueue(card);
        }

        /// <summary>
        /// observation의 작업 모드 진입과 부품별 실제 조작에 맞는 조립 안내를 예약한다.
        /// 성공 여부와 실패 원인을 구분하며 부품이나 씬 상태는 변경하지 않는다.
        /// </summary>
        private void HandleAssemblyCustomization(CustomizationObservation observation)
        {
            if (_drawerTransition != null
                && (observation.Type == "PartChanged" || observation.Type == "PartPanelOpened")
                && !_drawerTransition.IsTrayOpen) return;
            if ((observation.Type == "PartChanged" || observation.Type == "PartPanelOpened")
                && observation.Target == null) return;
            if (observation.Type == "WorkbenchPlaced")
                RequestAvailable(TutorialTopic.WorkbenchPlaced);
            if (observation.Type == "WorkshopOpened")
            {
                RequestAvailable(TutorialTopic.EditControls);
                RequestAvailable(TutorialTopic.AssemblyOrder);
            }
            if ((observation.Type == "PartChanged" || observation.Type == "PartPanelOpened")
                && !string.IsNullOrEmpty(observation.Part))
            {
                RequestAvailable(TutorialTopic.PartsCatalog);
                switch (observation.Part)
                {
                    case "Lower": case "Case": RequestAvailable(TutorialTopic.CaseEditing); break;
                    case "Pcb":
                        RequestAvailable(TutorialTopic.PCBEditing);
                        break;
                    case "Plate": RequestAvailable(TutorialTopic.PlateEditing); break;
                    case "Upper": RequestAvailable(TutorialTopic.UpperCaseEditing); break;
                    case "Switch": RequestAvailable(TutorialTopic.AssemblySwitch); break;
                    case "Keycap": RequestAvailable(TutorialTopic.KeycapEditing); break;
                    case "LED": RequestAvailable(TutorialTopic.LEDEditing); break;
                    case "CustomImage": RequestAvailable(TutorialTopic.Image); break;
                }
            }
            if (observation.Type == "BulkPlacementStarted") RequestAvailable(TutorialTopic.WholePart, true);
            if (observation.Type == "InventoryPartPicked" && _removedPartTypes.Remove(observation.Part))
                RequestAvailable(TutorialTopic.PartReplacement, true);
            if ((observation.Type == "InventoryPartPicked" || observation.Type == "ElementPicked")
                && observation.KeycapKind == "Artisan") RequestAvailable(TutorialTopic.Artisan);
            if (observation.Type == "PartInstallAttempted" && observation.Success && observation.Part == "Pcb")
                RequestAvailable(TutorialTopic.ScrewAssembly, true);
            if (observation.Type == "PartRemoveAttempted" && observation.Success)
            {
                if (observation.Part == "Keycap" || observation.Part == "Switch")
                    _removedPartTypes.Add(observation.Part);
                else
                    RequestAvailable(TutorialTopic.PartRemoval);
                RequestAvailable(TutorialTopic.PartReturn);
            }
            if (observation.Type == "PartReturned" && observation.Success) RequestAvailable(TutorialTopic.PartReturn);
            if ((observation.Type == "PartInstallAttempted" || observation.Type == "PartRemoveAttempted")
                && !observation.Success && !string.IsNullOrEmpty(observation.OutcomeReason))
                RequestAvailable(TutorialTopic.PartCompatibility);
            if (observation.Type == "CustomizationTestModeChanged" && observation.Element == "AttackEnabled")
                RequestAvailable(TutorialTopic.AttackPreview);
        }

        /// <summary>
        /// category 서랍이 완전히 열린 opened 상태에서 해당 부품의 첫 안내를 예약한다.
        /// 닫기와 전환 중 알림은 무시하고 현재 콘텐츠에 등록된 카드만 표시한다.
        /// </summary>
        private void HandleDrawerStateChanged(WorkshopDrawerCategory category, bool opened)
        {
            if (!opened) return;
            RequestAvailable(TutorialTopic.PartsCatalog);
            switch (category)
            {
                case WorkshopDrawerCategory.Keycaps: RequestAvailable(TutorialTopic.KeycapEditing); break;
                case WorkshopDrawerCategory.Switches: RequestAvailable(TutorialTopic.AssemblySwitch); break;
                case WorkshopDrawerCategory.ScrewsTools:
                    RequestAvailable(TutorialTopic.KeycapPuller, true);
                    break;
                case WorkshopDrawerCategory.LedCustomImage: RequestAvailable(TutorialTopic.LEDEditing); break;
                case WorkshopDrawerCategory.UpperHousing: RequestAvailable(TutorialTopic.UpperCaseEditing); break;
                case WorkshopDrawerCategory.Plate: RequestAvailable(TutorialTopic.PlateEditing); break;
                case WorkshopDrawerCategory.Pcb: RequestAvailable(TutorialTopic.PCBEditing); break;
                case WorkshopDrawerCategory.LowerHousing: RequestAvailable(TutorialTopic.CaseEditing); break;
            }
        }

        /// <summary>
        /// game이 메일 앱이면 고객별 목록과 의뢰 수락 안내를 예약한다.
        /// 다른 앱과 종료 알림은 무시하며 메일과 의뢰 상태는 변경하지 않는다.
        /// </summary>
        private void HandleMonitorGameChanged(MonitorMiniGame game)
        {
            if (game is MonitorMailGame) RequestAvailable(TutorialTopic.MailInbox);
        }

        /// <summary>
        /// installed가 참이면 모니터 사용 안내를 예약한다.
        /// 키보드 설치 상태는 수정하지 않고 연결된 콘텐츠의 안내만 요청한다.
        /// </summary>
        private void HandleMonitorKeyboardChanged(bool installed)
        {
            if (installed) RequestAvailable(TutorialTopic.MonitorUse);
        }

        /// <summary>
        /// 런타임 의뢰 변경에서 진행 의뢰와 결과 메일 도착을 확인한다.
        /// 해당 안내만 한 번 예약하며 의뢰 진행과 보상은 변경하지 않는다.
        /// </summary>
        private void HandleRequestChanged()
        {
            if (KeyboardRequestRuntime.ActiveRequest != null) RequestAvailable(TutorialTopic.RequestDelivery);
            foreach (KeyboardRequestState state in KeyboardRequestRuntime.Requests.Values)
                if (state.ResultMailSent) { RequestAvailable(TutorialTopic.RequestResult); break; }
        }

        /// <summary>source 상점의 open 상태를 받아 첫 상점 진입을 안내한다. 종료 통지는 안내를 만들지 않는다.</summary>
        private void HandleShop(TournamentController source, bool open, string reason)
        {
            if (open) Request(TutorialTopic.Shop);
        }

        /// <summary>source의 성공한 플레이어 구매와 배치를 관찰해 후속 안내를 예약한다. 로봇 거래는 제외한다.</summary>
        private void HandleShopAction(TournamentController source, TournamentShopObservation observation)
        {
            if (observation.IsEnemy) return;
            if (observation.Action == "PurchaseConfirmed")
                Request(observation.Part is KeycapDefinition ? TutorialTopic.KeycapPurchase : TutorialTopic.WholePart);
            if (observation.Action == "AbilityUnlocked" || observation.Action == "AbilityUpgraded") Request(TutorialTopic.Unlock);
        }

        /// <summary>실제 토너먼트 종료 type을 받아 재화 소멸과 재도전 안내를 예약한다. 나머지 연구 이벤트는 무시한다.</summary>
        private void HandleResearch(string type, string reason, string eventId)
        {
            if (type == "TournamentFinished") Request(TutorialTopic.TournamentEnd);
        }
    }
}
