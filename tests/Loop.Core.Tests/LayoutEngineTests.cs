using Loop.Core.Actions;
using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class LayoutEngineTests
{
    // A 1920x1080 monitor with a 40px taskbar at the bottom.
    private static readonly Rect Work = new(0, 0, 1920, 1040);
    private static readonly Rect Window = new(300, 200, 800, 600);

    private static Rect Compute(WindowAction action, Rect? work = null, Rect? current = null, double fraction = 0.5) =>
        LayoutEngine.Compute(action, work ?? Work, current ?? Window, fraction);

    [Fact]
    public void Maximize_FillsWorkArea() =>
        Assert.Equal(Work, Compute(WindowAction.Maximize));

    [Theory]
    [InlineData(WindowAction.LeftHalf, 0, 0, 960, 1040)]
    [InlineData(WindowAction.RightHalf, 960, 0, 960, 1040)]
    [InlineData(WindowAction.TopHalf, 0, 0, 1920, 520)]
    [InlineData(WindowAction.BottomHalf, 0, 520, 1920, 520)]
    [InlineData(WindowAction.TopLeft, 0, 0, 960, 520)]
    [InlineData(WindowAction.TopRight, 960, 0, 960, 520)]
    [InlineData(WindowAction.BottomLeft, 0, 520, 960, 520)]
    [InlineData(WindowAction.BottomRight, 960, 520, 960, 520)]
    public void HalvesAndCorners(WindowAction action, int x, int y, int w, int h) =>
        Assert.Equal(new Rect(x, y, w, h), Compute(action));

    [Fact]
    public void Halves_TileExactly_OnOddSizes()
    {
        var odd = new Rect(0, 0, 1921, 1041);
        var left = Compute(WindowAction.LeftHalf, odd);
        var right = Compute(WindowAction.RightHalf, odd);
        var top = Compute(WindowAction.TopHalf, odd);
        var bottom = Compute(WindowAction.BottomHalf, odd);

        Assert.Equal(left.Right, right.Left);
        Assert.Equal(odd.Width, left.Width + right.Width);
        Assert.Equal(top.Bottom, bottom.Top);
        Assert.Equal(odd.Height, top.Height + bottom.Height);
    }

    [Fact]
    public void Corners_TileTheWorkAreaExactly()
    {
        var odd = new Rect(0, 0, 1921, 1041);
        var tl = Compute(WindowAction.TopLeft, odd);
        var tr = Compute(WindowAction.TopRight, odd);
        var bl = Compute(WindowAction.BottomLeft, odd);
        var br = Compute(WindowAction.BottomRight, odd);

        Assert.Equal(tl.Right, tr.Left);
        Assert.Equal(bl.Right, br.Left);
        Assert.Equal(tl.Bottom, bl.Top);
        Assert.Equal(tr.Bottom, br.Top);
        Assert.Equal(odd.Right, br.Right);
        Assert.Equal(odd.Bottom, br.Bottom);
        Assert.Equal((long)odd.Width * odd.Height,
            (long)tl.Width * tl.Height + (long)tr.Width * tr.Height + (long)bl.Width * bl.Height + (long)br.Width * br.Height);
    }

    [Fact]
    public void Layout_IsRelativeToWorkAreaOrigin()
    {
        // A secondary monitor left of the primary, with a taskbar on top (negative coordinates).
        var secondary = new Rect(-2560, 48, 2560, 1392);
        Assert.Equal(new Rect(-1280, 48, 1280, 1392), Compute(WindowAction.RightHalf, secondary));
        Assert.Equal(new Rect(-2560, 744, 1280, 696), Compute(WindowAction.BottomLeft, secondary));
    }

    [Fact]
    public void MaximizeWidth_KeepsVerticalPositionAndHeight() =>
        Assert.Equal(new Rect(0, 200, 1920, 600), Compute(WindowAction.MaximizeWidth));

    [Fact]
    public void MaximizeHeight_KeepsHorizontalPositionAndWidth() =>
        Assert.Equal(new Rect(300, 0, 800, 1040), Compute(WindowAction.MaximizeHeight));

    [Fact]
    public void MaximizeWidth_PullsOffscreenWindowBackInside()
    {
        var hanging = new Rect(100, 900, 800, 600); // bottom edge at 1500, beyond the 1040 work area
        Assert.Equal(new Rect(0, 440, 1920, 600), Compute(WindowAction.MaximizeWidth, current: hanging));
    }

    [Fact]
    public void MaximizeHeight_ShrinksWindowWiderThanWorkArea()
    {
        var huge = new Rect(-100, 0, 3000, 500);
        Assert.Equal(new Rect(0, 0, 1920, 1040), Compute(WindowAction.MaximizeHeight, current: huge));
    }

    [Fact]
    public void Center_KeepsSize() =>
        Assert.Equal(new Rect(560, 220, 800, 600), Compute(WindowAction.Center));

    [Fact]
    public void Center_RespectsWorkAreaOffset()
    {
        var secondary = new Rect(1920, 100, 1000, 1000);
        Assert.Equal(new Rect(2020, 300, 800, 600), Compute(WindowAction.Center, secondary));
    }

    [Fact]
    public void Center_ShrinksWindowLargerThanWorkArea()
    {
        var huge = new Rect(0, 0, 2500, 1500);
        Assert.Equal(Work, Compute(WindowAction.Center, current: huge));
    }

    [Theory]
    [InlineData(1.0 / 3, 640)]
    [InlineData(2.0 / 3, 1280)]
    public void Fraction_ControlsHalfWidth(double fraction, int expectedWidth)
    {
        Assert.Equal(new Rect(0, 0, expectedWidth, 1040), Compute(WindowAction.LeftHalf, fraction: fraction));
        Assert.Equal(new Rect(1920 - expectedWidth, 0, expectedWidth, 1040), Compute(WindowAction.RightHalf, fraction: fraction));
    }

    [Fact]
    public void ThirdAndTwoThirds_TileExactly()
    {
        var odd = new Rect(0, 0, 1919, 1040);
        var leftThird = Compute(WindowAction.LeftHalf, odd, fraction: 1.0 / 3);
        var rightTwoThirds = Compute(WindowAction.RightHalf, odd, fraction: 2.0 / 3);
        Assert.Equal(leftThird.Right, rightTwoThirds.Left);
        Assert.Equal(odd.Right, rightTwoThirds.Right);
    }

    [Fact]
    public void Minimize_HasNoRectangle() =>
        Assert.Throws<ArgumentException>(() => Compute(WindowAction.Minimize));

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void InvalidFraction_Throws(double fraction) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Compute(WindowAction.LeftHalf, fraction: fraction));

    [Fact]
    public void EveryGeometricAction_StaysInsideWorkArea()
    {
        var offscreen = new Rect(-5000, -5000, 4000, 4000);
        foreach (var action in Enum.GetValues<WindowAction>().Where(a => a.IsGeometric()))
        {
            foreach (var current in new[] { Window, offscreen })
            {
                var r = Compute(action, current: current);
                Assert.True(r.Left >= Work.Left && r.Top >= Work.Top && r.Right <= Work.Right && r.Bottom <= Work.Bottom,
                    $"{action} from {current} produced {r}, outside {Work}");
                Assert.True(r.Width > 0 && r.Height > 0, $"{action} produced an empty rect {r}");
            }
        }
    }
}
