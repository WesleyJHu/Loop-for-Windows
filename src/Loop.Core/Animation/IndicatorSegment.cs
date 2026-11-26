using Loop.Core.Actions;

namespace Loop.Core.Animation;

/// <summary>
/// Which part of the radial direction indicator lights up. The eight directions are listed
/// counter-clockwise from the right, so segment i sits at i * 45 degrees.
/// </summary>
public enum IndicatorSegment
{
    Right,
    TopRight,
    Top,
    TopLeft,
    Left,
    BottomLeft,
    Bottom,
    BottomRight,
    /// <summary>The middle: maximize and the centering actions.</summary>
    Center,
    /// <summary>Nothing to highlight (minimize, display moves).</summary>
    None,
}

public static class Indicator
{
    public static IndicatorSegment SegmentFor(WindowAction action) => action switch
    {
        WindowAction.RightHalf => IndicatorSegment.Right,
        WindowAction.TopRight => IndicatorSegment.TopRight,
        WindowAction.TopHalf => IndicatorSegment.Top,
        WindowAction.TopLeft => IndicatorSegment.TopLeft,
        WindowAction.LeftHalf => IndicatorSegment.Left,
        WindowAction.BottomLeft => IndicatorSegment.BottomLeft,
        WindowAction.BottomHalf => IndicatorSegment.Bottom,
        WindowAction.BottomRight => IndicatorSegment.BottomRight,
        WindowAction.Maximize or WindowAction.Center or WindowAction.CenterQuarter => IndicatorSegment.Center,
        _ => IndicatorSegment.None,
    };

    /// <summary>
    /// What choosing a segment of the mouse's radial menu does: a direction gives that half or
    /// corner, the center maximizes (or restores a maximized window). None for <see cref="IndicatorSegment.None"/>.
    /// </summary>
    public static WindowAction? ActionFor(IndicatorSegment segment) => segment switch
    {
        IndicatorSegment.Right => WindowAction.RightHalf,
        IndicatorSegment.TopRight => WindowAction.TopRight,
        IndicatorSegment.Top => WindowAction.TopHalf,
        IndicatorSegment.TopLeft => WindowAction.TopLeft,
        IndicatorSegment.Left => WindowAction.LeftHalf,
        IndicatorSegment.BottomLeft => WindowAction.BottomLeft,
        IndicatorSegment.Bottom => WindowAction.BottomHalf,
        IndicatorSegment.BottomRight => WindowAction.BottomRight,
        IndicatorSegment.Center => WindowAction.Maximize,
        _ => null,
    };

    /// <summary>The angle of a direction segment's middle, in degrees counter-clockwise from the right.</summary>
    public static double AngleOf(IndicatorSegment segment) =>
        segment <= IndicatorSegment.BottomRight
            ? (int)segment * 45.0
            : throw new ArgumentOutOfRangeException(nameof(segment), segment, "Only the eight directions have an angle.");
}
