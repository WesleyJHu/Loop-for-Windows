namespace Loop.Core.Geometry;

/// <summary>
/// On Windows 10/11, GetWindowRect includes invisible resize borders (about 7px left/right/bottom).
/// SetWindowPos works in that same outer space, so to place the *visible* frame at a target
/// we expand the target by the invisible insets measured from DWMWA_EXTENDED_FRAME_BOUNDS.
/// </summary>
public static class FrameAdjuster
{
    /// <param name="desiredVisible">Where the visible frame should end up.</param>
    /// <param name="windowRect">Current GetWindowRect result.</param>
    /// <param name="visibleFrame">Current DWMWA_EXTENDED_FRAME_BOUNDS result.</param>
    /// <returns>The rectangle to pass to SetWindowPos.</returns>
    public static Rect ToWindowRect(Rect desiredVisible, Rect windowRect, Rect visibleFrame)
    {
        var leftInset = visibleFrame.Left - windowRect.Left;
        var topInset = visibleFrame.Top - windowRect.Top;
        var rightInset = windowRect.Right - visibleFrame.Right;
        var bottomInset = windowRect.Bottom - visibleFrame.Bottom;

        // Ignore nonsensical measurements (e.g. a visible frame larger than the window).
        if (leftInset < 0 || topInset < 0 || rightInset < 0 || bottomInset < 0)
            return desiredVisible;

        return Rect.FromEdges(
            desiredVisible.Left - leftInset,
            desiredVisible.Top - topInset,
            desiredVisible.Right + rightInset,
            desiredVisible.Bottom + bottomInset);
    }
}
