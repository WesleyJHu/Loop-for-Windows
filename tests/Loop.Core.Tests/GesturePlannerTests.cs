using Loop.Core.Actions;
using Loop.Core.Geometry;

namespace Loop.Core.Tests;

public class GesturePlannerTests
{
    private static readonly DisplayArea Lower = new(2, new Rect(-348, 1080, 1920, 1080), new Rect(-348, 1080, 1920, 1080));
    private static readonly DisplayArea Primary = new(1, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040));
    private static readonly DisplayArea[] Displays = { Lower, Primary }; // display order: lower one is leftmost
    private const nint Hwnd = 7;
    private static readonly Rect Floating = new(300, 200, 800, 600);

    private readonly LayoutSession _session = new();
    private GesturePlanner NewPlanner() => new(_session);

    private PlannedAction? Activation(params WindowAction[] presses) => Activation(Floating, presses);

    /// <summary>One trigger hold: Begin, a press per action (each a new gesture), Finish.</summary>
    private PlannedAction? Activation(Rect window, params WindowAction[] presses)
    {
        var p = NewPlanner();
        p.Begin(Hwnd, window, maximized: false, Displays);
        foreach (var a in presses) p.Step(a, newGesture: true);
        var result = p.Finish();
        if (result is { Minimize: false } r) p.ReportLanded(Hwnd, r, r.Target);
        return result;
    }

    [Fact]
    public void Begin_PicksTheWindowsDisplay()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        Assert.Equal(Primary, p.Step(WindowAction.LeftHalf, true)!.Value.Display);
    }

    [Fact]
    public void Preview_DoesNotNeedTheWindowToMove_ToCycle()
    {
        // Holding the trigger and pressing Left three times previews 1/2, 1/3, 2/3.
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        var widths = new[] { 0, 1, 2 }.Select(_ => p.Step(WindowAction.LeftHalf, newGesture: true)!.Value.Target.Width).ToList();
        Assert.Equal(new[] { 960, 640, 1280 }, widths);
    }

    [Fact]
    public void Finish_ReturnsTheLastStep()
    {
        var result = Activation(WindowAction.LeftHalf, WindowAction.LeftHalf);
        Assert.Equal(new Rect(0, 0, 640, 1040), result!.Value.Target);
    }

    [Fact]
    public void CyclingContinuesAcrossActivations()
    {
        var first = Activation(WindowAction.LeftHalf)!.Value.Target;     // 1/2 applied
        var second = Activation(first, WindowAction.LeftHalf)!.Value.Target; // next activation: 1/3
        Assert.Equal(640, second.Width);
    }

    [Fact]
    public void Cancel_RestoresCycleState()
    {
        var applied = Activation(WindowAction.LeftHalf)!.Value.Target;   // window is at 1/2

        var p = NewPlanner();
        p.Begin(Hwnd, applied, false, Displays);
        p.Step(WindowAction.LeftHalf, true);                              // preview 1/3...
        p.Step(WindowAction.LeftHalf, true);                              // ...2/3
        p.Cancel();                                                       // Esc

        // The window never moved, so the next press continues from 1/2 as if nothing happened.
        Assert.Equal(640, Activation(applied, WindowAction.LeftHalf)!.Value.Target.Width);
    }

    [Fact]
    public void Finish_WithoutSteps_ReturnsNothing()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        Assert.Null(p.Finish());
        Assert.False(p.IsActive);
    }

    [Fact]
    public void DisplayMoveThenLayout_AppliesOnTheNewDisplay()
    {
        // ] then Left while holding the trigger: left half of the other display.
        var result = Activation(WindowAction.PreviousDisplay, WindowAction.LeftHalf);
        Assert.Equal(new Rect(-348, 1080, 960, 1080), result!.Value.Target);
        Assert.Equal(Lower, result.Value.Display);
    }

    [Fact]
    public void LayoutThenDisplayMove_KeepsLayout()
    {
        var result = Activation(WindowAction.TopRight, WindowAction.NextDisplay);
        Assert.Equal(Lower, result!.Value.Display);
        Assert.Equal(new Rect(612, 1080, 960, 540), result.Value.Target);
    }

    [Fact]
    public void DisplayMoveToSameDisplay_KeepsPreviousPending()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        var left = p.Step(WindowAction.LeftHalf, true);
        Assert.Equal(left, p.Step(WindowAction.Display2, true)); // already on display 2
    }

    // ---- Maximize behaves like the window's maximize/restore button ------------------------------

    [Fact]
    public void Maximize_OnNormalWindow_IsARealMaximize()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        var r = p.Step(WindowAction.Maximize, true)!.Value;

        Assert.Equal(PlanKind.Maximize, r.Kind);
        Assert.Equal(Primary.WorkArea, r.Target); // what the preview shows
        Assert.Equal(Primary, r.Display);
    }

    [Fact]
    public void Maximize_OnMaximizedWindow_Restores()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Primary.WorkArea, maximized: true, Displays, restored: Floating);
        var r = p.Step(WindowAction.Maximize, true)!.Value;

        Assert.Equal(PlanKind.Restore, r.Kind);
        Assert.Equal(Floating, r.Target);
    }

    [Fact]
    public void Maximize_WithoutToggling_KeepsAMaximizedWindowMaximized()
    {
        // The radial menu's center on a window that's already maximized.
        var p = NewPlanner();
        p.Begin(Hwnd, Primary.WorkArea, maximized: true, Displays, restored: Floating);
        var r = p.Step(WindowAction.Maximize, true, toggleMaximize: false)!.Value;

        Assert.Equal(PlanKind.Maximize, r.Kind);
        Assert.Equal(Primary.WorkArea, r.Target);
        Assert.Equal(Primary, r.Display);
    }

    [Fact]
    public void Maximize_WithoutToggling_StillMaximizesANormalWindow()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        var r = p.Step(WindowAction.Maximize, true, toggleMaximize: false)!.Value;

        Assert.Equal(PlanKind.Maximize, r.Kind);
        Assert.Equal(Primary.WorkArea, r.Target);
    }

    [Fact]
    public void Maximize_WithoutToggling_KeepsTheRestoredSizeForLater()
    {
        // Staying maximized mustn't forget the size to restore to.
        var p = NewPlanner();
        p.Begin(Hwnd, Primary.WorkArea, maximized: true, Displays, restored: Floating);
        p.Step(WindowAction.Maximize, true, toggleMaximize: false);
        var restored = p.Step(WindowAction.Maximize, true)!.Value;

        Assert.Equal(PlanKind.Restore, restored.Kind);
        Assert.Equal(Floating, restored.Target);
    }

    [Fact]
    public void Maximize_Twice_InOneHold_TogglesBack()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        p.Step(WindowAction.Maximize, true);
        var r = p.Step(WindowAction.Maximize, true)!.Value;

        Assert.Equal(PlanKind.Restore, r.Kind);
        Assert.Equal(Floating, r.Target);
    }

    [Fact]
    public void Maximize_IgnoresGaps()
    {
        var p = new GesturePlanner(new LayoutSession(gap: 12));
        p.Begin(Hwnd, Floating, false, Displays);
        Assert.Equal(Primary.WorkArea, p.Step(WindowAction.Maximize, true)!.Value.Target);
    }

    [Fact]
    public void MaximizedWindow_MovesAsMaximized_WithRestoredBoundsAlongside()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Primary.WorkArea, maximized: true, Displays, restored: Floating);
        var moved = p.Step(WindowAction.PreviousDisplay, true)!.Value;
        Assert.Equal(PlanKind.Maximize, moved.Kind);
        Assert.Equal(Lower, moved.Display);
        Assert.Equal(Lower.WorkArea, moved.Target);

        // Restoring afterwards returns to the restored size, now on the lower display.
        var restored = p.Step(WindowAction.Maximize, true)!.Value;
        Assert.Equal(PlanKind.Restore, restored.Kind);
        Assert.Equal(Lower, restored.Display);
        Assert.Equal(LayoutEngine.Remap(Floating, Primary.WorkArea, Lower.WorkArea), restored.Target);
        Assert.Equal(new Rect(-48, 1288, 800, 623), restored.Target); // 1040 -> 1080 tall: scaled to match
    }

    [Fact]
    public void DisplayMoveThenMaximize_MaximizesOnTheNewDisplay()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        p.Step(WindowAction.PreviousDisplay, true);
        var r = p.Step(WindowAction.Maximize, true)!.Value;
        Assert.Equal(PlanKind.Maximize, r.Kind);
        Assert.Equal(Lower, r.Display);
    }

    [Fact]
    public void LayoutOnMaximizedWindow_StartsFromRestoredSize()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Primary.WorkArea, maximized: true, Displays, restored: Floating);
        var center = p.Step(WindowAction.Center, true)!.Value;   // e.g. Left+Right

        Assert.Equal(PlanKind.Move, center.Kind);
        Assert.Equal(new Rect(560, 220, 800, 600), center.Target); // the 800x600 restored size, centered
    }

    [Fact]
    public void LayoutOnMaximizedWindow_IsAMove()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Primary.WorkArea, maximized: true, Displays, restored: Floating);
        var left = p.Step(WindowAction.LeftHalf, true)!.Value;
        Assert.Equal(PlanKind.Move, left.Kind);
        Assert.Equal(new Rect(0, 0, 960, 1040), left.Target);
    }

    [Fact]
    public void MaximizeAfterLayout_RestoresToThatLayout()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        var left = p.Step(WindowAction.LeftHalf, true)!.Value.Target;
        p.Step(WindowAction.Maximize, true);
        Assert.Equal(left, p.Step(WindowAction.Maximize, true)!.Value.Target);
    }

    [Fact]
    public void Minimize_IsPendingUntilReplaced()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        Assert.True(p.Step(WindowAction.Minimize, true)!.Value.Minimize);
        Assert.False(p.Step(WindowAction.RightHalf, true)!.Value.Minimize);
    }

    [Fact]
    public void ChordRefinement_InPreview_UsesPreGestureSize()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        p.Step(WindowAction.LeftHalf, newGesture: true);
        var center = p.Step(WindowAction.Center, newGesture: false)!.Value; // Right pressed with Left held
        Assert.Equal(new Rect(560, 220, 800, 600), center.Target);
    }

    [Fact]
    public void ReportLanded_UsesRealRect()
    {
        var p = NewPlanner();
        p.Begin(Hwnd, Floating, false, Displays);
        p.Step(WindowAction.LeftHalf, true);
        var planned = p.Finish()!.Value;
        var landed = planned.Target with { Width = 955 }; // app enforced its own size
        p.ReportLanded(Hwnd, planned, landed);

        Assert.Equal(1, _session.Cycles.Current(Hwnd, landed)?.Index + 1); // recorded at the real rect
    }

    [Fact]
    public void StepBeforeBegin_Throws() =>
        Assert.Throws<InvalidOperationException>(() => NewPlanner().Step(WindowAction.LeftHalf, true));

    [Fact]
    public void BeginWithoutDisplays_Throws() =>
        Assert.Throws<ArgumentException>(() => NewPlanner().Begin(Hwnd, Floating, false, Array.Empty<DisplayArea>()));
}
