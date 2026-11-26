using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Geometry;
using Loop.Core.Windows;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Windows;

/// <summary>
/// Measures and moves the foreground window. The decisions (targets, cycling, gestures, previews)
/// live in <see cref="GesturePlanner"/> and <see cref="LayoutSession"/>; this class only talks to
/// Win32. Use from one thread.
/// </summary>
public sealed class WindowManager
{
    private const uint MoveFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

    private readonly GesturePlanner _planner;
    private readonly IReadOnlyList<string> _excludedApps;
    private readonly AnimationOptions _animation;
    private readonly Func<bool> _systemAnimations;
    private readonly SynchronizationContext? _ui;
    private readonly WindowAnimator _animator = new();
    private Animating? _animating;
    private int _animationGeneration;

    /// <summary>A window gliding towards its target; its final placement is still to do.</summary>
    private sealed record Animating(IntPtr Window, PlannedAction Planned, int Generation);

    /// <param name="session">Layout state (cycle sizes, gap).</param>
    /// <param name="excludedApps">Process names never to move.</param>
    /// <param name="animation">Window-move animation settings (default: none).</param>
    /// <param name="systemAnimations">Whether Windows' animation effects are on (default: <see cref="SystemAnimations.Enabled"/>).</param>
    /// <remarks>Create on the UI thread: animations finish there, via its SynchronizationContext.</remarks>
    public WindowManager(LayoutSession? session = null, IReadOnlyList<string>? excludedApps = null,
        AnimationOptions? animation = null, Func<bool>? systemAnimations = null)
    {
        _planner = new GesturePlanner(session ?? new LayoutSession());
        _excludedApps = excludedApps ?? Array.Empty<string>();
        _animation = animation ?? AnimationOptions.None;
        _systemAnimations = systemAnimations ?? (() => SystemAnimations.Enabled);
        _ui = SynchronizationContext.Current;
    }

    public bool IsPreviewing => _planner.IsActive;

    /// <summary>
    /// Plans <paramref name="action"/> without moving anything, starting a preview for the foreground
    /// window if none is active. Returns the pending result, or null when there is no window to act on.
    /// </summary>
    public PlannedAction? Preview(WindowAction action, bool newGesture)
    {
        if (!_planner.IsActive)
        {
            FinishAnimation(); // measure the window where it is going, not mid-glide
            if (!TryBegin()) return null;
        }
        return _planner.Step(action, newGesture);
    }

    /// <summary>Applies the pending preview. Returns false if there was nothing to apply.</summary>
    public bool CommitPreview()
    {
        var window = _planner.Window;
        if (_planner.Finish() is not { } planned) return false;
        FinishAnimation();
        return Apply(window, planned);
    }

    public void CancelPreview() => _planner.Cancel();

    /// <summary>Applies <paramref name="action"/> to the foreground window immediately.</summary>
    public bool ApplyNow(WindowAction action, bool newGesture)
    {
        _planner.Cancel();
        FinishAnimation(); // a new key press during a glide jumps it to its end first
        if (!TryBegin()) return false;
        _planner.Step(action, newGesture);
        return CommitPreview();
    }

    /// <summary>Ends a glide in progress by placing the window at its target now.</summary>
    public void FinishAnimation()
    {
        if (_animating is not { } a) return;
        _animating = null;
        _animator.Stop();
        Landed(a.Window, a.Planned, IsWindow(a.Window) && Place(a.Window, a.Planned.Target, a.Planned.Display) is not null);
    }

    /// <summary>Starts gliding the window to a planned move, if animating makes sense for it.</summary>
    private bool TryStartAnimation(IntPtr window, PlannedAction planned, bool wasMaximizedOrMinimized)
    {
        if (_ui is null || !TryMeasure(window, out var windowRect, out var visible)) return false;
        var displays = MonitorService.GetDisplays();
        if (displays.Count == 0) return false;
        var from = displays[Math.Max(0, DisplayOrder.IndexOf(displays, visible))];

        if (!MotionPolicy.ShouldAnimateWindow(_animation, _systemAnimations(), planned.Kind, visible, planned.Target,
                from, planned.Display, wasMaximizedOrMinimized))
            return false;

        var generation = ++_animationGeneration;
        _animating = new Animating(window, planned, generation);
        var ui = _ui;
        _animator.Run(window, visible, planned.Target, windowRect, MotionPolicy.FrameBounds(from, planned.Display), _animation,
            framesDone: () => ui.Post(_ =>
            {
                if (_animating?.Generation == generation) FinishAnimation();
            }, null));
        return true;
    }

