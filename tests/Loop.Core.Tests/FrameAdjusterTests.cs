using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class FrameAdjusterTests
{
    [Fact]
    public void Windows11InvisibleBorders_AreAddedBack()
    {
        // Typical Win11 window: 7px invisible border left/right/bottom, none on top.
        var windowRect = new Rect(93, 100, 814, 607);
        var visible = new Rect(100, 100, 800, 600);
        var target = new Rect(0, 0, 960, 1040);

        var outer = FrameAdjuster.ToWindowRect(target, windowRect, visible);

        Assert.Equal(new Rect(-7, 0, 974, 1047), outer);
    }

    [Fact]
    public void NoInvisibleBorders_LeavesTargetUnchanged()
    {
        var r = new Rect(10, 20, 300, 400);
        var target = new Rect(0, 0, 960, 1040);
        Assert.Equal(target, FrameAdjuster.ToWindowRect(target, r, r));
    }

    [Fact]
    public void VisibleFrameLargerThanWindow_IsIgnored()
    {
        var windowRect = new Rect(100, 100, 800, 600);
        var bogusVisible = new Rect(90, 100, 820, 600);
        var target = new Rect(0, 0, 960, 1040);
        Assert.Equal(target, FrameAdjuster.ToWindowRect(target, windowRect, bogusVisible));
    }

    [Fact]
    public void Adjustment_RoundTrips()
    {
        // Placing the outer rect returned should put the visible frame exactly on the target.
        var windowRect = new Rect(93, 100, 814, 607);
        var visible = new Rect(100, 100, 800, 600);
        var target = new Rect(960, 0, 960, 520);

        var outer = FrameAdjuster.ToWindowRect(target, windowRect, visible);
        var resultingVisible = Rect.FromEdges(
            outer.Left + (visible.Left - windowRect.Left),
            outer.Top + (visible.Top - windowRect.Top),
            outer.Right - (windowRect.Right - visible.Right),
            outer.Bottom - (windowRect.Bottom - visible.Bottom));

        Assert.Equal(target, resultingVisible);
    }
}
