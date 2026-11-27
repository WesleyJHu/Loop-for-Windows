using Loop.Core.Geometry;

namespace Loop.Core.Actions;

/// <summary>How a window was placed just before Loop acted on it.</summary>
/// <param name="Visible">Its visible bounds (meaningless while minimized).</param>
/// <param name="Maximized">Whether it was in Windows' maximized state.</param>
/// <param name="Minimized">Whether it was minimized.</param>
/// <param name="Restored">When maximized or minimized: the bounds it returns to when restored, if known.</param>
public readonly record struct WindowSnapshot(Rect Visible, bool Maximized, bool Minimized, Rect? Restored = null);

/// <summary>One action Loop applied, with what is needed to take it back.</summary>
/// <param name="Window">The window acted on.</param>
/// <param name="Action">What was done to it.</param>
/// <param name="Before">Where the window was before.</param>
/// <param name="Cycle">The window's layout (for cycling) before.</param>
/// <param name="Origin">The window's "restore previous" origin before.</param>
public sealed record UndoEntry(nint Window, WindowAction Action, WindowSnapshot Before, CycleTracker.Entry? Cycle, WindowOrigin? Origin);

/// <summary>
/// The actions Loop applied, most recent last, across all windows, so <see cref="WindowAction.Undo"/>
/// can take them back one at a time, including a minimize (whose window no longer has focus).
/// Pure logic: the platform layer records each applied action and applies the plan for undoing it.
/// </summary>
public sealed class UndoHistory
{
    public const int DefaultCapacity = 50;

    private readonly LinkedList<UndoEntry> _entries = new();
    private readonly int _capacity;

    public UndoHistory(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _entries.Count;

    /// <summary>Records an applied action; the oldest is dropped once the history is full.</summary>
    public void Push(UndoEntry entry)
    {
        if (entry.Action is WindowAction.Undo) return; // undoing is not itself undoable
        _entries.AddLast(entry);
        while (_entries.Count > _capacity) _entries.RemoveFirst();
    }

    /// <summary>Takes the most recent action whose window still exists (actions on closed windows are dropped).</summary>
    public bool TryPop(Func<nint, bool> windowExists, out UndoEntry entry)
    {
        while (_entries.Last is { } last)
        {
            _entries.RemoveLast();
            if (windowExists(last.Value.Window))
            {
                entry = last.Value;
                return true;
            }
        }
        entry = null!;
        return false;
    }

    public void Clear() => _entries.Clear();

    /// <summary>
    /// Plans putting a window back the way <paramref name="entry"/> found it.
    /// </summary>
    /// <param name="entry">The action to undo.</param>
    /// <param name="displays">All displays, in display order (at least one).</param>
    /// <param name="minimizedNow">Whether the window is minimized now.</param>
    public static PlannedAction Plan(UndoEntry entry, IReadOnlyList<DisplayArea> displays, bool minimizedNow)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        var before = entry.Before;
        var plannedFrom = new PlannedAction(WindowAction.Undo, PlanKind.Move, before.Visible, displays[0], default)
        {
            PreviousCycle = entry.Cycle,
            PreviousOrigin = entry.Origin,
        };

        if (before.Minimized)
            return plannedFrom with { Kind = PlanKind.Minimize };

        // A minimize is undone like clicking the window on the taskbar: it comes back as it was
        // (maximized or not), where it was.
        var bounds = before.Maximized && before.Restored is { } r ? r : before.Visible;
        if (minimizedNow && entry.Action == WindowAction.Minimize)
        {
            var at = Math.Max(0, DisplayOrder.IndexOf(displays, before.Visible));
            return plannedFrom with { Kind = PlanKind.Restore, Target = before.Maximized ? displays[at].WorkArea : bounds, Display = displays[at] };
        }

        // Its display may be gone (unplugged): then bring it onto the nearest one.
        var index = Math.Max(0, DisplayOrder.IndexOf(displays, before.Maximized ? before.Visible : bounds));
        var display = displays[index];
        if (display.Bounds.Intersect(bounds).IsEmpty) bounds = LayoutEngine.ClampInto(bounds, display.WorkArea);

        if (before.Maximized)
            return plannedFrom with
            {
                Kind = PlanKind.Maximize,
                Target = display.WorkArea,
                Display = display,
                Plan = new LayoutPlan(display.WorkArea, WindowAction.Maximize, 0),
                NormalBounds = bounds,
            };

        return plannedFrom with { Target = bounds, Display = display, Plan = new LayoutPlan(bounds, null, 0) };
    }
}
