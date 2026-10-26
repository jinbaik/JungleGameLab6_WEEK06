using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Input;
using VoltstroStudios.UnityWebBrowser.Shared;
using VoltstroStudios.UnityWebBrowser.Shared.Events;
using VoltstroStudios.UnityWebBrowser.Shared.Popups;

using BrowserResolution = VoltstroStudios.UnityWebBrowser.Shared.Resolution;

namespace Game.Player
{
    public sealed class MonitorWebGame : MonitorMiniGame, IPointerDownHandler, IPointerUpHandler,
        IPointerMoveHandler, IPointerExitHandler, IDragHandler, IScrollHandler
    {
        [Header("Screen UI")]
        [SerializeField] private RawImage _view;
        [SerializeField] private Text _status;

        [Header("Browser Runtime")]
        private readonly List<WindowsKey> _keysDown = new List<WindowsKey>();
        private readonly List<WindowsKey> _keysUp = new List<WindowsKey>();
        private readonly HashSet<WindowsKey> _heldKeys = new HashSet<WindowsKey>();
        private readonly StringBuilder _textInput = new StringBuilder();
        private WebBrowserClient _browser;
        private TCPCommunicationLayer _communication;
        private MonitorMiniGameInput _input;
        private int _pageLoaded;
        private bool _connected;
        private bool _pointerHeld;
        private Vector2 _pointerPosition;
        private float _loadingTime;

        /// <summary>
        /// definition이 유효한 웹 앱 정의인지 검사해 실행 가능 여부를 반환한다.
        /// URL과 해상도를 사용하며 브라우저 생성은 실행 시작 시 처리한다.
        /// </summary>
        public override bool CanRun(MonitorMiniGameDefinition definition)
        {
            return definition is MonitorWebGameDefinition web && web.IsValid();
        }

        /// <summary>
        /// 정의의 URL을 창 없는 CEF 브라우저에서 열고 화면 텍스처를 연결한다.
        /// URL과 해상도 및 엔진 설정을 사용하며 앱의 통신과 입력 및 로딩 상태를 초기화한다.
        /// </summary>
        protected override void OnStarted()
        {
            _input = GetComponentInParent<MonitorMiniGameManager>().GetComponentInParent<MonitorMiniGameInput>();
            _connected = false;
            _pageLoaded = 0;
            _loadingTime = 0f;
            _view.color = Color.white;
            _view.texture = Texture2D.blackTexture;
            _view.uvRect = new Rect(0f, 0f, 1f, -1f);
            _status.gameObject.SetActive(true);
            _status.text = "JazzKeys 불러오는 중...";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                MonitorWebGameDefinition definition = (MonitorWebGameDefinition)Definition;
                _communication = ScriptableObject.CreateInstance<TCPCommunicationLayer>();
                _communication.inPort = GetAvailablePort(0);
                _communication.outPort = GetAvailablePort(_communication.inPort);
                _browser = new WebBrowserClient
                {
                    engine = Resources.Load<Engine>("Cef Engine Configuration"),
                    communicationLayer = _communication,
                    initialUrl = definition.Url,
                    Resolution = new BrowserResolution((uint)definition.Resolution.x, (uint)definition.Resolution.y),
                    backgroundColor = new Color32(0, 0, 0, 255),
                    windowlessFrameRate = 30,
                    engineStartupTimeout = 15000,
                    popupAction = PopupAction.Ignore
                };
                _browser.OnLoadFinish += HandlePageLoaded;
                _browser.Init();
                _view.texture = _browser.BrowserTexture;
            }
            catch (Exception exception)
            {
                _status.text = "웹 화면을 시작하지 못했습니다. Console을 확인하세요.";
                Debug.LogError($"[MonitorWebGame] {exception}", this);
                DisposeBrowser();
            }
#else
            _status.text = "이 웹 앱은 Windows에서 사용할 수 있습니다.";
#endif
        }

