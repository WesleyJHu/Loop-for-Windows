using System.ComponentModel;
using System.Runtime.InteropServices;
using Loop.Core.Input;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Input;

/// <summary>
/// A global low-level keyboard hook (WH_KEYBOARD_LL) on its own thread.
///
/// Windows calls a low-level hook on the thread that installed it, by sending that thread a
/// message, and skips the hook for a key if the thread doesn't answer within LowLevelHooksTimeout.
/// If the hook lived on the UI thread, anything that keeps the UI busy (rendering the preview,
/// finishing an animation, waiting on a slow app's SetWindowPos) would make Windows pass keys
/// straight through without Loop seeing them. So the hook gets a dedicated thread that does
/// nothing but pump messages and run the (fast) handler.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly Func<KeyEvent, bool> _handler;
    private readonly LowLevelKeyboardProc _proc; // kept in a field so the GC never collects it
    private IntPtr _hook;
    private Thread? _thread;
    private uint _threadId;

    /// <param name="handler">Runs on the hook thread; returns true to swallow the key. Must be quick.</param>
    public KeyboardHook(Func<KeyEvent, bool> handler)
    {
        _handler = handler;
        _proc = HookCallback;
    }

    /// <summary>Starts the hook thread and installs the hook on it. Throws if the hook can't be installed.</summary>
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
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                failure = new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the keyboard hook.");
            ready.Set();
            if (failure is not null) return;

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "Loop keyboard hook",
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

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
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
                    if ((isDown || isUp) && _handler(new KeyEvent((int)data.vkCode, isDown)))
                        return 1;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Loop] keyboard hook error: {ex}");
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
