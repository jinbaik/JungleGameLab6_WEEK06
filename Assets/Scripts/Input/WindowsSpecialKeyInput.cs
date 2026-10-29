using System;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
#endif

using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-31000)]
public sealed class WindowsSpecialKeyInput : MonoBehaviour
{
    /// <summary>keyboard와 key의 눌림 또는 현재 프레임 누름을 반환한다. 차단 중인 특수키는 단일 훅 상태를 사용해 중복 입력을 만들지 않는다.</summary>
    public static bool IsPressed(Keyboard keyboard, Key key)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        int index = GetSpecialIndex(key);
        if (index >= 0 && OwnsInput()) return _frameHeld[index] || _framePressed[index];
        if (index >= 0 && _instance != null && (Volatile.Read(ref _installed) != 0 || _dialogDepth > 0 || !IsGameForeground())) return false;
#endif
        return keyboard != null && (keyboard[key].isPressed || keyboard[key].wasPressedThisFrame);
    }

    /// <summary>keyboard와 key의 이번 프레임 누름 여부를 반환한다. 훅 상태와 Unity 입력 이벤트를 별도로 발행하지 않고 한 경로만 선택한다.</summary>
    public static bool WasPressedThisFrame(Keyboard keyboard, Key key)
    {
        if (Game.Tutorials.FirstEncounterTutorial.IsBlocking || Game.Tournament.TournamentController.IsReturnPaused) return false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        int index = GetSpecialIndex(key);
        if (index >= 0 && OwnsInput()) return _framePressed[index];
        if (index >= 0 && _instance != null && (Volatile.Read(ref _installed) != 0 || _dialogDepth > 0 || !IsGameForeground())) return false;
#endif
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    /// <summary>파일 대화상자 진입을 기록하고 훅 스레드를 종료한다. 다른 플랫폼과 에디터에서는 아무 상태도 변경하지 않는다.</summary>
    public static void SuspendForFileDialog()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        _dialogDepth++;
        if (_instance != null) _instance.StopHook();
#endif
    }

    /// <summary>파일 대화상자 종료를 기록한다. 다음 메인 프레임에서 실제 게임 창 포커스를 확인한 후에만 훅을 재설치한다.</summary>
    public static void ResumeAfterFileDialog()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        _dialogDepth = Math.Max(0, _dialogDepth - 1);
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_SNAPSHOT = 0x2C;

    private static readonly HookProcedure _callback = HandleKeyboard;
    private static readonly int[] _physicalHeld = new int[3];
    private static readonly int[] _gameHeld = new int[3];
    private static readonly int[] _pressed = new int[3];
    private static readonly int[] _blockedDown = new int[3];
    private static readonly int[] _receivedEvents = new int[3];
    private static readonly int[] _blockedEvents = new int[3];
    private static readonly long[] _eventWindows = new long[3];
    private static readonly int[] _eventEnabled = new int[3];
    private static readonly bool[] _frameHeld = new bool[3];
    private static readonly bool[] _framePressed = new bool[3];
    private static WindowsSpecialKeyInput _instance;
    private static IntPtr _gameWindow;
    private static int _allowBlocking;
    private static int _installed;
    private static int _stopRequested;
    private static int _nativeError;
    private static int _unhookError;
    private static int _dialogDepth;
    private static int _ignorePrintRelease;
    private static uint _threadId;

    [Header("Runtime")]
    private Thread _hookThread;
    private bool _attempted;
    private bool _wasActive;
    private bool _quitting;
    private bool _windowWarningIssued;
    private bool _reportedInstalled;
    private float _windowSearchStarted;
    private readonly int[] _reportedEvents = new int[3];

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProcedure(int code, UIntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardMessage
    {
        private uint _virtualKey;
        private uint _scanCode;
        private uint _flags;
        private uint _time;
        private UIntPtr _extraInfo;

        public uint VirtualKey => _virtualKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        private IntPtr _window;
        private uint _message;
        private UIntPtr _wParam;
        private IntPtr _lParam;
        private uint _time;
        private int _x;
        private int _y;
        private uint _private;
    }

    /// <summary>최초 씬 로드 전에 공통 관리자를 한 개 생성한다. 키보드와 씬의 수명에 관계없이 같은 훅을 유지한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_instance != null) return;
        new GameObject("WindowsSpecialKeyInput").AddComponent<WindowsSpecialKeyInput>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _windowSearchStarted = Time.unscaledTime;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground != _gameWindow && IsPlayerWindow(foreground))
            Interlocked.Exchange(ref _gameWindow, foreground);
        if (_gameWindow == IntPtr.Zero && Application.isFocused && !_windowWarningIssued
            && Time.unscaledTime - _windowSearchStarted >= 2f)
        {
            _windowWarningIssued = true;
            Debug.LogError("Unity 게임 창을 식별하지 못해 Windows 특수키 차단을 설치하지 않았습니다.", this);
        }
        bool active = !_quitting && _dialogDepth == 0 && IsGameForeground();
        if (!active || !_wasActive) ClearGameState();
        if (active && !_attempted && _gameWindow != IntPtr.Zero && _unhookError == 0) StartHook();
        Volatile.Write(ref _allowBlocking, active ? 1 : 0);
        bool installed = Volatile.Read(ref _installed) != 0;
        if (installed && !_reportedInstalled)
        {
            _reportedInstalled = true;
            Debug.Log("Windows 특수키 훅 설치 완료. Windows 키와 Print Screen만 차단 대상으로 등록했습니다.", this);
        }
        if (active != _wasActive)
            Debug.Log($"Windows 특수키 OS 차단 상태: {(active && installed ? "활성" : "비활성")} · 게임 창={_gameWindow} · 전경 창={foreground} · 훅 설치={installed}", this);
        _wasActive = active;
        for (int index = 0; index < 3; index++)
        {
            _frameHeld[index] = active && Volatile.Read(ref _gameHeld[index]) != 0;
            _framePressed[index] = Interlocked.Exchange(ref _pressed[index], 0) != 0 && active;
            int received = Volatile.Read(ref _receivedEvents[index]);
            if (received != _reportedEvents[index])
            {
                _reportedEvents[index] = received;
                Debug.Log($"Windows 특수키 훅 수신: 슬롯={index} · 수신={received} · 차단={Volatile.Read(ref _blockedEvents[index])} · 이벤트 전경 창={Interlocked.Read(ref _eventWindows[index])} · 게임 창={_gameWindow} · 차단 허용={Volatile.Read(ref _eventEnabled[index])} · 게임 눌림={_frameHeld[index]} · 새 누름={_framePressed[index]}", this);
            }
        }
        int error = Interlocked.Exchange(ref _nativeError, 0);
        if (error != 0) Debug.LogError($"Windows 특수키 훅 설치 또는 메시지 루프 실패: {error}. OS 차단이 활성화되지 않았습니다.", this);
        if (_unhookError != 0 && _attempted)
        {
            Debug.LogError($"Windows 특수키 훅 해제 실패: {_unhookError}. 차단을 끄고 재설치를 중단합니다.", this);
            _attempted = false;
        }
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        Volatile.Write(ref _allowBlocking, 0);
        _wasActive = false;
        ClearGameState();
    }

    void OnApplicationQuit()
    {
        _quitting = true;
        StopHook();
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        StopHook();
        _instance = null;
    }

    /// <summary>전용 메시지 루프 스레드를 한 번 시작한다. 설치 실패 시 매 프레임 재시도하지 않는다.</summary>
    private void StartHook()
    {
        if (_hookThread != null && _hookThread.IsAlive) return;
        _attempted = true;
        Volatile.Write(ref _stopRequested, 0);
        _hookThread = new Thread(RunHook) { IsBackground = true, Name = "Game special keys" };
        _hookThread.Start();
    }

    /// <summary>차단을 즉시 해제하고 훅 스레드에 종료를 전달한다. 파일창을 열기 전에 종료를 기다리고 실패는 메인 스레드에서 기록한다.</summary>
    private void StopHook()
    {
        Volatile.Write(ref _allowBlocking, 0);
        Volatile.Write(ref _stopRequested, 1);
        if (Volatile.Read(ref _physicalHeld[2]) != 0) Interlocked.Exchange(ref _ignorePrintRelease, 1);
        ClearGameState();
        _wasActive = false;
        Thread thread = _hookThread;
        if (thread != null && thread.IsAlive)
        {
            uint threadId = Volatile.Read(ref _threadId);
            if (threadId != 0 && !PostThreadMessageW(threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero))
                Debug.LogError($"Windows 특수키 훅 종료 요청 실패: {Marshal.GetLastWin32Error()}", this);
            if (!thread.Join(1500))
            {
                Debug.LogError("Windows 특수키 훅 종료 대기 시간 초과. OS 차단은 비활성화했습니다.", this);
                return;
            }
        }
        _hookThread = null;
        _attempted = false;
        _reportedInstalled = false;
        if (_unhookError != 0) Debug.LogError($"Windows 특수키 훅 해제 실패: {_unhookError}", this);
    }

    /// <summary>전용 스레드에서 메시지 큐와 저수준 훅을 만들고 종료 시 해제한다. Unity API를 호출하지 않고 오류 번호만 메인 스레드에 전달한다.</summary>
    private static void RunHook()
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            PeekMessageW(out NativeMessage unused, IntPtr.Zero, 0, 0, 0);
            Volatile.Write(ref _threadId, GetCurrentThreadId());
            if (Volatile.Read(ref _stopRequested) != 0) return;
            uint[] keys = { VK_LWIN, VK_RWIN, VK_SNAPSHOT };
            for (int index = 0; index < keys.Length; index++)
                Interlocked.Exchange(ref _physicalHeld[index], (GetAsyncKeyState((int)keys[index]) & 0x8000) != 0 ? 1 : 0);
            hook = SetWindowsHookExW(WH_KEYBOARD_LL, _callback, GetModuleHandleW(null), 0);
            if (hook == IntPtr.Zero) { Volatile.Write(ref _nativeError, Math.Max(1, Marshal.GetLastWin32Error())); return; }
            Volatile.Write(ref _installed, 1);
            while (Volatile.Read(ref _stopRequested) == 0)
            {
                int result = GetMessageW(out NativeMessage message, IntPtr.Zero, 0, 0);
                if (result == 0) break;
                if (result < 0) { Volatile.Write(ref _nativeError, Math.Max(1, Marshal.GetLastWin32Error())); break; }
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _nativeError, exception.HResult);
        }
        finally
        {
            Volatile.Write(ref _installed, 0);
            if (hook != IntPtr.Zero && !UnhookWindowsHookEx(hook))
                Volatile.Write(ref _unhookError, Math.Max(1, Marshal.GetLastWin32Error()));
            Volatile.Write(ref _threadId, 0);
        }
    }

    /// <summary>대상 세 키의 상태만 원자적으로 기록하고 활성 게임 창의 OS 처리를 차단한다. 다른 키와 외부 창의 입력은 다음 훅으로 전달한다.</summary>
    [AOT.MonoPInvokeCallback(typeof(HookProcedure))]
    private static IntPtr HandleKeyboard(int code, UIntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, message, data);
        KeyboardMessage input = Marshal.PtrToStructure<KeyboardMessage>(data);
        int index = input.VirtualKey == VK_LWIN ? 0 : input.VirtualKey == VK_RWIN ? 1 : input.VirtualKey == VK_SNAPSHOT ? 2 : -1;
        if (index < 0) return CallNextHookEx(IntPtr.Zero, code, message, data);
        Interlocked.Increment(ref _receivedEvents[index]);
        uint kind = message.ToUInt32();
        bool down = kind == WM_KEYDOWN || kind == WM_SYSKEYDOWN;
        bool up = kind == WM_KEYUP || kind == WM_SYSKEYUP;
        if (!down && !up) return CallNextHookEx(IntPtr.Zero, code, message, data);
        IntPtr foreground = GetForegroundWindow();
        bool active = Volatile.Read(ref _dialogDepth) == 0 && Volatile.Read(ref _stopRequested) == 0
            && foreground == Volatile.Read(ref _gameWindow) && foreground != IntPtr.Zero && !IsIconic(foreground);
        Interlocked.Exchange(ref _eventWindows[index], foreground.ToInt64());
        Volatile.Write(ref _eventEnabled[index], active ? 1 : 0);
        if (down)
        {
            bool fresh = Interlocked.Exchange(ref _physicalHeld[index], 1) == 0;
            if (fresh)
            {
                if (index == 2) Interlocked.Exchange(ref _ignorePrintRelease, 0);
                Interlocked.Exchange(ref _blockedDown[index], active ? 1 : 0);
                if (active)
                {
                    Interlocked.Exchange(ref _gameHeld[index], 1);
                    Interlocked.Exchange(ref _pressed[index], 1);
                }
            }
            if (!active) Interlocked.Exchange(ref _gameHeld[index], 0);
            if (active && Volatile.Read(ref _blockedDown[index]) != 0) return RecordBlockedEvent(index);
        }
        else
        {
            bool hadDown = Interlocked.Exchange(ref _physicalHeld[index], 0) != 0;
            bool blocked = Interlocked.Exchange(ref _blockedDown[index], 0) != 0;
            Interlocked.Exchange(ref _gameHeld[index], 0);
            // Print Screen이 해제 메시지만 전달되는 경우에도 한 프레임의 누름만 전달한다.
            bool ignoreRelease = index == 2 && Interlocked.Exchange(ref _ignorePrintRelease, 0) != 0;
            if (index == 2 && !hadDown && !ignoreRelease && active) Interlocked.Exchange(ref _pressed[index], 1);
            if (active && (blocked || (index == 2 && !hadDown))) return RecordBlockedEvent(index);
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    /// <summary>index의 OS 차단 횟수를 원자적으로 기록한다. Unity API 호출 없이 훅 차단 결과인 1을 반환한다.</summary>
    private static IntPtr RecordBlockedEvent(int index)
    {
        Interlocked.Increment(ref _blockedEvents[index]);
        return new IntPtr(1);
    }

    /// <summary>게임 창이 전경이며 최소화되지 않았는지 반환한다. 네이티브 조회만 사용해 훅 콜백에서도 Unity 객체 접근을 피한다.</summary>
    private static bool IsGameForeground()
    {
        IntPtr window = Volatile.Read(ref _gameWindow);
        return window != IntPtr.Zero && GetForegroundWindow() == window && !IsIconic(window);
    }

    /// <summary>window가 현재 프로세스의 Unity 게임 창인지 반환한다. 호출 스레드의 활성 창에 의존하지 않고 파일 대화상자와 외부 프로그램을 제외한다.</summary>
    private static bool IsPlayerWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint process);
        if (process != GetCurrentProcessId()) return false;
        var windowClass = new StringBuilder(128);
        return GetClassNameW(window, windowClass, windowClass.Capacity) > 0
            && windowClass.ToString() == "UnityWndClass";
    }

    /// <summary>현재 훅이 설치되었고 게임 창이 실제 입력 대상인지 반환한다. 실패 및 대화상자 상태에서는 훅 입력을 사용하지 않는다.</summary>
    private static bool OwnsInput()
    {
        return Volatile.Read(ref _installed) != 0 && Volatile.Read(ref _allowBlocking) != 0 && IsGameForeground();
    }

    /// <summary>key가 대상 특수키이면 고정 슬롯을 반환한다. 다른 키는 -1을 반환한다.</summary>
    private static int GetSpecialIndex(Key key)
    {
        return key == Key.LeftMeta ? 0 : key == Key.RightMeta ? 1 : key == Key.PrintScreen ? 2 : -1;
    }

    /// <summary>게임에 전달할 눌림과 프레임 상태를 비운다. 물리 키 추적은 유지해 포커스 복귀의 반복 메시지를 새 입력으로 처리하지 않는다.</summary>
    private static void ClearGameState()
    {
        for (int index = 0; index < 3; index++)
        {
            Interlocked.Exchange(ref _gameHeld[index], 0);
            Interlocked.Exchange(ref _pressed[index], 0);
            _frameHeld[index] = false;
            _framePressed[index] = false;
        }
    }

    /// <summary>hookId, callback, module, threadId로 훅을 설치하고 핸들을 반환한다. 실패 원인은 마지막 Win32 오류로 제공한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int hookId, HookProcedure callback, IntPtr module, uint threadId);
    /// <summary>hook을 해제하고 성공 여부를 반환한다. 실패 원인은 마지막 Win32 오류로 제공한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    /// <summary>code, message, data를 다음 훅에 전달하고 네이티브 결과를 반환한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, UIntPtr message, IntPtr data);
    /// <summary>window의 메시지를 가져와 message로 반환하며 종료는 0, 오류는 -1을 반환한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int GetMessageW(out NativeMessage message, IntPtr window, uint minimum, uint maximum);
    /// <summary>message의 가상 키 메시지를 문자 메시지로 변환하고 변환 여부를 반환한다. 훅 스레드의 표준 메시지 루프를 유지한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);
    /// <summary>message를 대상 창 프로시저에 전달하고 처리 결과를 반환한다. 훅 스레드의 메시지를 버리지 않는다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr DispatchMessageW(ref NativeMessage message);
    /// <summary>window의 메시지를 조회해 스레드 큐를 생성한다. 조회 결과를 message로 반환한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);
    /// <summary>threadId의 메시지 큐에 message를 보내고 성공 여부를 반환한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    /// <summary>현재 전경 창 핸들을 반환한다. 게임 창과 외부 프로그램 및 대화상자를 구분한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();
    /// <summary>window의 최소화 여부를 반환한다. 최소화된 게임에서는 입력을 차단하지 않는다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    /// <summary>window의 클래스 이름을 buffer에 기록하고 길이를 반환한다. 게임 창만 선택한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, StringBuilder buffer, int capacity);
    /// <summary>window의 프로세스 ID를 process로 제공하고 소유 스레드 ID를 반환한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    /// <summary>key의 현재 Windows 눌림 비트를 반환해 훅 설치 전 유지된 키를 기준 상태로 수집한다.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int key);
    /// <summary>현재 훅 스레드의 네이티브 ID를 반환한다. 종료 메시지 전달에 사용한다.</summary>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
    /// <summary>현재 게임 프로세스 ID를 반환한다. 다른 프로그램 창을 게임 창으로 선택하지 않는다.</summary>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentProcessId();
    /// <summary>name 모듈의 핸들을 반환한다. null이면 현재 게임 실행 모듈을 훅 등록에 사용한다.</summary>
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);
#endif
}
