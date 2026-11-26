using Loop.Core.Geometry;

namespace Loop.Core.Actions;

/// <summary>
/// Remembers, per window, the last layout Loop applied, so that repeating an action cycles through
/// sizes (1/2, then 1/3, then 2/3, then back to 1/2). The cycle resets when the user moves or resizes
/// the window themselves, which is detected by the window no longer being where Loop put it.
/// </summary>
public sealed class CycleTracker
{
    public static IReadOnlyList<double> DefaultFractions { get; } = new[] { 1.0 / 2, 1.0 / 3, 2.0 / 3 };

    private const int MaxTrackedWindows = 256;
    private readonly Dictionary<nint, Entry> _entries = new();

    public CycleTracker(IReadOnlyList<double>? fractions = null)
    {
        Fractions = fractions ?? DefaultFractions;
        if (Fractions.Count == 0 || Fractions.Any(f => !(f > 0 && f <= 1)))
            throw new ArgumentException("Cycle fractions must be non-empty and in (0, 1].", nameof(fractions));
    }

    public IReadOnlyList<double> Fractions { get; }

    /// <summary>The layout Loop last applied to a window.</summary>
    public readonly record struct Entry(WindowAction Action, int Index, Rect Applied);

    /// <summary>The window's Loop layout, if it is still exactly where Loop put it.</summary>
    public Entry? Current(nint window, Rect current) =>
        _entries.TryGetValue(window, out var entry) && entry.Applied == current ? entry : null;

    /// <summary>The stored entry for a window, wherever the window is now (for save/restore).</summary>
    public Entry? Peek(nint window) => _entries.TryGetValue(window, out var entry) ? entry : null;

    /// <summary>Puts back an entry returned by <see cref="Peek"/> (null removes it).</summary>
    public void Restore(nint window, Entry? entry)
    {
        if (entry is { } e) _entries[window] = e;
        else _entries.Remove(window);
    }

    /// <summary>
    /// The cycle index for applying <paramref name="action"/> to a window whose layout is
    /// <paramref name="previous"/>: the next size if it is the same cycling action, else the first.
    /// </summary>
    public int NextIndex(Entry? previous, WindowAction action) =>
        action.Cycles() && previous is { } p && p.Action == action ? (p.Index + 1) % Fractions.Count : 0;

    /// <summary>Shorthand for <c>NextIndex(Current(window, current), action)</c>.</summary>
    public int NextIndex(nint window, WindowAction action, Rect current) =>
        NextIndex(Current(window, current), action);

    public double FractionAt(int index) => Fractions[index % Fractions.Count];

    /// <summary>Records where the window actually ended up after applying a layout.</summary>
    public void Record(nint window, WindowAction action, int index, Rect applied)
    {
        if (_entries.Count >= MaxTrackedWindows && !_entries.ContainsKey(window))
            _entries.Clear(); // closed windows are never removed otherwise; this bounds memory
        _entries[window] = new Entry(action, index, applied);
    }

    public void Forget(nint window) => _entries.Remove(window);
}
