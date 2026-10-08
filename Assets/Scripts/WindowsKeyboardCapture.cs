#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KeyboardModeling
{
    internal static class WindowsKeyboardCapture
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int EXTENDED_KEY = 0x01;
        private const int VK_ESCAPE = 0x1B;
        private const uint WM_QUIT = 0x0012;

        private static readonly HashSet<KeyboardInputController> _owners = new HashSet<KeyboardInputController>();
        private static readonly bool[] _pressedKeys = new bool[256];
        private static readonly int[] _pressEdges = new int[256];
        private static readonly ConcurrentQueue<long> _debugEvents = new ConcurrentQueue<long>();
        private static readonly bool[] _suppressedScanCodes = new bool[512];
        private static readonly Key[] _scanKeys = BuildScanMap();
        private static readonly KeyboardHookCallback _callback = HandleKeyboard;
        private static IntPtr _hook;
        private static uint _processId;
        private static uint _hookThreadId;
        private static Thread _hookThread;
        private static volatile bool _captureRequested;
        private static volatile bool _hookInstalled;
        private static bool _callbacksRegistered;
        private static volatile bool _debugEnabled;
        private static int _eventCount;

        internal static bool IsCapturing => _hookInstalled;
        internal static int EventCount => Volatile.Read(ref _eventCount);

        /// <summary>
        /// 훅 입력의 진단 이벤트 수집 여부를 설정한다.
        /// debugEnabled를 사용하여 Unity 메인 스레드에서 출력할 이벤트 수집 상태를 변경한다.
        /// </summary>
        internal static void SetDebugEnabled(bool debugEnabled)
        {
            _debugEnabled = debugEnabled;
        }

        /// <summary>
        /// 훅이 수집한 물리 키 이벤트를 메인 스레드에서 출력한다.
        /// 진단 큐와 활성 설정을 사용하여 눌림, 해제, 가상 키 및 스캔 코드를 Console에 기록한다.
        /// </summary>
        internal static void LogCapturedEvents()
        {
            while (_debugEvents.TryDequeue(out long data))
            {
                if (!_debugEnabled)
                    continue;
                int virtualKey = (int)(data & 0xFFFF);
                int scanIndex = (int)((data >> 16) & 0xFFFF);
                bool isDown = ((data >> 32) & 1) != 0;
                Key key = (Key)(data >> 40);
                Debug.Log("[Keyboard][Hook] " + (isDown ? "DOWN " : "UP ") + key + " VK=0x" + virtualKey.ToString("X2") + " Scan=0x" + scanIndex.ToString("X3"));
            }
        }

        /// <summary>
        /// 입력 제어기의 단축키 차단 참여 상태를 변경한다.
        /// owner와 captureEnabled를 사용하여 첫 참여 시 Windows 훅을 설치하고 마지막 해제 시 제거한다.
        /// </summary>
        internal static void SetCaptureEnabled(KeyboardInputController owner, bool captureEnabled)
        {
            if (!captureEnabled)
            {
                _owners.Remove(owner);
                if (_owners.Count == 0)
                    ReleaseAll();
                return;
            }
            if (!_owners.Add(owner) || _captureRequested)
                return;
            RegisterCleanup();
            _processId = GetCurrentProcessId();
            _captureRequested = true;
            _hookThread = new Thread(PumpKeyboardEvents)
            {
                IsBackground = true,
                Name = "Keyboard Shortcut Capture"
            };
            _hookThread.Start();
        }

        /// <summary>
        /// Windows 메시지 루프가 있는 전용 스레드에서 키보드 훅을 처리한다.
        /// 차단 요청 상태를 사용하여 훅을 설치하고 메시지를 처리한 뒤 훅과 네이티브 메모리를 해제한다.
        /// </summary>
        private static void PumpKeyboardEvents()
        {
            IntPtr buffer = Marshal.AllocHGlobal(IntPtr.Size == 8 ? 48 : 32);
            try
            {
                PeekMessage(buffer, IntPtr.Zero, 0, 0, 0);
                Volatile.Write(ref _hookThreadId, GetCurrentThreadId());
                if (!_captureRequested)
                    return;
                IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                {
                    Debug.LogWarning("Windows 키보드 단축키 차단을 시작하지 못했습니다. 오류: " + Marshal.GetLastWin32Error());
                    return;
                }
                Interlocked.Exchange(ref _hook, hook);
                _hookInstalled = true;
                while (_captureRequested && GetMessage(buffer, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(buffer);
                    DispatchMessage(buffer);
                }
            }
            finally
            {
                _hookInstalled = false;
                IntPtr hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
                if (hook != IntPtr.Zero)
                    UnhookWindowsHookEx(hook);
                Volatile.Write(ref _hookThreadId, 0);
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Unity 프로세스가 현재 전경 앱인지 확인한다.
        /// 전경 창의 프로세스와 현재 프로세스 식별자를 사용하여 실제 앱 포커스 여부를 반환한다.
        /// </summary>
        internal static bool HasApplicationFocus()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
            return foregroundProcess == GetCurrentProcessId();
        }

        /// <summary>
        /// 차단 중 직접 수집한 물리 키의 눌림 상태를 반환한다.
        /// key를 상태 배열의 인덱스로 사용하여 현재 눌림 여부를 반환한다.
        /// </summary>
        internal static bool IsPressed(Key key)
        {
            bool pressed = Volatile.Read(ref _pressedKeys[(int)key]);
            bool pressedThisFrame = Interlocked.Exchange(ref _pressEdges[(int)key], 0) != 0;
            if (key == Key.Pause)
                Volatile.Write(ref _pressedKeys[(int)key], false);
            return pressed || pressedThisFrame;
        }

        /// <summary>
        /// 실행 종료와 에디터 상태 전환에 맞춘 정리 콜백을 등록한다.
        /// 등록 상태를 사용하여 종료, 일시정지, 어셈블리 재로드 시 훅이 제거되도록 설정한다.
        /// </summary>
        private static void RegisterCleanup()
        {
            if (_callbacksRegistered)
                return;
            Application.quitting += ReleaseAll;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.pauseStateChanged += OnPauseChanged;
            EditorApplication.update += ReleaseWhenGameUnfocused;
#endif
            _callbacksRegistered = true;
        }

        /// <summary>
        /// 단축키 차단과 저장된 키 상태를 모두 해제한다.
        /// 설치된 훅 핸들과 참여 목록을 사용하여 훅을 제거하고 눌림 상태를 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ReleaseAll()
        {
            _captureRequested = false;
            _hookInstalled = false;
            IntPtr hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
            if (hook != IntPtr.Zero)
                UnhookWindowsHookEx(hook);
            Thread thread = _hookThread;
            if (thread != null && thread.IsAlive)
            {
                uint threadId = Volatile.Read(ref _hookThreadId);
                if (threadId != 0)
                    PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                thread.Join();
            }
            _hookThread = null;
            _owners.Clear();
            Array.Clear(_pressedKeys, 0, _pressedKeys.Length);
            Array.Clear(_pressEdges, 0, _pressEdges.Length);
            _debugEvents.Clear();
            Array.Clear(_suppressedScanCodes, 0, _suppressedScanCodes.Length);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Game 창 외의 에디터 작업으로 이동하면 단축키 차단을 해제한다.
        /// 에디터의 포커스와 실행 상태를 사용하여 일시정지와 다른 창에서 훅을 제거한다.
        /// </summary>
        private static void ReleaseWhenGameUnfocused()
        {
            if (_owners.Count == 0)
                return;
            EditorWindow window = EditorWindow.focusedWindow;
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || window == null || window.GetType().Name != "GameView")
                ReleaseAll();
        }

        /// <summary>
        /// 에디터가 Play 모드를 끝내면 단축키 차단을 정리한다.
        /// state를 사용하여 실행 종료 시 훅과 저장된 키 상태를 해제한다.
        /// </summary>
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                ReleaseAll();
        }

        /// <summary>
        /// 에디터 실행 일시정지 시 단축키 차단을 정리한다.
        /// state가 Paused이면 훅과 저장된 키 상태를 해제한다.
        /// </summary>
        private static void OnPauseChanged(PauseState state)
        {
            if (state == PauseState.Paused)
                ReleaseAll();
        }
#endif

        /// <summary>
        /// Windows 키 이벤트를 받아 모델용 상태를 저장하고 Esc 외 입력 전달을 차단한다.
        /// code, message, data와 전경 프로세스를 사용하여 훅 처리 결과 또는 다음 훅의 반환값을 반환한다.
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(KeyboardHookCallback))]
        private static IntPtr HandleKeyboard(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || !_captureRequested)
                return CallNextHookEx(_hook, code, message, data);
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcess);
            if (foregroundProcess != _processId)
                return CallNextHookEx(_hook, code, message, data);
            int eventType = message.ToInt32();
            bool isDown = eventType == WM_KEYDOWN || eventType == WM_SYSKEYDOWN;
            bool isUp = eventType == WM_KEYUP || eventType == WM_SYSKEYUP;
            if (!isDown && !isUp)
                return CallNextHookEx(_hook, code, message, data);
            int virtualKey = Marshal.ReadInt32(data, 0);
            int scanCode = Marshal.ReadInt32(data, 4);
            int flags = Marshal.ReadInt32(data, 8);
            int scanIndex = (scanCode & 0xFF) | ((flags & EXTENDED_KEY) != 0 ? 0x100 : 0);
            Key key = ResolveKey(virtualKey, scanIndex);
            Interlocked.Increment(ref _eventCount);
            if (_debugEnabled && _debugEvents.Count < 512)
                _debugEvents.Enqueue((long)virtualKey | ((long)scanIndex << 16) | ((isDown ? 1L : 0L) << 32) | ((long)key << 40));
            if (key != Key.None)
            {
                Volatile.Write(ref _pressedKeys[(int)key], isDown);
                if (isDown)
                    Interlocked.Exchange(ref _pressEdges[(int)key], 1);
            }
            if (virtualKey == VK_ESCAPE)
                return CallNextHookEx(_hook, code, message, data);
            bool suppress = isDown || _suppressedScanCodes[scanIndex];
            _suppressedScanCodes[scanIndex] = isDown;
            return suppress ? new IntPtr(1) : CallNextHookEx(_hook, code, message, data);
        }

        /// <summary>
        /// 스캔 코드와 특수 가상 키 값을 ANSI 물리 키로 해석한다.
        /// virtualKey와 scanIndex를 사용하여 숫자패드 및 특수 키를 구분한 Key 값을 반환한다.
        /// </summary>
        private static Key ResolveKey(int virtualKey, int scanIndex)
        {
            switch (virtualKey)
            {
                case 0x13: return Key.Pause;
                case 0x2C: return Key.PrintScreen;
                case 0x90: return Key.NumLock;
                case 0x91: return Key.ScrollLock;
                default: return _scanKeys[scanIndex];
            }
        }

        /// <summary>
        /// ANSI 키 위치와 확장 스캔 코드의 대응표를 만든다.
        /// 고정 104키 배열을 사용하여 좌우 보조 키와 숫자패드를 구분하는 조회 배열을 반환한다.
        /// </summary>
        private static Key[] BuildScanMap()
        {
            var keys = new Key[512];
            keys[0x01] = Key.Escape;
            Key[] numberRow = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus, Key.Equals, Key.Backspace };
            Key[] topRow = { Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P, Key.LeftBracket, Key.RightBracket, Key.Enter, Key.LeftCtrl };
            Key[] homeRow = { Key.A, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L, Key.Semicolon, Key.Quote, Key.Backquote, Key.LeftShift, Key.Backslash };
            Key[] bottomRow = { Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M, Key.Comma, Key.Period, Key.Slash, Key.RightShift, Key.NumpadMultiply, Key.LeftAlt, Key.Space, Key.CapsLock };
            Key[] numpad = { Key.Numpad7, Key.Numpad8, Key.Numpad9, Key.NumpadMinus, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.NumpadPlus, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad0, Key.NumpadPeriod };
            CopyScanRow(keys, 0x02, numberRow);
            CopyScanRow(keys, 0x10, topRow);
            CopyScanRow(keys, 0x1E, homeRow);
            CopyScanRow(keys, 0x2C, bottomRow);
            CopyScanRow(keys, 0x47, numpad);
            keys[0x0F] = Key.Tab;
            for (int index = 0; index < 10; index++)
                keys[0x3B + index] = (Key)((int)Key.F1 + index);
            keys[0x45] = Key.NumLock;
            keys[0x46] = Key.ScrollLock;
            keys[0x57] = Key.F11;
            keys[0x58] = Key.F12;
            keys[0x11C] = Key.NumpadEnter;
            keys[0x11D] = Key.RightCtrl;
            keys[0x135] = Key.NumpadDivide;
            keys[0x137] = Key.PrintScreen;
            keys[0x138] = Key.RightAlt;
            keys[0x147] = Key.Home;
            keys[0x148] = Key.UpArrow;
            keys[0x149] = Key.PageUp;
            keys[0x14B] = Key.LeftArrow;
            keys[0x14D] = Key.RightArrow;
            keys[0x14F] = Key.End;
            keys[0x150] = Key.DownArrow;
            keys[0x151] = Key.PageDown;
            keys[0x152] = Key.Insert;
            keys[0x153] = Key.Delete;
            keys[0x15B] = Key.LeftMeta;
            keys[0x15C] = Key.RightMeta;
            keys[0x15D] = Key.ContextMenu;
            return keys;
        }

        /// <summary>
        /// 연속된 키 행을 스캔 코드 조회표에 복사한다.
        /// destination, start, row를 사용하여 지정 범위의 Key 값을 변경한다.
        /// </summary>
        private static void CopyScanRow(Key[] destination, int start, Key[] row)
        {
            Array.Copy(row, 0, destination, start, row.Length);
        }

        /// <summary>
        /// Windows 키보드 훅 콜백의 호출 형식을 정의한다.
        /// code, message, data를 받아 차단 여부 또는 다음 훅의 처리 결과를 반환한다.
        /// </summary>
        private delegate IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data);

        /// <summary>
        /// Windows 키보드 훅을 설치한다.
        /// hookType, callback, module, threadId를 사용하여 설치된 훅 핸들을 반환한다.
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookType, KeyboardHookCallback callback, IntPtr module, uint threadId);

        /// <summary>
        /// 설치된 Windows 훅을 제거한다.
        /// hook 핸들을 사용하여 제거 성공 여부를 반환한다.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        /// <summary>
        /// 처리하지 않은 키 이벤트를 다음 훅으로 전달한다.
        /// hook, code, message, data를 사용하여 다음 훅의 처리 결과를 반환한다.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        /// <summary>
        /// 현재 전경 창의 핸들을 조회한다.
        /// 별도 입력 없이 사용자가 조작 중인 창의 핸들을 반환한다.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// 창을 소유하는 프로세스와 스레드를 조회한다.
        /// window를 사용하여 processId를 기록하고 소유 스레드 식별자를 반환한다.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        /// <summary>
        /// 현재 Unity 프로세스 식별자를 조회한다.
        /// 별도 입력 없이 실행 중인 프로세스의 식별자를 반환한다.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        /// <summary>
        /// 훅 스레드의 네이티브 식별자를 조회한다.
        /// 별도 입력 없이 현재 호출 스레드의 Windows 식별자를 반환한다.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        /// 스레드 메시지 큐를 준비하고 대기 중인 메시지를 조회한다.
        /// buffer, window, minimum, maximum, remove를 사용하여 메시지 존재 여부를 반환한다.
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(IntPtr buffer, IntPtr window, uint minimum, uint maximum, uint remove);

        /// <summary>
        /// 훅 스레드에 전달되는 Windows 메시지를 기다린다.
        /// buffer, window, minimum, maximum을 사용하여 메시지를 기록하고 수신 상태를 반환한다.
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "GetMessageW")]
        private static extern int GetMessage(IntPtr buffer, IntPtr window, uint minimum, uint maximum);

        /// <summary>
        /// 수신 메시지를 Windows 문자 메시지로 변환한다.
        /// buffer의 메시지를 사용하여 변환 여부를 반환한다.
        /// </summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(IntPtr buffer);

        /// <summary>
        /// 수신 메시지를 Windows 창 프로시저로 전달한다.
        /// buffer의 메시지를 사용하여 처리 결과를 반환한다.
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        private static extern IntPtr DispatchMessage(IntPtr buffer);

        /// <summary>
        /// 훅 스레드에 종료 메시지를 보내 대기를 해제한다.
        /// threadId, message, wParam, lParam을 사용하여 전달 성공 여부를 반환한다.
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 훅 설치에 사용할 모듈 핸들을 조회한다.
        /// name으로 지정한 모듈의 핸들을 반환하며 null이면 실행 파일의 핸들을 반환한다.
        /// </summary>
        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
    }
}
#endif
