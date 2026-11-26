using Loop.Core.Actions;

namespace Loop.Core.Geometry;

/// <summary>A connected display, in physical pixels. <see cref="Id"/> is the platform's monitor handle.</summary>
public readonly record struct DisplayArea(nint Id, Rect Bounds, Rect WorkArea);

/// <summary>Numbering of displays for "next display" and "display N" actions.</summary>
public static class DisplayOrder
{
    /// <summary>
    /// The display a rectangle is on: the one it overlaps most, or else the one whose bounds are
    /// nearest. -1 only when there are no displays.
    /// </summary>
    public static int IndexOf(IReadOnlyList<DisplayArea> displays, Rect rect)
    {
        var best = -1;
        long bestArea = -1;
        for (var i = 0; i < displays.Count; i++)
        {
            var o = displays[i].Bounds.Intersect(rect);
            long area = o.IsEmpty ? 0 : (long)o.Width * o.Height;
            if (area > bestArea) (best, bestArea) = (i, area);
        }
        if (bestArea > 0 || displays.Count == 0) return best;

        // No overlap (window entirely off-screen): pick the display whose bounds are closest.
        long bestDistance = long.MaxValue;
        for (var i = 0; i < displays.Count; i++)
        {
            var d = DistanceSquared(displays[i].Bounds, rect);
            if (d < bestDistance) (best, bestDistance) = (i, d);
        }
        return best;
    }

    private static long DistanceSquared(Rect a, Rect b)
    {
        long dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        long dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return dx * dx + dy * dy;
    }

    /// <summary>Sorts displays left-to-right, then top-to-bottom, by the top-left corner of their bounds.</summary>
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> displays, Func<T, Rect> bounds) =>
        displays.OrderBy(d => bounds(d).Left).ThenBy(d => bounds(d).Top).ToList();

    /// <summary>
    /// The index of the display a display-move action targets, or null when the action does nothing
    /// (a single display, a display number that doesn't exist, or the display the window is already on).
    /// </summary>
    public static int? Resolve(WindowAction action, int currentIndex, int count)
    {
        if (count <= 1 || currentIndex < 0 || currentIndex >= count) return null;

        int target = action switch
        {
            WindowAction.NextDisplay => (currentIndex + 1) % count,
            WindowAction.PreviousDisplay => (currentIndex - 1 + count) % count,
            _ when action.DisplayIndex() is { } n => n,
            _ => -1,
        };

        return target >= 0 && target < count && target != currentIndex ? target : null;
    }
}
