using System.ComponentModel;
using System.Runtime.InteropServices;
using Loop.Core.Input;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Input;

/// <summary>
/// Global low-level keyboard and mouse hooks (WH_KEYBOARD_LL, WH_MOUSE_LL) on their own thread.
///
/// Windows calls a low-level hook on the thread that installed it, by sending that thread a
/// message, and skips the hook for an event if the thread doesn't answer within LowLevelHooksTimeout.
/// If the hooks lived on the UI thread, anything that keeps the UI busy (rendering the preview,
/// finishing an animation, waiting on a slow app's SetWindowPos) would make Windows pass input
/// straight through without Loop seeing it. So the hooks get a dedicated thread that does
/// nothing but pump messages and run the (fast) handlers.
///
/// The mouse hook sees every mouse move on the system, so it is only installed while
/// <see cref="MouseEnabled"/> is true.
/// </summary>
public sealed class InputHook : IDisposable
{
    /// <summary>Thread message asking the hook thread to install or remove the mouse hook.</summary>
    private const uint WM_APP_UPDATE_MOUSE_HOOK = 0x8000 + 1;

    private readonly Func<KeyEvent, bool> _keyHandler;
    private readonly Func<MouseEvent, bool> _mouseHandler;
    private readonly LowLevelKeyboardProc _keyProc;  // kept in fields so the GC never collects them
    private readonly LowLevelMouseProc _mouseProc;
    private IntPtr _keyHook;
    private IntPtr _mouseHook;
    private volatile bool _mouseWanted;
    private Thread? _thread;
    private uint _threadId;

    /// <param name="keyHandler">Runs on the hook thread; returns true to swallow the key. Must be quick.</param>
    /// <param name="mouseHandler">Runs on the hook thread; returns true to swallow the event. Must be very quick (it sees every move).</param>
    public InputHook(Func<KeyEvent, bool> keyHandler, Func<MouseEvent, bool> mouseHandler)
    {
        _keyHandler = keyHandler;
        _mouseHandler = mouseHandler;
        _keyProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    /// <summary>Whether the mouse hook should be installed. Can be changed at any time, from any thread.</summary>
    public bool MouseEnabled
    {
        get => _mouseWanted;
        set
        {
            _mouseWanted = value;
            if (_thread is not null) PostThreadMessage(_threadId, WM_APP_UPDATE_MOUSE_HOOK, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Starts the hook thread and installs the hooks on it. Throws if the keyboard hook can't be installed.</summary>
    public void Start()
    {
        if (_thread is not null) return;

        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            // Make sure the thread has a message queue before anyone posts to it.
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, GetModuleHandle(null), 0);
            if (_keyHook == IntPtr.Zero)
                failure = new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the keyboard hook.");
            else
                UpdateMouseHook();
            ready.Set();
            if (failure is not null) return;

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_UPDATE_MOUSE_HOOK)
                {
                    UpdateMouseHook();
                    continue;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
            UnhookWindowsHookEx(_keyHook);
            _keyHook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "Loop input hooks",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        ready.Wait();
        if (failure is not null)
        {
            _thread = null;
            throw failure;
        }
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    /// <summary>Installs or removes the mouse hook to match <see cref="MouseEnabled"/>. Hook thread only.</summary>
    private void UpdateMouseHook()
    {
        if (_mouseWanted && _mouseHook == IntPtr.Zero)
        {
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero) Log.Write($"failed to install the mouse hook: error {Marshal.GetLastWin32Error()}");
        }
        else if (!_mouseWanted && _mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Exceptions must never escape into native code; on any failure, let the key through.
        try
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (data.dwExtraInfo != InputInjector.Signature)
                {
                    var msg = (int)wParam;
                    var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                    var isUp = msg is WM_KEYUP or WM_SYSKEYUP;
                    if ((isDown || isUp) && _keyHandler(new KeyEvent((int)data.vkCode, isDown)))
                        return 1;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Loop] keyboard hook error: {ex}");
        }
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Exceptions must never escape into native code; on any failure, let the event through.
        try
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var xButton = (data.mouseData >> 16) == XBUTTON1 ? MouseButton.X1 : MouseButton.X2;
                (MouseEventKind Kind, MouseButton Button)? e = (int)wParam switch
                {
                    WM_MOUSEMOVE => (MouseEventKind.Move, MouseButton.Left),
                    WM_LBUTTONDOWN => (MouseEventKind.Down, MouseButton.Left),
                    WM_LBUTTONUP => (MouseEventKind.Up, MouseButton.Left),
                    WM_RBUTTONDOWN => (MouseEventKind.Down, MouseButton.Right),
                    WM_RBUTTONUP => (MouseEventKind.Up, MouseButton.Right),
                    WM_MBUTTONDOWN => (MouseEventKind.Down, MouseButton.Middle),
                    WM_MBUTTONUP => (MouseEventKind.Up, MouseButton.Middle),
                    WM_XBUTTONDOWN => (MouseEventKind.Down, xButton),
                    WM_XBUTTONUP => (MouseEventKind.Up, xButton),
                    WM_MOUSEWHEEL or WM_MOUSEHWHEEL => (MouseEventKind.Wheel, MouseButton.Middle),
                    _ => null,
                };
                if (e is { } ev &&
                    _mouseHandler(new MouseEvent(ev.Kind, data.pt.X, data.pt.Y, data.time, ev.Button,
                        Injected: data.dwExtraInfo == InputInjector.Signature)))
                    return 1;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Loop] mouse hook error: {ex}");
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
