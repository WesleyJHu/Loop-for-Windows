using Loop.Core.Geometry;

namespace Loop.Core.Actions;

/// <summary>How a planned action changes the window.</summary>
public enum PlanKind
{
    /// <summary>Move and resize the window to <see cref="PlannedAction.Target"/>.</summary>
    Move,
    Minimize,
    /// <summary>Put the window in Windows' maximized state (like its maximize button) on <see cref="PlannedAction.Display"/>.</summary>
    Maximize,
    /// <summary>Take the window out of the maximized state (like its restore button).</summary>
    Restore,
}

/// <summary>A planned result for the window. <see cref="Target"/> is what the preview shows.</summary>
public readonly record struct PlannedAction(WindowAction Action, PlanKind Kind, Rect Target, DisplayArea Display, LayoutPlan Plan)
{
    public bool Minimize => Kind == PlanKind.Minimize;
}

/// <summary>
/// Plans what happens to one window during one trigger activation, without moving it.
///
/// In preview mode every key press is a <see cref="Step"/>: the planner simulates the window
/// landing at each step's target, so cycling (Left, Left, Left), chords, and display moves
/// (] then Left) all build on each other while the real window stays put. Releasing the trigger
/// calls <see cref="Finish"/> and the caller applies the final plan; Esc calls <see cref="Cancel"/>,
/// which restores the cycle state as if nothing happened.
///
/// In instant mode the caller does Begin, Step, Finish for every key press.
///
/// Maximize behaves like the window's maximize/restore button: it toggles Windows' maximized state.
/// While (simulated) maximized, the window's restored bounds are tracked, so restoring, centering,
/// or moving it to another display uses the size it had before it was maximized.
/// </summary>
public sealed class GesturePlanner
{
    private readonly LayoutSession _session;
    private IReadOnlyList<DisplayArea> _displays = Array.Empty<DisplayArea>();
    private int _display;
    private Rect _visible;
    private bool _maximized;
    private Rect? _restored;
    private CycleTracker.Entry? _savedEntry;

    public GesturePlanner(LayoutSession session) => _session = session;

    public bool IsActive { get; private set; }
    public nint Window { get; private set; }

    /// <summary>The result if the activation ended now, or null if nothing has been planned yet.</summary>
    public PlannedAction? Pending { get; private set; }

    /// <summary>Starts planning for a window.</summary>
    /// <param name="window">The window's handle.</param>
    /// <param name="visible">Its visible bounds.</param>
    /// <param name="maximized">Whether it is maximized.</param>
    /// <param name="displays">All displays, in display order.</param>
    /// <param name="restored">When maximized: the bounds it returns to when restored, if known.</param>
    public void Begin(nint window, Rect visible, bool maximized, IReadOnlyList<DisplayArea> displays, Rect? restored = null)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        Window = window;
        _visible = visible;
        _maximized = maximized;
        _restored = maximized ? restored : null;
        _displays = displays;
        _display = Math.Max(0, DisplayOrder.IndexOf(displays, visible));
        _savedEntry = _session.Cycles.Peek(window);
        Pending = null;
        IsActive = true;
    }

    /// <summary>Plans one key press. Returns the new pending result (unchanged if the key does nothing).</summary>
    public PlannedAction? Step(WindowAction action, bool newGesture)
    {
        if (!IsActive) throw new InvalidOperationException("Call Begin first.");

        if (action == WindowAction.Minimize)
            return Pending = new PlannedAction(action, PlanKind.Minimize, _visible, _displays[_display], default);

        if (action == WindowAction.Maximize)
            return _maximized ? PlanRestore(action) : PlanMaximize(action, _display);

        if (action.IsDisplayMove())
        {
            if (DisplayOrder.Resolve(action, _display, _displays.Count) is not { } dest) return Pending;
            if (_maximized)
            {
                // A maximized window stays maximized; its restored bounds move along with it.
                if (_restored is { } r) _restored = LayoutEngine.Remap(r, _displays[_display].WorkArea, _displays[dest].WorkArea);
                return PlanMaximize(action, dest);
            }

            var move = _session.PlanDisplayMove(Window, _visible, _displays[_display].WorkArea, _displays[dest].WorkArea);
            _display = dest;
            return Moved(action, move);
        }

        // Layouts of a maximized window start from its restored size (e.g. Center keeps that size).
        var basis = _maximized && _restored is { } restored ? restored : _visible;
        return Moved(action, _session.PlanLayout(Window, action, newGesture, basis, _displays[_display].WorkArea));
    }

    private PlannedAction Moved(WindowAction action, LayoutPlan plan)
    {
        // Simulate the window landing there, so the next step builds on this one.
        _session.Landed(Window, plan, plan.Target);
        _visible = plan.Target;
        _maximized = false;
        _restored = null;
        return (Pending = new PlannedAction(action, PlanKind.Move, plan.Target, _displays[_display], plan)).Value;
    }

    private PlannedAction PlanMaximize(WindowAction action, int display)
    {
        _session.EndGesture();
        if (!_maximized) _restored = _visible;
        _display = display;
        var work = _displays[display].WorkArea;
        var plan = new LayoutPlan(work, WindowAction.Maximize, 0);
        _session.Landed(Window, plan, work);
        _visible = work;
        _maximized = true;
        return (Pending = new PlannedAction(action, PlanKind.Maximize, work, _displays[display], plan)).Value;
    }

    private PlannedAction PlanRestore(WindowAction action)
    {
        _session.EndGesture();
        var target = _restored ?? _visible;
        _display = Math.Max(0, DisplayOrder.IndexOf(_displays, target));
        var plan = new LayoutPlan(target, null, 0); // a restored window is free-floating
        _session.Landed(Window, plan, target);
        _visible = target;
        _maximized = false;
        _restored = null;
        return (Pending = new PlannedAction(action, PlanKind.Restore, target, _displays[_display], plan)).Value;
    }

    /// <summary>Ends the activation and returns what to apply (null if nothing was planned).</summary>
    public PlannedAction? Finish()
    {
        var result = Pending;
        if (IsActive && result is null) _session.Cycles.Restore(Window, _savedEntry);
        Reset();
        return result;
    }

    /// <summary>Ends the activation without applying anything, restoring the window's cycle state.</summary>
    public void Cancel()
    {
        if (!IsActive) return;
        _session.Cycles.Restore(Window, _savedEntry);
        _session.EndGesture();
        Reset();
    }

    /// <summary>After applying a finished plan, records where the window really landed.</summary>
    public void ReportLanded(nint window, PlannedAction applied, Rect landed)
    {
        if (applied.Minimize) _session.Forget(window);
        else _session.Landed(window, applied.Plan, landed);
    }

    private void Reset()
    {
        IsActive = false;
        Pending = null;
    }
}