    private bool TryBegin()
    {
        var hwnd = GetTargetWindow();
        if (hwnd == IntPtr.Zero || IsExcluded(hwnd) || !TryMeasure(hwnd, out _, out var visible)) return false;

        var displays = MonitorService.GetDisplays();
        if (displays.Count == 0) return false;

        var maximized = IsZoomed(hwnd);
        if (!maximized) RememberInsets(hwnd);
        _planner.Begin(hwnd, visible, maximized, displays, maximized ? GetRestoredBounds(hwnd, displays, visible) : null);
        return true;
    }

    /// <summary>A window's bounds and invisible-border widths the last time Loop saw it not maximized.</summary>
    private readonly record struct NormalState(Rect Visible, int Left, int Top, int Right, int Bottom);

    private readonly Dictionary<IntPtr, NormalState> _normal = new();

    private void RememberInsets(IntPtr hwnd)
    {
        if (IsZoomed(hwnd) || IsIconic(hwnd) || !TryMeasure(hwnd, out var outer, out var visible)) return;
        if (_normal.Count >= 256 && !_normal.ContainsKey(hwnd)) _normal.Clear();
        _normal[hwnd] = new NormalState(visible,
            visible.Left - outer.Left, visible.Top - outer.Top, outer.Right - visible.Right, outer.Bottom - visible.Bottom);
    }

    private bool Apply(nint window, PlannedAction planned)
    {
        if (!IsWindow(window)) return false;

        bool ok;
        switch (planned.Kind)
        {
            case PlanKind.Minimize:
                _planner.ReportLanded(window, planned, default);
                return ShowWindow(window, SW_MINIMIZE);

            case PlanKind.Restore:
                ShowWindow(window, SW_RESTORE); // exactly what the window's restore button does
                ok = true;
                break;

            case PlanKind.Maximize when CanMaximize(window):
                ok = Maximize(window, planned.Display);
                break;

            default:
                // Move, or "maximize" for windows without a maximize button (fixed-size dialogs):
                // those just fill the work area, since a real maximize would strand their content.
                // A maximized (or minimized) window ignores SetWindowPos sizing until restored.
                var wasMaximizedOrMinimized = IsZoomed(window) || IsIconic(window);
                if (wasMaximizedOrMinimized) ShowWindow(window, SW_RESTORE);
                if (TryStartAnimation(window, planned, wasMaximizedOrMinimized)) return true; // lands later
                ok = Place(window, planned.Target, planned.Display) is not null;
                break;
        }

        Landed(window, planned, ok);
        return ok;
    }

    private void Landed(IntPtr window, PlannedAction planned, bool ok)
    {
        Rect? landed = ok && TryMeasure(window, out _, out var r) ? r : null;
        if (landed is { } l) _planner.ReportLanded(window, planned, l);
        RememberInsets(window); // only records when the window ended up not maximized
        Log.Write($"{planned.Action} {planned.Kind} plan={planned.Plan} display={planned.Display.WorkArea} landed={landed}");
    }

    /// <summary>
    /// Puts the window in Windows' maximized state on <paramref name="dest"/>, as its maximize button
    /// would. Moving to another display keeps the window's restored size, moved to that display.
    /// </summary>
    private static bool Maximize(IntPtr hwnd, DisplayArea dest)
    {
        if (MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) == dest.Id)
        {
            if (!IsZoomed(hwnd)) ShowWindow(hwnd, SW_MAXIMIZE);
            return true;
        }

