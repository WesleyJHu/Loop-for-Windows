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

    /// <summary>For a maximize: the restored bounds to give the window first (when undoing to a maximized origin).</summary>
    public Rect? NormalBounds { get; init; }

    /// <summary>Where the window was before this activation, to remember for "restore previous" once applied.</summary>
    public WindowOrigin? RecordOrigin { get; init; }

    /// <summary>
    /// The window's layout (for cycling) before this activation. For an undo: the layout to put back.
    /// </summary>
    public CycleTracker.Entry? PreviousCycle { get; init; }

    /// <summary>The window's "restore previous" origin before this activation. For an undo: the origin to put back.</summary>
    public WindowOrigin? PreviousOrigin { get; init; }
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
///
/// "Restore previous" undoes Loop: it returns the window to where it was before Loop first moved
/// it. That origin is recorded when Loop moves a window that isn't in one of Loop's layouts (it
/// was placed by the user or the app), and kept while the window stays in Loop's layouts.
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
    private WindowOrigin? _originCandidate; // set when the window wasn't in a Loop layout at Begin
    private bool _changed;                 // a step has (virtually) moved the window this activation

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
        _originCandidate = _session.Cycles.Current(window, visible) is null
            ? new WindowOrigin(maximized && restored is { } r ? r : visible, maximized)
            : null;
        _changed = false;
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

        if (action == WindowAction.RestorePrevious)
            return PlanRestorePrevious(action);

        if (action == WindowAction.Undo)
            return Pending; // undo works on Loop's history, not this window (see UndoHistory)

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

    /// <summary>Back to where the window was before Loop first moved it.</summary>
    private PlannedAction? PlanRestorePrevious(WindowAction action)
    {
        // A window that wasn't in a Loop layout when the trigger was pressed has nothing to undo,
        // unless it was moved earlier in this same activation (then undo goes back to its start).
        var origin = _originCandidate is { } candidate ? (_changed ? candidate : (WindowOrigin?)null) : _session.OriginOf(Window);
        if (origin is not { } o) return Pending;

        // Its display may be gone (unplugged): then bring it onto the nearest one.
        var index = Math.Max(0, DisplayOrder.IndexOf(_displays, o.Bounds));
        var display = _displays[index];
        var bounds = display.Bounds.Intersect(o.Bounds).IsEmpty ? LayoutEngine.ClampInto(o.Bounds, display.WorkArea) : o.Bounds;

        _session.EndGesture();
        if (o.Maximized)
        {
            _maximized = false;
            _visible = bounds;
            return (Pending = PlanMaximize(action, index) with { NormalBounds = bounds }).Value;
        }

        _display = index;
        return Moved(action, new LayoutPlan(bounds, null, 0));
    }

    private PlannedAction Moved(WindowAction action, LayoutPlan plan)
    {
        // Simulate the window landing there, so the next step builds on this one.
        _session.Landed(Window, plan, plan.Target);
        _visible = plan.Target;
        _maximized = false;
        _restored = null;
        _changed = true;
        return (Pending = new PlannedAction(action, PlanKind.Move, plan.Target, _displays[_display], plan)).Value;
    }

    private PlannedAction PlanMaximize(WindowAction action, int display)
    {
        _session.EndGesture();
        _changed = true;
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
        _changed = true;
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
        var result = Pending is { } p
            ? p with { RecordOrigin = _originCandidate, PreviousCycle = _savedEntry, PreviousOrigin = _session.OriginOf(Window) }
            : (PlannedAction?)null;
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
        if (applied.Action == WindowAction.Undo)
        {
            // Back where it was, so its layout and origin are what they were then.
            _session.EndGesture();
            _session.Cycles.Restore(window, applied.PreviousCycle);
            if (applied.PreviousOrigin is { } previous) _session.RememberOrigin(window, previous);
            else _session.ForgetOrigin(window);
            return;
        }

        if (applied.Minimize)
        {
            // Restored from the taskbar, the window comes back exactly where it was, so its layout
            // (for cycling) and origin (for undo) stay valid.
            _session.EndGesture();
            return;
        }

        _session.Landed(window, applied.Plan, landed);
        if (applied.Action == WindowAction.RestorePrevious) _session.ForgetOrigin(window); // undone
        else if (applied.RecordOrigin is { } origin) _session.RememberOrigin(window, origin);
    }

    private void Reset()
    {
        IsActive = false;
        Pending = null;
    }
}
