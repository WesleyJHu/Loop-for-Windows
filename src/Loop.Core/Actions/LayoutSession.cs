using Loop.Core.Geometry;

namespace Loop.Core.Actions;

/// <summary>Where to put a window, and what to remember about it once it lands.</summary>
/// <param name="Target">The visible rect to place the window at.</param>
/// <param name="Layout">The layout to record for cycling and display moves; null for a free-floating window.</param>
/// <param name="Index">The cycle index of <paramref name="Layout"/>.</param>
public readonly record struct LayoutPlan(Rect Target, WindowAction? Layout, int Index);

/// <summary>
/// Decides target rectangles for layout and display-move actions, tracking gestures and size
/// cycles. Pure logic: the platform layer measures the window, calls a Plan method, moves the
/// window, then reports where it landed with <see cref="Landed"/>.
///
/// A gesture is one press of the trigger keys' action, possibly refined by more chord keys
/// (Left, then Up while Left is held). Every step of a gesture is computed against the window as
/// it was when the gesture began: Center keeps the original size (not the half the first key gave
/// it), and cycling compares with the layout from before the gesture, so a chord whose keys land
/// slower than the grace period still cycles on its next press.
/// </summary>
public sealed class LayoutSession
{
    private readonly CycleTracker _cycles;
    private nint _gestureWindow;
    private Rect _gestureOrigin;
    private CycleTracker.Entry? _gestureBase;

    /// <param name="cycles">Cycle state (sizes to cycle through).</param>
    /// <param name="gap">Pixels between tiled windows and screen edges.</param>
    public LayoutSession(CycleTracker? cycles = null, int gap = 0)
    {
        _cycles = cycles ?? new CycleTracker();
        Gap = Math.Max(0, gap);
    }

    public CycleTracker Cycles => _cycles;
    public int Gap { get; }

    /// <param name="window">The window handle.</param>
    /// <param name="action">A layout action (<see cref="WindowActionExtensions.IsLayout"/>).</param>
    /// <param name="newGesture">False when refining the previous action with another chord key.</param>
    /// <param name="visible">The window's current visible bounds.</param>
    /// <param name="workArea">The work area of the window's monitor.</param>
    public LayoutPlan PlanLayout(nint window, WindowAction action, bool newGesture, Rect visible, Rect workArea)
    {
        if (newGesture || window != _gestureWindow)
        {
            _gestureWindow = window;
            _gestureOrigin = visible;
            _gestureBase = _cycles.Current(window, visible);
        }

        var index = _cycles.NextIndex(_gestureBase, action);
        var target = LayoutEngine.Compute(action, workArea, _gestureOrigin, _cycles.FractionAt(index), Gap);
        return new LayoutPlan(target, action, index);
    }

    /// <summary>
    /// Plans moving a (non-maximized) window between displays, keeping its layout: a Loop layout is
    /// re-applied on the destination (a left third stays a left third), and a free-floating window
    /// keeps its relative position and size. Maximized windows are handled by <see cref="GesturePlanner"/>.
    /// </summary>
    public LayoutPlan PlanDisplayMove(nint window, Rect visible, Rect fromWorkArea, Rect toWorkArea)
    {
        EndGesture(); // a display move ends any gesture in progress

        if (_cycles.Current(window, visible) is { } entry)
            return new LayoutPlan(
                LayoutEngine.Compute(entry.Action, toWorkArea, visible, _cycles.FractionAt(entry.Index), Gap),
                entry.Action,
                entry.Index);

        return new LayoutPlan(LayoutEngine.Remap(visible, fromWorkArea, toWorkArea), null, 0);
    }

    /// <summary>Records where the window landed (apps may enforce minimum sizes or snap to a grid).</summary>
    public void Landed(nint window, LayoutPlan plan, Rect landed)
    {
        if (plan.Layout is { } layout) _cycles.Record(window, layout, plan.Index, landed);
        else _cycles.Forget(window);
    }

    /// <summary>Ends the current gesture, so the next action starts a new one.</summary>
    public void EndGesture() => _gestureWindow = 0;

    /// <summary>Forgets a window's layout (e.g. after minimizing it).</summary>
    public void Forget(nint window)
    {
        _cycles.Forget(window);
        if (window == _gestureWindow) EndGesture();
    }
}
