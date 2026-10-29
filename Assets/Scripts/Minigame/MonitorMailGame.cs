using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using Game.Logging;

namespace Game.Player
{
    public sealed class MonitorMailGame : MonitorMiniGame
    {
        [Header("Inbox UI")]
        [SerializeField] private Transform _listRoot;
        [SerializeField] private ScrollRect _listScroll;
        [SerializeField] private MonitorMailListItem _rowTemplate;
        [SerializeField] private Text _count;
        [SerializeField] private GameObject _emptyState;
        private readonly List<MonitorMailListItem> _rows = new List<MonitorMailListItem>();
        private int _selectedIndex = -1;
        private string _selectedMailId;

        [Header("Mailbox UI")]
        [SerializeField] private MonitorOSButton _deleteMail;
        [SerializeField] private MonitorOSButton _restoreMail;
        private readonly List<MonitorMailListItem> _folders = new List<MonitorMailListItem>();
        private readonly HashSet<string> _expandedCustomers = new HashSet<string>();
        private bool _trashExpanded;

        [Header("Message UI")]
        [SerializeField] private GameObject _messagePanel;
        [SerializeField] private Text _subject;
        [SerializeField] private Text _sender;
        [SerializeField] private Text _date;
        [SerializeField] private Text _body;
        [SerializeField] private ScrollRect _bodyScroll;

        [Header("Request UI")]
        [SerializeField] private Text _requestInfo;
        [SerializeField] private GameObject _requestActions;
        [SerializeField] private Text _activeRequestText;
        [SerializeField] private MonitorOSButton _acceptRequest;
        [SerializeField] private MonitorOSButton _closeRequest;
        private string _requestNotice;

        /// <summary>
        /// definition이 메일 앱 데이터인지 검사하여 실행 가능 여부를 반환한다.
        /// 빈 받은편지함도 유효하며 화면 상태는 변경하지 않는다.
        /// </summary>
        public override bool CanRun(MonitorMiniGameDefinition definition)
        {
            return definition is MonitorMailGameDefinition;
        }

        /// <summary>
        /// 현재 Definition의 최초 메일을 런타임 받은편지함에 반영하고 첫 메일을 연다.
        /// 유효한 메일 수와 빈 목록 표시 및 클릭 이벤트 구독을 초기화한다.
        /// </summary>
        protected override void OnStarted()
        {
            KeyboardRequestRuntime.Initialize((MonitorMailGameDefinition)Definition);
            KeyboardRequestRuntime.MailReceived += HandleMailReceived;
            KeyboardRequestRuntime.MailboxChanged += RefreshInbox;
            _deleteMail.Clicked += HandleDeleteMail;
            _restoreMail.Clicked += HandleRestoreMail;
            KeyboardRequestRuntime.Changed += RefreshRequestUI;
            _acceptRequest.Clicked += HandleAcceptRequest;
            _closeRequest.Clicked += HandleCloseRequest;
            RefreshInbox();
        }

        /// <summary>
        /// 런타임 받은편지함의 메일로 화면 목록을 다시 구성한다.
        /// 기존 선택을 MailId로 복원하고 목록과 본문 스크롤을 가능한 범위에서 유지하며 의뢰 진행 상태는 변경하지 않는다.
        /// </summary>
        private void RefreshInbox()
        {
            Vector2 listPosition = _listScroll.content.anchoredPosition;
            Vector2 bodyPosition = _bodyScroll.content.anchoredPosition;
            ClearRows();
            _rowTemplate.gameObject.SetActive(false);
            var customers = new Dictionary<string, List<MonitorMailEntry>>();
            var trash = new List<MonitorMailEntry>();
            int inboxCount = 0;
            foreach (MonitorMailEntry message in KeyboardRequestRuntime.Inbox)
            {
                if (message == null) continue;
                if (message.IsDeleted)
                {
                    trash.Add(message);
                    continue;
                }
                string key = GetCustomerKey(message);
                if (!customers.TryGetValue(key, out List<MonitorMailEntry> messages))
                {
                    messages = new List<MonitorMailEntry>();
                    customers.Add(key, messages);
                }
                messages.Add(message);
                inboxCount++;
            }
            foreach (KeyValuePair<string, List<MonitorMailEntry>> customer in customers)
            {
                bool expanded = _expandedCustomers.Contains(customer.Key);
                CreateFolder(customer.Key, customer.Value[0].Sender, customer.Value.Count, expanded);
                if (expanded) foreach (MonitorMailEntry message in customer.Value) CreateMailRow(message);
            }
            CreateFolder(null, "휴지통", trash.Count, _trashExpanded);
            if (_trashExpanded) foreach (MonitorMailEntry message in trash) CreateMailRow(message);
            _count.text = $"받은편지함 {inboxCount}통 / 휴지통 {trash.Count}통";
            _emptyState.SetActive(_rows.Count == 0);
            _messagePanel.SetActive(_rows.Count > 0);
            int selectedIndex = _rows.FindIndex(row => row.Message.MailId == _selectedMailId);
            if (_rows.Count > 0) SelectMessage(Mathf.Max(0, selectedIndex));
            else
            {
                _selectedMailId = null;
                _emptyState.GetComponent<Text>().text = inboxCount + trash.Count == 0
                    ? "도착한 메일이 없습니다." : "고객 또는 휴지통을 펼쳐 메일을 선택하세요.";
                RefreshRequestUI();
            }
            RestoreScrollPosition(_listScroll, listPosition);
            if (selectedIndex >= 0) RestoreScrollPosition(_bodyScroll, bodyPosition);
        }