        /// <summary>
        /// deltaTime으로 로딩 안내와 브라우저 텍스처 및 입력을 갱신한다.
        /// 브라우저 연결 상태와 모니터 포커스를 사용하며 페이지 완료 후 웹 화면을 표시한다.
        /// </summary>
        public override void Tick(float deltaTime)
        {
            if (_browser == null) return;
            if (_browser.HasDisposed)
            {
                _status.gameObject.SetActive(true);
                _status.text = "웹 엔진이 종료되었습니다. 앱을 닫고 다시 실행하세요.";
                return;
            }
            _loadingTime += deltaTime;
            if (!_browser.IsConnected || !_browser.ReadySignalReceived)
            {
                if (_loadingTime >= 15f) _status.text = "웹 엔진 연결을 기다리고 있습니다...";
                return;
            }
            if (!_connected)
            {
                _browser.AudioMute(false);
                _connected = true;
            }
            _browser.LoadTextureData();
            _browser.UpdateFps();
            if (Interlocked.Exchange(ref _pageLoaded, 0) != 0)
            {
                _status.gameObject.SetActive(false);
                _browser.ExecuteJs("document.querySelector('textarea')?.focus({preventScroll:true});");
            }
            if (!CanReceiveInput() || Keyboard.current == null)
            {
                ReleaseInput();
                return;
            }
            _keysDown.Clear();
            _keysUp.Clear();
            foreach (var key in Keyboard.current.allKeys)
            {
                WindowsKey browserKey;
                try { browserKey = key.keyCode.UnityKeyToWindowKey(); }
                catch (ArgumentOutOfRangeException) { continue; }
                bool allowed = _input.CanReceiveKey(key.keyCode);
                if (key.wasPressedThisFrame && allowed && _heldKeys.Add(browserKey)) _keysDown.Add(browserKey);
                if ((!allowed || !key.isPressed) && _heldKeys.Remove(browserKey)) _keysUp.Add(browserKey);
            }
            if (_keysDown.Count > 0 || _keysUp.Count > 0 || _textInput.Length > 0)
            {
                _browser.SendKeyboardControls(_keysDown.ToArray(), _keysUp.ToArray(), _textInput.ToString().ToCharArray());
                _textInput.Clear();
            }
        }

        /// <summary>
        /// character를 다음 프레임에 브라우저로 보낼 문자 버퍼에 추가한다.
        /// 모니터 포커스와 연결 상태를 사용하며 웹 엔진이 실제 문자 입력을 처리하게 한다.
        /// </summary>
        public override void HandleTextInput(char character)
        {
            if (CanReceiveInput()) _textInput.Append(character);
        }

        /// <summary>
        /// url의 페이지 로드 완료를 저장한다.
        /// 완료 이벤트를 사용하며 UI 갱신과 텍스트 영역 포커스는 다음 Tick에서 처리한다.
        /// </summary>
        private void HandlePageLoaded(string url)
        {
            Interlocked.Exchange(ref _pageLoaded, 1);
        }

        /// <summary>
        /// 실행 중인 앱과 브라우저 연결 및 모니터 포커스로 입력 허용 여부를 반환한다.
        /// 현재 상태를 조회하며 일시정지와 튜토리얼 및 앱 포커스 상실 중에는 입력을 막는다.
        /// </summary>
        private bool CanReceiveInput()
        {
            return IsRunning && _browser != null && !_browser.HasDisposed && _connected
                && _browser.IsConnected && _browser.ReadySignalReceived && _input.HasFocus
                && Application.isFocused && Time.timeScale > 0f && !Game.Tutorials.FirstEncounterTutorial.IsBlocking;
        }

