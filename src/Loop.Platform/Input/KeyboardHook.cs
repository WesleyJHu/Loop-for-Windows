using System.ComponentModel;
using System.Runtime.InteropServices;
using Loop.Core.Input;
using Loop.Platform.Interop;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Input;

/// <summary>
/// A global low-level keyboard hook (WH_KEYBOARD_LL).
///
/// The hook must be installed on a thread that pumps messages (the WPF UI thread does).
/// Windows silently uninstalls hooks whose callback exceeds LowLevelHooksTimeout, so the
/// handler must only make a decision and return; real work belongs elsewhere.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly Func<KeyEvent, bool> _handler;
    private readonly LowLevelKeyboardProc _proc; // kept in a field so the GC never collects it
    private IntPtr _hook;

    /// <param name="handler">Returns true to swallow the key.</param>
    public KeyboardHook(Func<KeyEvent, bool> handler)
    {
        _handler = handler;
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the keyboard hook.");
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
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
