using System.Runtime.InteropServices;
using System.Text;
using Loop.Core.Actions;
using Loop.Core.Geometry;
using Loop.Core.Windows;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Windows;

/// <summary>Applies window actions to the foreground window via Win32.</summary>
public sealed class WindowManager
{
    // The window's visible bounds when the current gesture began (see KeyDecision.NewGesture).
    private IntPtr _gestureWindow;
    private Rect _gestureOrigin;

    /// <summary>Applies <paramref name="action"/> to the foreground window. Returns false if there was nothing to act on.</summary>
    /// <param name="newGesture">
    /// False when this action refines the previous one (e.g. the second key of a chord); the layout
    /// is then computed from the window's bounds before the gesture started, not its current bounds.
    /// </param>
    public bool TryApply(WindowAction action, bool newGesture = true)
    {
        var hwnd = GetTargetWindow();
        if (hwnd == IntPtr.Zero) return false;

        if (action == WindowAction.Minimize)
            return ShowWindow(hwnd, SW_MINIMIZE);

        // A maximized (or minimized) window ignores SetWindowPos sizing until restored.
        if (IsZoomed(hwnd) || IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        var workArea = GetWorkArea(hwnd);
        if (workArea is null || !TryGetWindowRect(hwnd, out var windowRect)) return false;
        var visible = GetVisibleFrame(hwnd) ?? windowRect;

        if (newGesture || hwnd != _gestureWindow)
        {
            _gestureWindow = hwnd;
            _gestureOrigin = visible;
        }

        var target = LayoutEngine.Compute(action, workArea.Value, _gestureOrigin);
        var outer = FrameAdjuster.ToWindowRect(target, windowRect, visible);
        var ok = SetWindowPos(hwnd, IntPtr.Zero, outer.Left, outer.Top, outer.Width, outer.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

        Log.Write($"{action} work={workArea} origin={_gestureOrigin} target={target} result={GetVisibleFrame(hwnd)} ok={ok}");
        return ok;
    }

    /// <summary>The top-level window that has focus, or zero if it is something Loop should not move.</summary>
    public static IntPtr GetTargetWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        var root = GetAncestor(hwnd, GA_ROOT);
        if (root != IntPtr.Zero) hwnd = root;

        if (!IsWindowVisible(hwnd) || IsCloaked(hwnd)) return IntPtr.Zero;
        if (WindowFilter.IsShellWindowClass(ReadClassName(hwnd))) return IntPtr.Zero;
        return hwnd;
    }

    private static Rect? GetWorkArea(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? ToRect(info.rcWork) : null;
    }

    private static bool TryGetWindowRect(IntPtr hwnd, out Rect rect)
    {
        var ok = GetWindowRect(hwnd, out var r);
        rect = ToRect(r);
        return ok;
    }

    private static Rect? GetVisibleFrame(IntPtr hwnd)
    {
        var hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>());
        return hr == 0 ? ToRect(r) : null;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ReadClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    private static Rect ToRect(RECT r) => Rect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