        /// <summary>
        /// excluded와 다른 사용 가능한 로컬 TCP 포트를 찾아 반환한다.
        /// 운영체제가 할당한 포트를 사용하며 포트 확인에 사용한 리스너는 즉시 닫는다.
        /// </summary>
        private static int GetAvailablePort(int excluded)
        {
            int port;
            do
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try { port = ((IPEndPoint)listener.LocalEndpoint).Port; }
                finally { listener.Stop(); }
            } while (port == excluded);
            return port;
        }

        /// <summary>
        /// 눌린 웹 키와 왼쪽 마우스 버튼을 해제하고 문자 버퍼를 비운다.
        /// 추적한 키와 마지막 포인터 위치를 사용하며 포커스가 사라진 뒤 입력이 남지 않게 한다.
        /// </summary>
        private void ReleaseInput()
        {
            if (_browser != null && !_browser.HasDisposed && _browser.IsConnected && _browser.ReadySignalReceived)
            {
                if (_heldKeys.Count > 0)
                {
                    WindowsKey[] released = new WindowsKey[_heldKeys.Count];
                    _heldKeys.CopyTo(released);
                    _browser.SendKeyboardControls(Array.Empty<WindowsKey>(), released, Array.Empty<char>());
                }
                if (_pointerHeld) _browser.SendMouseClick(_pointerPosition, 1, MouseClickType.Left, MouseEventType.Up);
            }
            _heldKeys.Clear();
            _textInput.Clear();
            _pointerHeld = false;
        }

        /// <summary>
        /// 앱 종료 시 입력을 해제하고 웹 엔진을 정리한다.
        /// 입력값 없이 텍스처와 소리 재생 및 로컬 통신을 함께 종료한다.
        /// </summary>
        protected override void OnStopped()
        {
            ReleaseInput();
            DisposeBrowser();
        }

        /// <summary>
        /// 현재 앱의 브라우저와 통신 설정을 해제한다.
        /// 보관한 참조를 사용하며 프로세스와 텍스처를 정리하고 화면 참조를 비운다.
        /// </summary>
        private void DisposeBrowser()
        {
            if (_browser != null)
            {
                _browser.OnLoadFinish -= HandlePageLoaded;
                _browser.Dispose();
                _browser = null;
            }
            _view.texture = Texture2D.blackTexture;
            if (_communication != null) Destroy(_communication);
            _communication = null;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !CanReceiveInput()) return;
            if (!UpdatePointer(eventData)) return;
            _pointerHeld = true;
            _browser.SendMouseClick(_pointerPosition, Mathf.Max(1, eventData.clickCount), MouseClickType.Left, MouseEventType.Down);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pointerHeld || eventData.button != PointerEventData.InputButton.Left) return;
            if (CanReceiveInput())
            {
                UpdatePointer(eventData);
                _browser.SendMouseClick(_pointerPosition, 1, MouseClickType.Left, MouseEventType.Up);
            }
            _pointerHeld = false;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (CanReceiveInput() && UpdatePointer(eventData)) _browser.SendMouseMove(_pointerPosition);
        }

        public void OnDrag(PointerEventData eventData) { OnPointerMove(eventData); }
        public void OnPointerExit(PointerEventData eventData) { OnPointerMove(eventData); }

        public void OnScroll(PointerEventData eventData)
        {
            if (CanReceiveInput() && UpdatePointer(eventData))
                _browser.SendMouseScroll(_pointerPosition, Mathf.RoundToInt(eventData.scrollDelta.y * 120f));
        }

        /// <summary>
        /// eventData의 화면 좌표를 브라우저의 픽셀 좌표로 변환해 성공 여부를 반환한다.
        /// RawImage 영역과 이벤트 카메라를 사용하며 마지막 웹 포인터 위치를 저장한다.
        /// </summary>
        private bool UpdatePointer(PointerEventData eventData)
        {
            RectTransform rect = _view.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position,
                eventData.pressEventCamera ?? eventData.enterEventCamera, out Vector2 local)) return false;
            BrowserResolution resolution = _browser.Resolution;
            _pointerPosition = new Vector2(
                Mathf.Clamp01((local.x - rect.rect.xMin) / rect.rect.width) * (resolution.Width - 1),
                (1f - Mathf.Clamp01((local.y - rect.rect.yMin) / rect.rect.height)) * (resolution.Height - 1));
            return true;
        }
    }
}
