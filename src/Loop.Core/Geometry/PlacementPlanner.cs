namespace Loop.Core.Geometry;

/// <summary>One SetWindowPos call: a full move+resize, or a resize that keeps the top-left corner.</summary>
public readonly record struct PlacementStep(Rect Rect, bool SizeOnly);

/// <summary>
/// Plans the SetWindowPos calls that put a window's outer rect in place.
///
/// The outer rect includes invisible resize borders, so a window snapped to a screen edge pokes a
/// few pixels past it. If a monitor with a different DPI sits there, Windows' DPI virtualization
/// remaps the position of DPI-unaware windows using that monitor's scale, and they land far off
/// target. A size-only resize is not remapped, so: move with the right/bottom edges pulled back
/// inside the destination monitor, then grow to the full size without moving.
///
/// Left/top overhang is kept in the move (it can't be added by a size-only resize). It only causes
/// trouble when a different-DPI monitor sits directly left of or above the destination; the
/// caller's verification pass is the fallback for that layout.
/// </summary>
public static class PlacementPlanner
{
    public static IReadOnlyList<PlacementStep> Plan(Rect outer, Rect monitorBounds)
    {
        var move = Rect.FromEdges(
            outer.Left,
            outer.Top,
            Math.Min(outer.Right, monitorBounds.Right),
            Math.Min(outer.Bottom, monitorBounds.Bottom));

        if (monitorBounds.IsEmpty || move.IsEmpty || move == outer)
            return new[] { new PlacementStep(outer, SizeOnly: false) };

        return new[] { new PlacementStep(move, SizeOnly: false), new PlacementStep(outer, SizeOnly: true) };
    }
}
