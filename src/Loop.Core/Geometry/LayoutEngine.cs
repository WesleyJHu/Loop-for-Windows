using Loop.Core.Actions;

namespace Loop.Core.Geometry;

/// <summary>Computes the target (visible) rectangle for a window action.</summary>
public static class LayoutEngine
{
    /// <param name="action">A geometric action (anything but <see cref="WindowAction.Minimize"/>).</param>
    /// <param name="workArea">The monitor work area (excludes the taskbar).</param>
    /// <param name="current">The window's current visible bounds.</param>
    /// <param name="fraction">Share of the work area a half/corner occupies (0.5 = half). Used for cycling.</param>
    public static Rect Compute(WindowAction action, Rect workArea, Rect current, double fraction = 0.5)
    {
        if (!(fraction > 0 && fraction <= 1))
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Fraction must be in (0, 1].");

        var w = workArea;
        var (leftX, leftW) = Leading(w.Left, w.Width, fraction);
        var (rightX, rightW) = Trailing(w.Left, w.Width, fraction);
        var (topY, topH) = Leading(w.Top, w.Height, fraction);
        var (bottomY, bottomH) = Trailing(w.Top, w.Height, fraction);

        return action switch
        {
            WindowAction.Maximize => w,
            WindowAction.MaximizeWidth => VerticalKept(w, current),
            WindowAction.MaximizeHeight => HorizontalKept(w, current),

            WindowAction.LeftHalf => new Rect(leftX, w.Top, leftW, w.Height),
            WindowAction.RightHalf => new Rect(rightX, w.Top, rightW, w.Height),
            WindowAction.TopHalf => new Rect(w.Left, topY, w.Width, topH),
            WindowAction.BottomHalf => new Rect(w.Left, bottomY, w.Width, bottomH),

            WindowAction.TopLeft => new Rect(leftX, topY, leftW, topH),
            WindowAction.TopRight => new Rect(rightX, topY, rightW, topH),
            WindowAction.BottomLeft => new Rect(leftX, bottomY, leftW, bottomH),
            WindowAction.BottomRight => new Rect(rightX, bottomY, rightW, bottomH),

            WindowAction.Center => CenterIn(w, current),

            _ => throw new ArgumentException($"{action} does not produce a rectangle.", nameof(action)),
        };
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

    private static int Split(int length, double fraction) =>
        (int)Math.Round(length * fraction, MidpointRounding.AwayFromZero);

    private static Rect CenterIn(Rect w, Rect current)
    {
        var width = Math.Min(current.Width, w.Width);
        var height = Math.Min(current.Height, w.Height);
        return new Rect(w.Left + (w.Width - width) / 2, w.Top + (w.Height - height) / 2, width, height);
    }

    private static Rect VerticalKept(Rect w, Rect current)
    {
        var (top, height) = ClampSpan(current.Top, current.Height, w.Top, w.Height);
        return new Rect(w.Left, top, w.Width, height);
    }

    private static Rect HorizontalKept(Rect w, Rect current)
    {
        var (left, width) = ClampSpan(current.Left, current.Width, w.Left, w.Width);
        return new Rect(left, w.Top, width, w.Height);
    }

    /// <summary>Keeps a 1-D span inside [areaStart, areaStart + areaSize).</summary>
    private static (int Position, int Size) ClampSpan(int position, int size, int areaStart, int areaSize)
    {
        size = Math.Clamp(size, 1, areaSize);
        position = Math.Clamp(position, areaStart, areaStart + areaSize - size);
        return (position, size);
    }
}
