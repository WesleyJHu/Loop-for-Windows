using Loop.Core.Actions;
using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class CycleTrackerTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1040);
    private const nint Hwnd = 0x1234;
    private const nint OtherHwnd = 0x5678;

    /// <summary>Simulates applying an action exactly: computes its rect and records it as where the window landed.</summary>
    private static (int Index, Rect Rect) Apply(CycleTracker t, WindowAction action, Rect current, nint hwnd = Hwnd)
    {
        var index = t.NextIndex(hwnd, action, current);
        var rect = LayoutEngine.Compute(action, Work, current, t.FractionAt(index));
        t.Record(hwnd, action, index, rect);
        return (index, rect);
    }

    [Fact]
    public void RepeatedPresses_CycleHalfThirdTwoThirds_ThenWrap()
    {
        var t = new CycleTracker();
        var window = new Rect(300, 200, 800, 600);
        var widths = new List<int>();

        for (var i = 0; i < 4; i++)
        {
            (_, window) = Apply(t, WindowAction.LeftHalf, window);
            widths.Add(window.Width);
        }

        Assert.Equal(new[] { 960, 640, 1280, 960 }, widths);
    }

    [Fact]
    public void FirstPress_StartsAtHalf()
    {
        var t = new CycleTracker();
        Assert.Equal(0, t.NextIndex(Hwnd, WindowAction.RightHalf, new Rect(1, 2, 3, 4)));
    }

    [Fact]
    public void DifferentAction_RestartsCycle()
    {
        var t = new CycleTracker();
        var (_, r) = Apply(t, WindowAction.LeftHalf, new Rect(300, 200, 800, 600));
        (_, r) = Apply(t, WindowAction.LeftHalf, r); // now at 1/3

        Assert.Equal(0, t.NextIndex(Hwnd, WindowAction.RightHalf, r));
    }

    [Fact]
    public void WindowMovedByUser_RestartsCycle()
    {
        var t = new CycleTracker();
        var (_, r) = Apply(t, WindowAction.LeftHalf, new Rect(300, 200, 800, 600));

        var dragged = r with { Left = r.Left + 25 };
        Assert.Equal(0, t.NextIndex(Hwnd, WindowAction.LeftHalf, dragged));
    }

    [Fact]
    public void CyclesAreTrackedPerWindow()
    {
        var t = new CycleTracker();
        var (_, a) = Apply(t, WindowAction.LeftHalf, new Rect(0, 0, 100, 100), Hwnd);
        var (_, b) = Apply(t, WindowAction.LeftHalf, new Rect(0, 0, 100, 100), OtherHwnd);

        Assert.Equal(1, t.NextIndex(Hwnd, WindowAction.LeftHalf, a));
        Assert.Equal(1, t.NextIndex(OtherHwnd, WindowAction.LeftHalf, b));
    }

    [Theory]
    [InlineData(WindowAction.Maximize)]
    [InlineData(WindowAction.Center)]
    public void NonCyclingActions_AlwaysUseFirstSize(WindowAction action)
    {
        var t = new CycleTracker();
        var (_, r) = Apply(t, action, new Rect(300, 200, 800, 600));
        Assert.Equal(0, t.NextIndex(Hwnd, action, r));
    }

    [Theory]
    [InlineData(WindowAction.TopLeft)]
    [InlineData(WindowAction.BottomRight)]
    [InlineData(WindowAction.TopHalf)]
    [InlineData(WindowAction.CenterQuarter)]
    public void CornersHalvesAndCenterQuarter_Cycle(WindowAction action)
    {
        var t = new CycleTracker();
        var (_, r) = Apply(t, action, new Rect(300, 200, 800, 600));
        Assert.Equal(1, t.NextIndex(Hwnd, action, r));
    }

    [Fact]
    public void AppAdjustedSize_StillCycles()
    {
        // Some apps snap to a grid or enforce a minimum size; the recorded rect is where it really
        // landed, so the next press still recognises the window as being in Loop's layout.
        var t = new CycleTracker();
        var landed = new Rect(0, 0, 955, 1036);
        t.Record(Hwnd, WindowAction.LeftHalf, 0, landed);
        Assert.Equal(1, t.NextIndex(Hwnd, WindowAction.LeftHalf, landed));
    }

    [Fact]
    public void Current_OnlyWhileWindowIsUntouched()
    {
        var t = new CycleTracker();
        var r = new Rect(0, 0, 640, 1040);
        t.Record(Hwnd, WindowAction.LeftHalf, 1, r);

        var entry = t.Current(Hwnd, r);
        Assert.NotNull(entry);
        Assert.Equal((WindowAction.LeftHalf, 1), (entry.Value.Action, entry.Value.Index));
        Assert.Null(t.Current(Hwnd, r with { Width = 700 }));
        Assert.Null(t.Current(OtherHwnd, r));
    }

    [Fact]
    public void NextIndex_FromExplicitPreviousLayout()
    {
        var t = new CycleTracker();
        var previous = new CycleTracker.Entry(WindowAction.TopLeft, 2, default);
        Assert.Equal(0, t.NextIndex(previous, WindowAction.TopLeft)); // 2 -> wraps to 0
        Assert.Equal(0, t.NextIndex(previous, WindowAction.TopRight));
        Assert.Equal(0, t.NextIndex(null, WindowAction.TopLeft));
    }

    [Fact]
    public void Forget_ClearsWindow()
    {
        var t = new CycleTracker();
        var r = new Rect(0, 0, 960, 1040);
        t.Record(Hwnd, WindowAction.LeftHalf, 0, r);
        t.Forget(Hwnd);
        Assert.Equal(0, t.NextIndex(Hwnd, WindowAction.LeftHalf, r));
    }

    [Fact]
    public void CustomFractions_AreUsed()
    {
        var t = new CycleTracker(new[] { 0.25, 0.75 });
        Assert.Equal(0.25, t.FractionAt(0));
        Assert.Equal(0.75, t.FractionAt(1));
        Assert.Equal(0.25, t.FractionAt(2));
    }

    [Theory]
    [InlineData(new double[0])]
    [InlineData(new[] { 0.5, 0.0 })]
    [InlineData(new[] { 1.5 })]
    public void InvalidFractions_Throw(double[] fractions) =>
        Assert.Throws<ArgumentException>(() => new CycleTracker(fractions));

    [Fact]
    public void ManyWindows_DoNotGrowWithoutBound()
    {
        var t = new CycleTracker();
        for (nint h = 1; h <= 1000; h++) t.Record(h, WindowAction.LeftHalf, 0, new Rect(0, 0, 1, 1));
        // The most recent window is still tracked.
        Assert.NotNull(t.Current(1000, new Rect(0, 0, 1, 1)));
    }
}
