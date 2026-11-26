using Loop.Core.Actions;
using Loop.Core.Geometry;
using Loop.Core.Input;
using Loop.Core.Windows;
using VK = Loop.Core.Input.VirtualKeys;

namespace Loop.Core.Tests;

public class GapTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);
    private static readonly Rect Window = new(300, 200, 800, 600);
    private static Rect Compute(WindowAction a, int gap, double f = 0.5) => LayoutEngine.Compute(a, Work, Window, f, gap);

    [Fact]
    public void Maximize_InsetsAllEdges() =>
        Assert.Equal(new Rect(10, 10, 1900, 1020), Compute(WindowAction.Maximize, 10));

    [Fact]
    public void Halves_AreOneGapApart()
    {
        var left = Compute(WindowAction.LeftHalf, 10);
        var right = Compute(WindowAction.RightHalf, 10);
        Assert.Equal(new Rect(10, 10, 945, 1020), left);
        Assert.Equal(10, right.Left - left.Right);
        Assert.Equal(10, Work.Right - right.Right);
    }

    [Fact]
    public void OddGap_StillExact()
    {
        var left = Compute(WindowAction.LeftHalf, 7);
        var right = Compute(WindowAction.RightHalf, 7);
        Assert.Equal(7, right.Left - left.Right);
        var top = Compute(WindowAction.TopLeft, 7);
        var bottom = Compute(WindowAction.BottomLeft, 7);
        Assert.Equal(7, bottom.Top - top.Bottom);
    }

    [Fact]
    public void Thirds_AreOneGapApart()
    {
        var third = Compute(WindowAction.LeftHalf, 8, 1.0 / 3);
        var twoThirds = Compute(WindowAction.RightHalf, 8, 2.0 / 3);
        Assert.Equal(8, twoThirds.Left - third.Right);
    }

    [Fact]
    public void Corners_TileWithGaps()
    {
        var tl = Compute(WindowAction.TopLeft, 10);
        var br = Compute(WindowAction.BottomRight, 10);
        Assert.Equal((10, 10), (tl.Left, tl.Top));
        Assert.Equal((Work.Right - 10, Work.Bottom - 10), (br.Right, br.Bottom));
        Assert.Equal(10, br.Left - tl.Right);
        Assert.Equal(10, br.Top - tl.Bottom);
    }

    [Fact]
    public void Center_IgnoresGap() =>
        Assert.Equal(LayoutEngine.Compute(WindowAction.Center, Work, Window), Compute(WindowAction.Center, 20));

    [Fact]
    public void ZeroGap_IsUnchanged() =>
        Assert.Equal(LayoutEngine.Compute(WindowAction.TopRight, Work, Window), Compute(WindowAction.TopRight, 0));

    [Fact]
    public void HugeGap_OnTinyArea_FallsBackToNoGap()
    {
        var tiny = new Rect(0, 0, 10, 10);
        Assert.Equal(tiny, LayoutEngine.Compute(WindowAction.Maximize, tiny, Window, gap: 50));
    }

    [Fact]
    public void Session_AppliesGap_AndDisplayMovesKeepIt()
    {
        var session = new LayoutSession(gap: 10);
        var plan = session.PlanLayout(1, WindowAction.LeftHalf, true, Window, Work);
        Assert.Equal(new Rect(10, 10, 945, 1020), plan.Target);
        session.Landed(1, plan, plan.Target);

        var other = new Rect(1920, 0, 1920, 1040);
        Assert.Equal(new Rect(1930, 10, 945, 1020), session.PlanDisplayMove(1, plan.Target, Work, other).Target);
    }
}

public class EscapeCancelTests
{
    private static TriggerController Held()
    {
        var c = new TriggerController(ModifierKeys.Ctrl | ModifierKeys.Win | ModifierKeys.Alt, KeyBindings.Default);
        c.Process(new KeyEvent(VK.LControl, true));
        c.Process(new KeyEvent(VK.LWin, true));
        c.Process(new KeyEvent(VK.LMenu, true));
        return c;
    }

    [Fact]
    public void EscapeWithTrigger_CancelsAndIsSwallowed()
    {
        var c = Held();
        var d = c.Process(new KeyEvent(VK.Escape, true));
        Assert.True(d.Cancel);
        Assert.True(d.Swallow);
        Assert.Null(d.Action);
        Assert.True(c.Process(new KeyEvent(VK.Escape, false)).Swallow);
    }

    [Fact]
    public void EscapeWithoutTrigger_PassesThrough()
    {
        var c = new TriggerController(ModifierKeys.Ctrl, KeyBindings.Default);
        Assert.Equal(KeyDecision.PassThrough, c.Process(new KeyEvent(VK.Escape, true)));
    }

    [Fact]
    public void Escape_ClearsHeldDirections()
    {
        var c = Held();
        c.Process(new KeyEvent(VK.Left, true));
        c.Process(new KeyEvent(VK.Escape, true));
        var d = c.Process(new KeyEvent(VK.Up, true));
        Assert.Equal(WindowAction.TopHalf, d.Action);
        Assert.True(d.NewGesture);
    }

    [Fact]
    public void Debouncer_Cancel_DropsPending()
    {
        var fired = new List<int>();
        Action? scheduled = null;
        var d = new ChordDebouncer<int>(TimeSpan.FromMilliseconds(60), (_, cb) => { scheduled = cb; return new Noop(); },
            fired.Add, (_, b) => b);
        d.Submit(1, isChordKey: true);
        d.Cancel();
        scheduled!();
        d.Flush();
        Assert.Empty(fired);
    }

    private sealed class Noop : IDisposable { public void Dispose() { } }
}

public class ExclusionAndDisplayLookupTests
{
    [Theory]
    [InlineData("notepad", "notepad")]
    [InlineData("Notepad", "notepad.exe")]
    [InlineData("notepad.exe", "NOTEPAD")]
    public void Excluded_Matches(string process, string entry) =>
        Assert.True(WindowFilter.IsExcluded(process, new[] { entry }));

    [Theory]
    [InlineData("notepad", "notepad++")]
    [InlineData("", "notepad")]
    [InlineData("chrome", " ")]
    public void Excluded_DoesNotMatch(string process, string entry) =>
        Assert.False(WindowFilter.IsExcluded(process, new[] { entry }));

    private static readonly DisplayArea[] Displays =
    {
        new(1, new Rect(-348, 1080, 1920, 1080), new Rect(-348, 1080, 1920, 1080)),
        new(2, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040)),
    };

    [Fact]
    public void IndexOf_LargestOverlap() =>
        Assert.Equal(0, DisplayOrder.IndexOf(Displays, new Rect(0, 900, 800, 600))); // 180 rows up, 420 rows down

    [Fact]
    public void IndexOf_OffScreen_Nearest() =>
        Assert.Equal(1, DisplayOrder.IndexOf(Displays, new Rect(2500, 100, 300, 300)));

    [Fact]
    public void IndexOf_NoDisplays() => Assert.Equal(-1, DisplayOrder.IndexOf(Array.Empty<DisplayArea>(), new Rect(0, 0, 1, 1)));
}