        /// <summary>
        /// 생성한 메일 행의 이벤트를 해제하고 목록을 정리한다.
        /// 선택 상태를 초기화하며 원본 메일 에셋은 변경하지 않는다.
        /// </summary>
        protected override void OnStopped()
        {
            KeyboardRequestRuntime.MailReceived -= HandleMailReceived;
            KeyboardRequestRuntime.MailboxChanged -= RefreshInbox;
            _deleteMail.Clicked -= HandleDeleteMail;
            _restoreMail.Clicked -= HandleRestoreMail;
            KeyboardRequestRuntime.Changed -= RefreshRequestUI;
            _acceptRequest.Clicked -= HandleAcceptRequest;
            _closeRequest.Clicked -= HandleCloseRequest;
            ClearRows();
            _selectedMailId = null;
            _requestNotice = null;
        }

        /// <summary>
        /// 새로 수신된 message를 포함하도록 받은편지함 화면을 갱신한다.
        /// 입력 메일의 수신 상태는 런타임 저장소가 소유하며 여기서는 목록만 다시 표시한다.
        /// </summary>
        private void HandleMailReceived(MonitorMailEntry message)
        {
            RefreshInbox();
        }

        /// <summary>
        /// 현재 화면 행의 이벤트를 해제하고 생성된 행을 제거한다.
        /// 입력과 반환값 없이 화면 선택을 초기화하며 런타임 받은편지함은 유지한다.
        /// </summary>
        private void ClearRows()
        {
            foreach (MonitorMailListItem folder in _folders)
            {
                folder.Selected -= HandleFolderSelected;
                folder.gameObject.SetActive(false);
                Destroy(folder.gameObject);
            }
            _folders.Clear();
            foreach (MonitorMailListItem row in _rows)
            {
                row.Selected -= HandleSelected;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            _rows.Clear();
            _selectedIndex = -1;
        }

        /// <summary>
        /// message의 주소를 고객 그룹 식별자로 반환한다.
        /// 주소가 없는 메일은 발신자 이름을 사용하며 원본 데이터를 변경하지 않는다.
        /// </summary>
        private string GetCustomerKey(MonitorMailEntry message)
        {
            return string.IsNullOrWhiteSpace(message.Address)
                ? message.Sender : message.Address.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// key와 label 및 count로 현재 expanded 상태의 폴더 행을 생성한다.
        /// 클릭 이벤트를 연결하고 생성된 행을 정리 목록에 등록한다.
        /// </summary>
        private void CreateFolder(string key, string label, int count, bool expanded)
        {
            MonitorMailListItem folder = Instantiate(_rowTemplate, _listRoot, false);
            folder.InitializeFolder(key, label, count, expanded);
            folder.Selected += HandleFolderSelected;
            folder.gameObject.SetActive(true);
            _folders.Add(folder);
        }

        /// <summary>
        /// message를 표시하는 하위 메일 행을 생성하고 선택 이벤트를 연결한다.
        /// 제목과 발신자 정보를 들여쓰기하여 고객 하위 목록을 구성한다.
        /// </summary>
        private void CreateMailRow(MonitorMailEntry message)
        {
            MonitorMailListItem row = Instantiate(_rowTemplate, _listRoot, false);
            row.Initialize(message);
            row.Selected += HandleSelected;
            row.gameObject.SetActive(true);
            _rows.Add(row);
        }

        /// <summary>
        /// folder의 식별자로 고객 또는 휴지통의 펼침 상태를 반전한다.
        /// 변경된 상태로 목록을 재구성하며 메일과 의뢰 데이터는 유지한다.
        /// </summary>
        private void HandleFolderSelected(MonitorMailListItem folder)
        {
            if (folder.FolderKey == null) _trashExpanded = !_trashExpanded;
            else if (!_expandedCustomers.Remove(folder.FolderKey)) _expandedCustomers.Add(folder.FolderKey);
            RefreshInbox();
        }

        /// <summary>
        /// button 클릭으로 선택 메일을 휴지통으로 이동한다.
        /// 의뢰 진행과 보상은 변경하지 않고 런타임 메일함만 갱신한다.
        /// </summary>
        private void HandleDeleteMail(MonitorOSButton button)
        {
            if (_selectedIndex >= 0) KeyboardRequestRuntime.TrySetMailDeleted(_selectedMailId, true);
        }

        /// <summary>
        /// button 클릭으로 선택한 휴지통 메일을 원래 고객 목록에 복원한다.
        /// 복원할 고객을 펼치며 기존 의뢰 상태와 메일 식별자는 유지한다.
        /// </summary>
        private void HandleRestoreMail(MonitorOSButton button)
        {
            if (_selectedIndex < 0) return;
            _expandedCustomers.Add(GetCustomerKey(_rows[_selectedIndex].Message));
            KeyboardRequestRuntime.TrySetMailDeleted(_selectedMailId, false);
        }

        /// <summary>
        /// 클릭한 row의 목록 인덱스로 메일 본문을 연다.
        /// 다른 행의 강조를 해제하고 선택된 메일 정보로 화면을 갱신한다.
        /// </summary>
        private void HandleSelected(MonitorMailListItem row)
        {
            SelectMessage(_rows.IndexOf(row), true);
        }

        /// <summary>
        /// index의 메일 발신자와 날짜 및 본문을 표시한다.
        /// userSelected로 직접 선택 여부를 구분해 표시 로그를 남기고 목록 강조와 스크롤 위치를 갱신한다.
        /// </summary>
        private void SelectMessage(int index, bool userSelected = false)
        {
            _selectedIndex = index;
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetSelected(i == index);
            MonitorMailEntry message = _rows[index].Message;
            bool sameMessage = _selectedMailId == message.MailId;
            _selectedMailId = message.MailId;
            if (sameMessage)
            {
                RefreshRequestUI();
                return;
            }
            _requestNotice = null;
            _subject.text = message.Subject;
            _sender.text = $"{message.Sender}  <{message.Address}>";
            _date.text = message.Date;
            _body.text = message.Body;
            RefreshRequestUI();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyScroll.content);
            _bodyScroll.verticalNormalizedPosition = 1f;
            if (message.Request != null)
                CustomizationLogger.RecordRequestEvent("RequestMailViewed", message.Request.RequestId,
                    message.MailId, userSelected ? "UserSelection" : "AutoSelection");
        }

        /// <summary>
        /// 현재 선택 메일에 명시적으로 연결된 의뢰의 런타임 상태를 반환한다.
        /// 안내 메일, 빈 목록과 등록되지 않은 의뢰는 null을 반환하며 수락 상태는 변경하지 않는다.
        /// </summary>
        private KeyboardRequestState GetSelectedRequest()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _rows.Count) return null;
            if (_rows[_selectedIndex].Message.IsDeleted) return null;
            KeyboardRequestDefinition definition = _rows[_selectedIndex].Message.Request;
            if (definition == null || string.IsNullOrWhiteSpace(definition.RequestId)) return null;
            return KeyboardRequestRuntime.Requests.TryGetValue(definition.RequestId, out KeyboardRequestState state)
                && state.Definition == definition ? state : null;
        }

