using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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
        private readonly List<InputAction> _actions = new List<InputAction>();
        private PendingCard _current;
        private float _savedTimeScale;
        private CursorLockMode _savedCursorLock;
        private bool _savedCursorVisible;
        private bool _paused;
        private bool _closing;
        private bool _browsing;
        private int _page;
        private int _closeFrame;

        public static bool IsBlocking => (_instance != null && _instance._paused) || Time.frameCount <= _resumeFrame;
        private IReadOnlyList<TutorialCard> Cards => _content.Cards;

        private sealed class PendingCard
        {
            public TutorialTopic Topic { get; set; }
            public Action Completed { get; set; }
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
            _next.Clicked += HandleNext;
            _previous.Clicked += HandlePrevious;
            _close.Clicked += HandleClose;
            _help.Clicked += HandleHelp;
            CustomizationTelemetry.Observed += HandleCustomization;
            TournamentController.PlaytestShopStateChanged += HandleShop;
            TournamentController.PlaytestShopAction += HandleShopAction;
            PlaytestResearchSession.Observed += HandleResearch;
        }

        void Start()
        {
            if (SceneManager.GetActiveScene().name == "WorkShop2") Request(TutorialTopic.Welcome);
            else if (FindFirstObjectByType<TournamentController>() != null) Request(TutorialTopic.Battle);
        }

        void Update()
        {
            if (Game.Tournament.TournamentController.IsReturnPaused) return;
            if (!_paused && Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                HandleHelp(_help);
            if (!_paused) return;
            Time.timeScale = 0f;
            InGameCursor.LockState = CursorLockMode.None;
            InGameCursor.Visible = true;
            if (_closing && Time.frameCount > _closeFrame && InputsReleased()) FinishClose();
        }

        void LateUpdate()
        {
            if (Game.Tournament.TournamentController.IsReturnPaused) return;
            if (_paused)
            {
                Time.timeScale = 0f;
                return;
            }
            if (Time.frameCount <= _resumeFrame || _pending.Count == 0 || Time.timeScale <= 0f || !InputsReleased()) return;
            _current = _pending.Dequeue();
            _browsing = false;
            Open(FindCard(_current.Topic));
        }

        void OnDisable()
        {
            _next.Clicked -= HandleNext;
            _previous.Clicked -= HandlePrevious;
            _close.Clicked -= HandleClose;
            _help.Clicked -= HandleHelp;
            CustomizationTelemetry.Observed -= HandleCustomization;
            TournamentController.PlaytestShopStateChanged -= HandleShop;
            TournamentController.PlaytestShopAction -= HandleShopAction;
            PlaytestResearchSession.Observed -= HandleResearch;
            if (_paused)
            {
                PlaytestResearchSession.Emit("TutorialInterrupted", _title.text);
                RestorePause();
            }
            _pending.Clear();
            _scheduled.Clear();
            if (_instance == this) _instance = null;
        }

        /// <summary>topic이 미열람이면 완료 후 실행할 completed와 함께 예약한다. 기존 예약의 빈 콜백만 채우고 예약 여부를 반환한다.</summary>
        public static bool Request(TutorialTopic topic, Action completed = null)
        {
            if (_instance == null || _seenTopics.Contains(topic)) return false;
            if (_instance._scheduled.Add(topic))
                _instance._pending.Enqueue(new PendingCard { Topic = topic, Completed = completed });
            else if (completed != null)
            {
                if (_instance._current != null && _instance._current.Topic == topic)
                    _instance._current.Completed ??= completed;
                else
                    foreach (PendingCard card in _instance._pending)
                        if (card.Topic == topic) card.Completed ??= completed;
            }
            return true;
        }

        /// <summary>topic의 Inspector 안내 데이터를 찾아 반환한다. 등록되지 않은 항목은 구성 오류로 보고한다.</summary>
        private TutorialCard FindCard(TutorialTopic topic)
        {
            foreach (TutorialCard card in Cards) if (card.Topic == topic) return card;
            throw new InvalidOperationException("튜토리얼 카드 누락: " + topic);
        }

        /// <summary>card를 표시하고 현재 시간 배율과 커서 및 게임 입력을 보관한다. UI 입력은 유지한 채 게임을 일시정지한다.</summary>
        private void Open(TutorialCard card)
        {
            _savedTimeScale = Time.timeScale;
            _savedCursorLock = InGameCursor.LockState;
            _savedCursorVisible = InGameCursor.Visible;
            _paused = true;
            _actions.Clear();
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                if (string.Equals(action.actionMap?.name, "UI", StringComparison.OrdinalIgnoreCase)) continue;
                _actions.Add(action);
                action.Disable();
            }
            Time.timeScale = 0f;
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
            if (Game.Tournament.TournamentController.IsReturnPaused || IsBlocking || _pending.Count > 0 || Time.timeScale <= 0f) return;
            _page = 0;
            _browsing = true;
            Open(Cards[0]);
        }

        /// <summary>확인 버튼을 비활성화하고 입력 해제를 기다린다. 닫기 입력이 게임으로 전달되지 않도록 정지 상태를 유지한다.</summary>
        private void BeginClose()
        {
            _closing = true;
            _closeFrame = Time.frameCount;
            _next.interactable = false;
            _controls.text = "누르고 있는 키와 마우스 버튼에서 손을 떼면 계속합니다.";
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

        /// <summary>보관한 시간 배율과 입력 및 커서 상태를 복원한다. 종료나 씬 전환에서도 튜토리얼의 정지 소유권을 해제한다.</summary>
        private void RestorePause()
        {
            Time.timeScale = _savedTimeScale;
            foreach (InputAction action in _actions) action.Enable();
            _actions.Clear();
            InGameCursor.LockState = _savedCursorLock;
            InGameCursor.Visible = _savedCursorVisible;
            _paused = false;
            _closing = false;
        }

        /// <summary>observation의 실제 편집 진입과 기능 선택에 맞는 안내를 예약한다. 자동 복원이나 실패 동작은 안내 조건으로 쓰지 않는다.</summary>
        private void HandleCustomization(CustomizationObservation observation)
        {
            if (observation.Type == "PartChanged" && observation.Part == "Keycap") Request(TutorialTopic.KeycapEditing);
            if (observation.Type == "PartChanged" && observation.Part == "CustomImage") Request(TutorialTopic.Image);
            if (observation.Type == "CustomizationTestModeChanged" && observation.Element == "AttackEnabled") Request(TutorialTopic.AttackPreview);
            if (observation.Type == "ElementPicked" && observation.KeycapKind == "Artisan") Request(TutorialTopic.Artisan);
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
