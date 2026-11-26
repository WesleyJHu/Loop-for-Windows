using Loop.Core.Actions;
using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class LayoutSessionTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1080);
    private static readonly Rect Below = new(-348, 1080, 1920, 1080); // this machine's second display
    private static readonly Rect Wide = new(1920, 0, 2560, 1440);
    private const nint Hwnd = 0x42;

    private readonly LayoutSession _session = new();
    private Rect _window = new(300, 200, 800, 600);

    /// <summary>Plans a layout action and pretends the window landed exactly on target.</summary>
    private LayoutPlan Press(WindowAction action, bool newGesture = true, Rect? work = null)
    {
        var plan = _session.PlanLayout(Hwnd, action, newGesture, _window, work ?? Primary);
        _window = plan.Target;
        _session.Landed(Hwnd, plan, plan.Target);
        return plan;
    }

    private LayoutPlan MoveTo(Rect from, Rect to)
    {
        var plan = _session.PlanDisplayMove(Hwnd, _window, from, to);
        _window = plan.Target;
        _session.Landed(Hwnd, plan, plan.Target);
        return plan;
    }

    // ---- Gestures ---------------------------------------------------------------------------------

    [Fact]
    public void ChordCenter_UsesSizeFromBeforeTheGesture()
    {
        Press(WindowAction.LeftHalf);                                    // Left...
        var center = Press(WindowAction.Center, newGesture: false);     // ...then Right while Left is held

        Assert.Equal(new Rect(560, 240, 800, 600), center.Target);      // original 800x600, centered
    }

    [Fact]
    public void SeparateCenter_UsesCurrentSize()
    {
        Press(WindowAction.TopLeft);
        var center = Press(WindowAction.Center);

        Assert.Equal(new Rect(480, 270, 960, 540), center.Target);
    }

    [Fact]
    public void StaggeredFourKeyChord_StillCycles()
    {
        // Each press of "all four" arrives key by key, slower than the grace period, so the
        // intermediate actions are applied too. The second press must still cycle.
        void PressAllFour()
        {
            Press(WindowAction.LeftHalf);
            Press(WindowAction.Center, newGesture: false);
            Press(WindowAction.TopHalf, newGesture: false);
            Press(WindowAction.CenterQuarter, newGesture: false);
        }

        PressAllFour();
        Assert.Equal(960, _window.Width);
        PressAllFour();
        Assert.Equal(640, _window.Width);
        PressAllFour();
        Assert.Equal(1280, _window.Width);
    }

    [Fact]
    public void StaggeredCornerChord_StillCycles()
    {
        Press(WindowAction.TopHalf);
        Press(WindowAction.TopLeft, newGesture: false);
        Assert.Equal(960, _window.Width);

        Press(WindowAction.TopHalf);
        Press(WindowAction.TopLeft, newGesture: false);
        Assert.Equal(640, _window.Width);
    }

    [Fact]
    public void RepeatedPresses_Cycle()
    {
        var widths = Enumerable.Range(0, 4).Select(_ => Press(WindowAction.RightHalf).Target.Width).ToList();
        Assert.Equal(new[] { 960, 640, 1280, 960 }, widths);
    }

    [Fact]
    public void CycleIndexIsReported()
    {
        Assert.Equal(0, Press(WindowAction.LeftHalf).Index);
        Assert.Equal(1, Press(WindowAction.LeftHalf).Index);
    }

    [Fact]
    public void AppAdjustedLanding_StillCycles()
    {
        var plan = _session.PlanLayout(Hwnd, WindowAction.LeftHalf, true, _window, Primary);
        var landed = plan.Target with { Width = plan.Target.Width - 3 }; // e.g. a terminal snapping to its grid
        _session.Landed(Hwnd, plan, landed);

        Assert.Equal(1, _session.PlanLayout(Hwnd, WindowAction.LeftHalf, true, landed, Primary).Index);
    }

    [Fact]
    public void Forget_ResetsCycle()
    {
        Press(WindowAction.LeftHalf);
        _session.Forget(Hwnd);
        Assert.Equal(0, Press(WindowAction.LeftHalf).Index);
    }

    // ---- Display moves ----------------------------------------------------------------------------

    [Fact]
    public void DisplayMove_KeepsLoopLayout()
    {
        Press(WindowAction.LeftHalf);
        var plan = MoveTo(Primary, Below);

        Assert.Equal(new Rect(-348, 1080, 960, 1080), plan.Target);
        Assert.Equal(WindowAction.LeftHalf, plan.Layout);
    }

    [Fact]
    public void DisplayMove_KeepsCycleSize_AndCyclingContinues()
    {
        Press(WindowAction.LeftHalf);
        Press(WindowAction.LeftHalf);                                   // 1/3
        Assert.Equal(new Rect(1920, 0, 853, 1440), MoveTo(Primary, Wide).Target);

        Assert.Equal(1707, Press(WindowAction.LeftHalf, work: Wide).Target.Width); // 2/3 of 2560
    }

    [Fact]
    public void DisplayMove_FreeWindow_KeepsRelativePlacement()
    {
        var plan = MoveTo(Primary, Below);
        Assert.Equal(new Rect(-48, 1280, 800, 600), plan.Target);
        Assert.Null(plan.Layout);

        Assert.Equal(new Rect(300, 200, 800, 600), MoveTo(Below, Primary).Target); // round trip
    }

    [Fact]
    public void DisplayMove_AfterUserMovedWindow_TreatsItAsFree()
    {
        Press(WindowAction.LeftHalf);
        _window = _window with { Left = 50 };                           // user dragged it
        Assert.Null(MoveTo(Primary, Below).Layout);
    }

    [Fact]
    public void DisplayMove_EndsGesture()
    {
        Press(WindowAction.LeftHalf);
        MoveTo(Primary, Below);
        // A "refining" Center right after a display move must not use the pre-move origin.
        var center = Press(WindowAction.Center, newGesture: false, work: Below);
        Assert.Equal(new Rect(-348 + 480, 1080, 960, 1080), center.Target);
    }
}