        /// <summary>
        /// 런타임의 선택 메일과 단일 진행 의뢰 상태로 정보 및 버튼만 갱신한다.
        /// 목록과 메일 본문 및 선택 ID는 유지하며 완료 버튼에서는 보상을 지급하지 않는다.
        /// </summary>
        private void RefreshRequestUI()
        {
            bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _rows.Count;
            bool deleted = hasSelection && _rows[_selectedIndex].Message.IsDeleted;
            _deleteMail.SetInteractable(hasSelection && !deleted);
            _restoreMail.SetInteractable(hasSelection && deleted);
            Vector2 bodyPosition = _bodyScroll.content.anchoredPosition;
            KeyboardRequestState state = GetSelectedRequest();
            KeyboardRequestState activeRequest = KeyboardRequestRuntime.ActiveRequest;
            _activeRequestText.text = activeRequest == null ? "진행 의뢰: 없음"
                : $"진행 의뢰: {activeRequest.Definition.DisplayName}";
            _requestInfo.gameObject.SetActive(state != null);
            _requestActions.SetActive(state != null);
            RectTransform scrollRect = (RectTransform)_bodyScroll.transform;
            scrollRect.offsetMin = new Vector2(scrollRect.offsetMin.x, state != null ? 106f : 44f);
            if (state == null)
            {
                RestoreScrollPosition(_bodyScroll, bodyPosition);
                return;
            }

            KeyboardRequestDefinition definition = state.Definition;
            string phase = state.Phase switch
            {
                KeyboardRequestPhase.Unaccepted => "미수락",
                KeyboardRequestPhase.InProgress => "진행 중",
                KeyboardRequestPhase.AwaitingConfirmation => "검수 결과 확인 대기",
                _ => "종료"
            };
            string result = state.Result switch
            {
                KeyboardRequestResult.Success => " · 성공",
                KeyboardRequestResult.Failure => " · 실패",
                _ => string.Empty
            };
            string switchName = definition.RequiredSwitchType switch
            {
                SwitchType.Red => "적축",
                SwitchType.Sans => "샌즈 축",
                _ => definition.RequiredSwitchType.ToString()
            };
            string requirements = definition.RequiresAllRequiredSwitchSlots
                ? $"\n요구사항: 모든 필수 스위치 슬롯에 {switchName} 설치" : string.Empty;
            if (definition.RequiresCompleteAssembly)
                requirements += "\n전체 조립: 하단부 · PCB · 보강판 · 상단부 · 필수 키캡";
            if (!definition.RequiresAllRequiredSwitchSlots && !definition.RequiresCompleteAssembly && !definition.RequiresGlassSurfaces)
                requirements = "\n요구사항: 키보드 본체라면 구성과 조립 상태 제한 없음";
            string failure = state.Result == KeyboardRequestResult.Failure
                ? $"\n실패 사유: {state.FailureReason}" : string.Empty;
            _requestInfo.text = $"의뢰: {definition.DisplayName}\n상태: {phase}{result}"
                + (state == activeRequest && state.Phase == KeyboardRequestPhase.InProgress ? " · 발송 대상 의뢰"
                    : state == activeRequest ? " · 진행 의뢰" : activeRequest != null
                        && state.Phase == KeyboardRequestPhase.Unaccepted ? " · 다른 의뢰 진행 중" : string.Empty)
                + requirements + (definition.RequiresGlassSurfaces
                    ? "\n재질: 하단부 · 상단부 · 모든 필수 키캡은 유리 (PCB·보강판 제한 없음)"
                    : "\n재질과 키캡 디자인 제한 없음")
                + $"\n성공 보상: {definition.RewardGold}골드" + failure
                + (string.IsNullOrEmpty(_requestNotice) ? string.Empty : $"\n{_requestNotice}");
            _acceptRequest.SetInteractable(state.Phase == KeyboardRequestPhase.Unaccepted && activeRequest == null);
            _closeRequest.SetInteractable(state.Phase == KeyboardRequestPhase.AwaitingConfirmation);
            RestoreScrollPosition(_bodyScroll, bodyPosition);
        }

