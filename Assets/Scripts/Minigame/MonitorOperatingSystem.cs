using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class MonitorOperatingSystem : MonoBehaviour
    {
        [Header("Connections")]
        [SerializeField] private MonitorMiniGameManager _manager;
        [SerializeField] private MonitorMiniGameInput _input;
        [SerializeField] private MonitorKeyboardHolder _holder;

        [Header("Desktop")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _interactionGroup;
        [SerializeField] private GameObject _desktop;
        [SerializeField] private Transform _appRoot;
        [SerializeField] private MonitorOSButton _appTemplate;

        [Header("App Layout")]
        [SerializeField, Min(1)] private int _appColumns = 6;
        [SerializeField] private Vector2 _appSpacing = new Vector2(190f, 180f);

        [Header("App Window")]
        [SerializeField] private GameObject _window;
        [SerializeField] private Image _windowIcon;
        [SerializeField] private Text _windowTitle;
        [SerializeField] private Text _taskbarTitle;
        [SerializeField] private MonitorOSButton _closeButton;

        void Awake()
        {
            _appTemplate.gameObject.SetActive(false);
            int appIndex = 0;
            foreach (MonitorMiniGameDefinition definition in _manager.Games)
            {
                if (definition == null) continue;
                MonitorOSButton icon = Instantiate(_appTemplate, _appRoot, false);
                RectTransform iconRect = (RectTransform)icon.transform;
                int columns = Mathf.Max(1, _appColumns);
                iconRect.anchoredPosition += new Vector2(
                    appIndex % columns * _appSpacing.x, -(appIndex / columns) * _appSpacing.y);
                appIndex++;
                icon.Configure(definition);
                icon.Clicked += HandleAppClicked;
                icon.gameObject.SetActive(true);
            }
        }

        void OnEnable()
        {
            _manager.GameChanged += HandleGameChanged;
            _input.FocusChanged += HandleFocusChanged;
            _closeButton.Clicked += HandleCloseClicked;
            _holder.KeyboardChanged += HandleKeyboardChanged;
            HandleKeyboardChanged(_holder.KeyboardBody != null);
            HandleFocusChanged(_input.HasFocus);
            HandleGameChanged(_manager.CurrentGame);
        }

        void OnDisable()
        {
            _manager.GameChanged -= HandleGameChanged;
            _input.FocusChanged -= HandleFocusChanged;
            _closeButton.Clicked -= HandleCloseClicked;
            _holder.KeyboardChanged -= HandleKeyboardChanged;
            _canvas.enabled = false;
            HandleFocusChanged(false);
        }

        /// <summary>
        /// installed에 따라 모니터 화면의 전원을 변경한다.
        /// 키보드 거치 상태를 사용하며 제거 시 현재 앱을 종료하고 Canvas 표시를 끈다.
        /// </summary>
        private void HandleKeyboardChanged(bool installed)
        {
            if (!installed) _manager.StopGame();
            _canvas.enabled = installed;
            HandleFocusChanged(installed && _input.HasFocus);
        }

        /// <summary>button에 할당된 SO를 매니저로 실행한다. 모니터 포커스가 없으면 무시하며 화면 생성과 종료는 매니저에 맡긴다.</summary>
        private void HandleAppClicked(MonitorOSButton button)
        {
            if (_input.HasFocus) _manager.StartGame(button.Definition);
        }

        /// <summary>닫기 button 요청으로 현재 앱을 종료한다. 포커스 중일 때만 처리하며 GameChanged를 통해 바탕화면을 복원한다.</summary>
        private void HandleCloseClicked(MonitorOSButton button)
        {
            if (_input.HasFocus) _manager.StopGame();
        }

        /// <summary>game 유무에 따라 바탕화면과 앱 창을 전환한다. 실행 중인 SO 이름을 창 제목과 작업표시줄에 표시한다.</summary>
        private void HandleGameChanged(MonitorMiniGame game)
        {
            bool running = game != null;
            _desktop.SetActive(!running);
            _window.SetActive(running);
            _windowTitle.text = running ? game.DisplayName : "";
            _taskbarTitle.text = running ? game.DisplayName : "아이콘을 더블클릭하여 실행하세요";
            if (_windowIcon != null)
            {
                _windowIcon.sprite = running && game.Definition != null ? game.Definition.Icon : null;
                _windowIcon.enabled = running && _windowIcon.sprite != null;
            }
        }

        /// <summary>focused로 모니터 UI의 클릭 허용 여부를 변경한다. 바탕화면과 연결된 씬 카메라는 유지한다.</summary>
        private void HandleFocusChanged(bool focused)
        {
            _interactionGroup.interactable = focused;
            _interactionGroup.blocksRaycasts = focused;
        }
    }
}