        // Another display: move the restored position there and maximize in one step, so the
        // window never visibly restores on the old display.
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        var displays = MonitorService.GetDisplays();
        if (GetWindowPlacement(hwnd, ref placement) && TryMeasure(hwnd, out _, out var visible))
        {
            var from = displays[Math.Max(0, DisplayOrder.IndexOf(displays, visible))];
            var normal = ToRect(placement.rcNormalPosition);
            var moved = LayoutEngine.Remap(normal, from.WorkArea, dest.WorkArea);
            placement.rcNormalPosition = new RECT { Left = moved.Left, Top = moved.Top, Right = moved.Right, Bottom = moved.Bottom };
            placement.showCmd = SW_SHOWMAXIMIZED;
            placement.flags = 0;
            if (SetWindowPlacement(hwnd, ref placement) && IsZoomed(hwnd) &&
                MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) == dest.Id)
                return true;
        }

        // Fallback: restore, move the restored window onto the destination, then maximize there.
        if (IsZoomed(hwnd) || IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (!TryMeasure(hwnd, out _, out var restored)) return false;
        var current = displays[Math.Max(0, DisplayOrder.IndexOf(displays, restored))];
        if (Place(hwnd, LayoutEngine.Remap(restored, current.WorkArea, dest.WorkArea), dest) is null) return false;
        ShowWindow(hwnd, SW_MAXIMIZE);
        return true;
    }

    private static bool CanMaximize(IntPtr hwnd) =>
        ((long)GetWindowLongPtr(hwnd, GWL_STYLE) & WS_MAXIMIZEBOX) != 0;

    /// <summary>
    /// Where a maximized window goes when restored, as visible bounds in screen coordinates.
    ///
    /// GetWindowPlacement reports an outer rect (with invisible borders) in workspace coordinates
    /// (offset by a taskbar docked at the top or left). For DPI-unaware windows whose restored rect
    /// touches a monitor with another scale, Windows also distorts its right and bottom edges (the
    /// top-left stays exact). So when Loop saw the window in its normal state and that state's
    /// top-left still matches, the remembered bounds are used; otherwise the reported rect.
    /// </summary>
    private Rect? GetRestoredBounds(IntPtr hwnd, IReadOnlyList<DisplayArea> displays, Rect visible)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return null;
        var display = displays[Math.Max(0, DisplayOrder.IndexOf(displays, visible))];
        var n = ToRect(placement.rcNormalPosition);
        var outer = n with
        {
            Left = n.Left + display.WorkArea.Left - display.Bounds.Left,
            Top = n.Top + display.WorkArea.Top - display.Bounds.Top,
        };
        if (!_normal.TryGetValue(hwnd, out var s)) return outer; // never seen restored: close enough

        var reported = Rect.FromEdges(outer.Left + s.Left, outer.Top + s.Top, outer.Right - s.Right, outer.Bottom - s.Bottom);
        if (Math.Abs(reported.Left - s.Visible.Left) <= 1 && Math.Abs(reported.Top - s.Visible.Top) <= 1)
            return s.Visible;
        return reported.IsEmpty ? outer : reported;
    }

    /// <summary>
    /// Moves the window so its visible frame covers <paramref name="target"/>. Returns where the
    /// visible frame actually landed, or null on failure.
    /// </summary>
    private static Rect? Place(IntPtr hwnd, Rect target, DisplayArea dest)
    {
        // Two passes: invisible-border widths depend on the window's DPI, which changes when it
        // crosses onto a monitor with a different scale. The second pass re-measures and corrects.
        for (var pass = 0; pass < 2; pass++)
        {
            if (!TryMeasure(hwnd, out var windowRect, out var visible)) return null;
            if (pass > 0 && visible == target) break;

            var outer = FrameAdjuster.ToWindowRect(target, windowRect, visible);
            foreach (var step in PlacementPlanner.Plan(outer, dest.Bounds))
            {
                var r = step.Rect;
                var flags = step.SizeOnly ? MoveFlags | SWP_NOMOVE : MoveFlags;
                if (!SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags)) return null;
            }

            if (TryMeasure(hwnd, out _, out var landed) && landed == target) return landed;
        }

        return TryMeasure(hwnd, out _, out var final) ? final : null;
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

    private bool IsExcluded(IntPtr hwnd)
    {
        if (_excludedApps.Count == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return WindowFilter.IsExcluded(process.ProcessName, _excludedApps);
        }
        catch (ArgumentException)
        {
            return false; // the process exited
        }
    }

    /// <summary>Reads the outer window rect and the visible frame (falls back to the outer rect).</summary>
    private static bool TryMeasure(IntPtr hwnd, out Rect windowRect, out Rect visible)
    {
        if (!GetWindowRect(hwnd, out var r))
        {
            windowRect = visible = default;
            return false;
        }
        windowRect = ToRect(r);
        visible = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT f, Marshal.SizeOf<RECT>()) == 0
            ? ToRect(f)
            : windowRect;
        return true;
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