        /// <summary>
        /// scroll의 레이아웃을 갱신한 뒤 position의 세로 스크롤 위치를 유효 범위 안에서 복원한다.
        /// 내용 높이가 줄어들면 끝 위치로 제한하며 수신 기록과 현재 메일 선택은 변경하지 않는다.
        /// </summary>
        private void RestoreScrollPosition(ScrollRect scroll, Vector2 position)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            float maximum = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
            scroll.StopMovement();
            scroll.content.anchoredPosition = new Vector2(position.x, Mathf.Clamp(position.y, 0f, maximum));
        }

        /// <summary>
        /// button 클릭으로 현재 메일의 의뢰 수락을 런타임에 요청한다.
        /// 런타임이 미수락 여부와 다른 진행 의뢰를 재검사하고 상태를 갱신한다.
        /// </summary>
        private void HandleAcceptRequest(MonitorOSButton button)
        {
            KeyboardRequestState state = GetSelectedRequest();
            if (state != null) KeyboardRequestRuntime.TryAccept(state.Definition.RequestId);
        }

        /// <summary>
        /// button 클릭으로 현재 의뢰의 결과 확인과 종료만 런타임에 요청한다.
        /// 런타임이 종료 조건을 다시 검사하며 거부하면 안내를 표시하고 골드는 지급하지 않는다.
        /// </summary>
        private void HandleCloseRequest(MonitorOSButton button)
        {
            KeyboardRequestState state = GetSelectedRequest();
            if (state == null) return;
            _requestNotice = null;
            if (!KeyboardRequestRuntime.TryClose(state.Definition.RequestId))
            {
                _requestNotice = "의뢰 완료 처리가 아직 준비되지 않았습니다.";
                RefreshRequestUI();
            }
        }

        /// <summary>
        /// key가 위아래 방향키이면 받은편지함의 이전 또는 다음 메일을 연다.
        /// 목록 범위 안에서 선택만 변경하며 빈 목록의 입력은 무시한다.
        /// </summary>
        public override void HandleKeyPressed(Key key)
        {
            if (_rows.Count == 0) return;
            if (key == Key.UpArrow) SelectMessage(Mathf.Max(0, _selectedIndex - 1), true);
            if (key == Key.DownArrow) SelectMessage(Mathf.Min(_rows.Count - 1, _selectedIndex + 1), true);
        }
    }
}
