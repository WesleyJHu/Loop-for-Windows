using Loop.Core.Actions;
using Loop.Core.Geometry;

namespace Loop.Core.Animation;

public enum EasingKind
{
    /// <summary>Fast start, gentle stop (cubic). Feels responsive; the default.</summary>
    EaseOut,
    /// <summary>Gentle start and stop (cubic).</summary>
    EaseInOut,
    Linear,
}

public static class Easing
{
    /// <summary>Maps linear progress <paramref name="t"/> in [0, 1] onto the easing curve.</summary>
    public static double Apply(EasingKind kind, double t)
    {
        t = Math.Clamp(double.IsNaN(t) ? 1 : t, 0, 1);
        return kind switch
        {
            EasingKind.Linear => t,
            EasingKind.EaseInOut => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            _ => 1 - Math.Pow(1 - t, 3),
        };
    }
}

/// <summary>Interpolation of rectangles over time.</summary>
public static class RectTween
{
    /// <summary>Eased progress in [0, 1] after <paramref name="elapsed"/>; 1 once the duration has passed.</summary>
    public static double Progress(TimeSpan elapsed, TimeSpan duration, EasingKind easing) =>
        duration <= TimeSpan.Zero ? 1 : Easing.Apply(easing, elapsed.TotalMilliseconds / duration.TotalMilliseconds);

    /// <summary>
    /// The rectangle at <paramref name="progress"/> between two rectangles. Edges are interpolated
    /// (not position and size), so two windows that tile at the end stay tiled throughout.
    /// </summary>
    public static Rect At(Rect from, Rect to, double progress)
    {
        if (progress <= 0) return from;
        if (progress >= 1) return to;
        return Rect.FromEdges(
            Lerp(from.Left, to.Left, progress),
            Lerp(from.Top, to.Top, progress),
            Lerp(from.Right, to.Right, progress),
            Lerp(from.Bottom, to.Bottom, progress));
    }

    private static int Lerp(int a, int b, double p) => (int)Math.Round(a + (b - a) * p, MidpointRounding.AwayFromZero);
}

/// <summary>Animation settings, already resolved from the user's settings.</summary>
public readonly record struct AnimationOptions(bool AnimatePreview, bool AnimateWindows, TimeSpan Duration, EasingKind Easing)
{
    public static AnimationOptions None => new(false, false, TimeSpan.Zero, EasingKind.EaseOut);

    /// <summary>Fades are short and never longer than the movement itself.</summary>
    public TimeSpan FadeDuration => Duration < TimeSpan.FromMilliseconds(100) ? Duration : TimeSpan.FromMilliseconds(100);
}

/// <summary>Decides when animating would help and when it would only get in the way.</summary>
public static class MotionPolicy
{
    /// <summary>Moves shorter than this (on every edge) aren't worth animating.</summary>
    public const int MinDistance = 3;

    /// <summary>Animate the preview overlay?</summary>
    public static bool ShouldAnimatePreview(AnimationOptions options, bool systemAnimationsEnabled) =>
        systemAnimationsEnabled && options.AnimatePreview && options.Duration > TimeSpan.Zero;

    /// <summary>Animate a real window move?</summary>
    /// <param name="options">The user's animation settings.</param>
    /// <param name="systemAnimationsEnabled">Windows' "Animation effects" setting.</param>
    /// <param name="kind">What the plan does. Only plain moves animate: Windows animates maximize,
    /// restore, and minimize itself.</param>
    /// <param name="from">The window's visible bounds now.</param>
    /// <param name="to">Its target.</param>
    /// <param name="fromDisplay">The display it is on.</param>
    /// <param name="toDisplay">The display it is going to.</param>
    /// <param name="windowWasMaximizedOrMinimized">The window first has to be restored, which Windows animates on its own.</param>
    public static bool ShouldAnimateWindow(
        AnimationOptions options, bool systemAnimationsEnabled, PlanKind kind, Rect from, Rect to,
        DisplayArea fromDisplay, DisplayArea toDisplay, bool windowWasMaximizedOrMinimized)
    {
        if (!systemAnimationsEnabled || !options.AnimateWindows || options.Duration <= TimeSpan.Zero) return false;
        if (kind != PlanKind.Move || windowWasMaximizedOrMinimized) return false;
        if (from.IsEmpty || to.IsEmpty) return false;
        // Crossing onto a display with another scale makes Windows rescale the window mid-flight.
        if (fromDisplay.Dpi != toDisplay.Dpi) return false;
        return Math.Max(Math.Max(Math.Abs(from.Left - to.Left), Math.Abs(from.Top - to.Top)),
                        Math.Max(Math.Abs(from.Right - to.Right), Math.Abs(from.Bottom - to.Bottom))) >= MinDistance;
    }

    /// <summary>
    /// Where intermediate frames may reach. A window's invisible borders poking onto a neighbouring
    /// display with another scale make Windows remap DPI-unaware windows, so frames are kept to the
    /// displays the window is moving between.
    /// </summary>
    public static Rect FrameBounds(DisplayArea fromDisplay, DisplayArea toDisplay)
    {
        var a = fromDisplay.Bounds;
        var b = toDisplay.Bounds;
        return Rect.FromEdges(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    }
}

/// <summary>
/// Watches how long each animation frame takes the target app to handle. Apps that repaint
/// slowly can't keep up with per-frame moves and would stutter, so their animation is abandoned
/// (the window jumps to its target instead).
/// </summary>
public sealed class SlowFrameDetector
{
    private readonly TimeSpan _budget;
    private readonly int _strikesAllowed;
    private int _strikes;

    /// <param name="budget">A frame slower than this is a strike (default: two frames at 60 Hz).</param>
    /// <param name="strikesAllowed">Strikes tolerated before giving up (a single hiccup is forgiven).</param>
    public SlowFrameDetector(TimeSpan? budget = null, int strikesAllowed = 1)
    {
        _budget = budget ?? TimeSpan.FromMilliseconds(33);
        _strikesAllowed = strikesAllowed;
    }

    /// <summary>Records one frame's cost. Returns true when the animation should be abandoned.</summary>
    public bool Record(TimeSpan frameCost)
    {
        if (frameCost > _budget * 3) return true; // badly stuck: stop now
        if (frameCost > _budget) _strikes++;
        return _strikes > _strikesAllowed;
    }
}
