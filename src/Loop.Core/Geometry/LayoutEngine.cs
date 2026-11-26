using Loop.Core.Actions;

namespace Loop.Core.Geometry;

/// <summary>Computes target (visible) rectangles for window actions.</summary>
public static class LayoutEngine
{
    /// <param name="action">A layout action (see <see cref="WindowActionExtensions.IsLayout"/>).</param>
    /// <param name="workArea">The monitor work area (excludes the taskbar).</param>
    /// <param name="current">The window's visible bounds (used by Center to keep the size).</param>
    /// <param name="fraction">
    /// The cycling size. Halves use it along their axis (left/right: width, top/bottom: height).
    /// Corners and CenterQuarter use it for the width and keep half the height.
    /// </param>
    /// <param name="gap">
    /// Pixels between tiled windows and between windows and the work-area edges. Edges on the work
    /// area's border are inset by the full gap; edges inside it are inset by half, so two tiles that
    /// meet are exactly one gap apart. Center (which keeps the window's size) ignores it.
    /// </param>
    public static Rect Compute(WindowAction action, Rect workArea, Rect current, double fraction = 0.5, int gap = 0)
    {
        var rect = ComputeTile(action, workArea, current, fraction);
        return gap > 0 && action != WindowAction.Center ? ApplyGap(rect, workArea, gap) : rect;
    }

    private static Rect ApplyGap(Rect r, Rect w, int gap)
    {
        var half = gap / 2;     // trailing edges inside the area give up half...
        var rest = gap - half;  // ...leading edges the rest, so odd gaps still add up exactly
        var left = r.Left == w.Left ? r.Left + gap : r.Left + rest;
        var top = r.Top == w.Top ? r.Top + gap : r.Top + rest;
        var right = r.Right == w.Right ? r.Right - gap : r.Right - half;
        var bottom = r.Bottom == w.Bottom ? r.Bottom - gap : r.Bottom - half;
        return right - left >= 1 && bottom - top >= 1 ? Rect.FromEdges(left, top, right, bottom) : r;
    }

    private static Rect ComputeTile(WindowAction action, Rect workArea, Rect current, double fraction)
    {
        if (!(fraction > 0 && fraction <= 1))
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Fraction must be in (0, 1].");

        var w = workArea;
        var (leftX, leftW) = Leading(w.Left, w.Width, fraction);
        var (rightX, rightW) = Trailing(w.Left, w.Width, fraction);
        var (topY, topH) = Leading(w.Top, w.Height, fraction);
        var (bottomY, bottomH) = Trailing(w.Top, w.Height, fraction);

        // Corners always take half the height; only their width cycles.
        var (cornerTopY, cornerTopH) = Leading(w.Top, w.Height, 0.5);
        var (cornerBottomY, cornerBottomH) = Trailing(w.Top, w.Height, 0.5);

        return action switch
        {
            WindowAction.Maximize => w,

            WindowAction.LeftHalf => new Rect(leftX, w.Top, leftW, w.Height),
            WindowAction.RightHalf => new Rect(rightX, w.Top, rightW, w.Height),
            WindowAction.TopHalf => new Rect(w.Left, topY, w.Width, topH),
            WindowAction.BottomHalf => new Rect(w.Left, bottomY, w.Width, bottomH),

            WindowAction.TopLeft => new Rect(leftX, cornerTopY, leftW, cornerTopH),
            WindowAction.TopRight => new Rect(rightX, cornerTopY, rightW, cornerTopH),
            WindowAction.BottomLeft => new Rect(leftX, cornerBottomY, leftW, cornerBottomH),
            WindowAction.BottomRight => new Rect(rightX, cornerBottomY, rightW, cornerBottomH),

            WindowAction.Center => CenterIn(w, current.Width, current.Height),
            WindowAction.CenterQuarter => CenterIn(w, leftW, cornerTopH),

            _ => throw new ArgumentException($"{action} is not a layout action.", nameof(action)),
        };
    }

    /// <summary>
    /// Maps a free-floating window from one work area to another, keeping its relative position
    /// and size (a window covering the left third of a 1080p screen covers the left third of a 4K one).
    /// The result is kept inside the destination.
    /// </summary>
    public static Rect Remap(Rect window, Rect from, Rect to)
    {
        if (from.IsEmpty || to.IsEmpty) return window;

        var sx = (double)to.Width / from.Width;
        var sy = (double)to.Height / from.Height;
        var width = Math.Clamp(Round(window.Width * sx), 1, to.Width);
        var height = Math.Clamp(Round(window.Height * sy), 1, to.Height);
        var left = Math.Clamp(to.Left + Round((window.Left - from.Left) * sx), to.Left, to.Right - width);
        var top = Math.Clamp(to.Top + Round((window.Top - from.Top) * sy), to.Top, to.Bottom - height);
        return new Rect(left, top, width, height);
    }

    /// <summary>The leading (left/top) segment covering <paramref name="fraction"/> of the axis.</summary>
    private static (int Position, int Size) Leading(int start, int length, double fraction) =>
        (start, Split(length, fraction));

    /// <summary>
    /// The trailing (right/bottom) segment. It starts at the split point of the complementary
    /// fraction, so a leading f and a trailing (1 - f) segment tile the axis with no gap or overlap
    /// even when the length is odd.
    /// </summary>
    private static (int Position, int Size) Trailing(int start, int length, double fraction)
    {
        var offset = Split(length, 1 - fraction);
        return (start + offset, length - offset);
    }

    private static int Split(int length, double fraction) => Round(length * fraction);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static Rect CenterIn(Rect w, int width, int height)
    {
        width = Math.Clamp(width, 1, w.Width);
        height = Math.Clamp(height, 1, w.Height);
        return new Rect(w.Left + (w.Width - width) / 2, w.Top + (w.Height - height) / 2, width, height);
    }
}
