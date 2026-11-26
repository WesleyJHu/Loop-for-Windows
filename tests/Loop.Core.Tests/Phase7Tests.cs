using System.Xml.Linq;
using Loop.Core.Actions;
using Loop.Core.Geometry;
using Loop.Core.Input;
using Loop.Core.Settings;

namespace Loop.Core.Tests;

public class RestorePreviousTests
{
    private static readonly DisplayArea Primary = new(1, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040));
    private static readonly DisplayArea Lower = new(2, new Rect(-348, 1080, 1920, 1080), new Rect(-348, 1080, 1920, 1080));
    private static readonly DisplayArea[] Displays = { Lower, Primary };
    private const nint Hwnd = 9;
    private static readonly Rect Floating = new(300, 200, 800, 600);

    private readonly LayoutSession _session = new();
    private Rect _window = Floating;
    private bool _maximized;

    /// <summary>One trigger hold with the given presses; applies the result exactly (as the platform would).</summary>
    private PlannedAction? Activation(params WindowAction[] presses) => ActivationWith(null, presses);

    private PlannedAction? ActivationWith(Rect? restored, params WindowAction[] presses)
    {
        var p = new GesturePlanner(_session);
        p.Begin(Hwnd, _window, _maximized, Displays, restored);
        foreach (var a in presses) p.Step(a, newGesture: true);
        var result = p.Finish();
        if (result is { } r)
        {
            p.ReportLanded(Hwnd, r, r.Target);
            if (!r.Minimize)
            {
                _window = r.Target;
                _maximized = r.Kind == PlanKind.Maximize;
            }
        }
        return result;
    }

    [Fact]
    public void UndoesBackToWhereTheUserHadIt()
    {
        Activation(WindowAction.LeftHalf);
        Activation(WindowAction.TopRight);
        Activation(WindowAction.LeftHalf);

        var undo = Activation(WindowAction.RestorePrevious)!.Value;
        Assert.Equal(PlanKind.Move, undo.Kind);
        Assert.Equal(Floating, undo.Target);
    }

    [Fact]
    public void NothingToUndo_ForAWindowLoopNeverMoved() =>
        Assert.Null(Activation(WindowAction.RestorePrevious));

    [Fact]
    public void NothingToUndo_AfterUndoing()
    {
        Activation(WindowAction.LeftHalf);
        Activation(WindowAction.RestorePrevious);
        Assert.Null(Activation(WindowAction.RestorePrevious));
    }

    [Fact]
    public void UserMovingTheWindow_StartsAFreshOrigin()
    {
        Activation(WindowAction.LeftHalf);
        _window = new Rect(100, 100, 700, 500);                 // the user drags it somewhere
        Assert.Null(Activation(WindowAction.RestorePrevious));  // free window: nothing to undo

        Activation(WindowAction.RightHalf);
        Assert.Equal(new Rect(100, 100, 700, 500), Activation(WindowAction.RestorePrevious)!.Value.Target);
    }

    [Fact]
    public void WithinOneHold_UndoReturnsToTheStart()
    {
        var p = new GesturePlanner(_session);
        p.Begin(Hwnd, Floating, false, Displays);
        p.Step(WindowAction.LeftHalf, true);
        Assert.Equal(Floating, p.Step(WindowAction.RestorePrevious, true)!.Value.Target);
    }

    [Fact]
    public void UndoIsPreviewedWithoutForgettingTheOrigin()
    {
        Activation(WindowAction.LeftHalf);
        var p = new GesturePlanner(_session);
        p.Begin(Hwnd, _window, false, Displays);
        p.Step(WindowAction.RestorePrevious, true);
        p.Cancel();                                             // Esc: nothing happened

        Assert.Equal(Floating, Activation(WindowAction.RestorePrevious)!.Value.Target);
    }

    [Fact]
    public void MaximizedByTheUser_IsMaximizedAgain()
    {
        // The user maximized it with the button (Loop never did), then used Loop.
        _window = Primary.WorkArea;
        _maximized = true;
        ActivationWith(restored: Floating, WindowAction.LeftHalf);

        var undo = Activation(WindowAction.RestorePrevious)!.Value;
        Assert.Equal(PlanKind.Maximize, undo.Kind);
        Assert.Equal(Primary, undo.Display);
        Assert.Equal(Floating, undo.NormalBounds); // restoring later goes back to its old size
    }

    [Fact]
    public void LoopsOwnMaximize_UndoesToTheFreeWindow()
    {
        Activation(WindowAction.Maximize);
        var undo = Activation(WindowAction.RestorePrevious)!.Value;
        Assert.Equal(PlanKind.Move, undo.Kind);
        Assert.Equal(Floating, undo.Target);
    }

    [Fact]
    public void AcrossDisplays_ReturnsToTheOriginalDisplay()
    {
        Activation(WindowAction.LeftHalf);
        Activation(WindowAction.PreviousDisplay);
        var undo = Activation(WindowAction.RestorePrevious)!.Value;
        Assert.Equal(Primary, undo.Display);
        Assert.Equal(Floating, undo.Target);
    }

    [Fact]
    public void OriginOnAnUnpluggedDisplay_IsBroughtOntoANearbyOne()
    {
        _window = new Rect(4000, 200, 800, 600);  // on a display that's gone
        Activation(WindowAction.LeftHalf);
        var undo = Activation(WindowAction.RestorePrevious)!.Value;
        Assert.True(undo.Display.WorkArea.Contains(undo.Target), $"{undo.Target} not inside {undo.Display.WorkArea}");
        Assert.Equal((800, 600), (undo.Target.Width, undo.Target.Height));
    }

    [Fact]
    public void Minimize_KeepsTheOrigin()
    {
        Activation(WindowAction.LeftHalf);
        Activation(WindowAction.Minimize);
        Assert.Equal(Floating, Activation(WindowAction.RestorePrevious)!.Value.Target);
    }

    [Fact]
    public void IsNotALayout_AndHasDefaultKey()
    {
        Assert.False(WindowAction.RestorePrevious.IsLayout());
        Assert.False(WindowAction.RestorePrevious.IsDisplayMove());
        Assert.Equal(WindowAction.RestorePrevious, KeyBindings.Default.ActionKeys[VirtualKeys.Backspace]);
        Assert.Equal("Backspace", LoopSettings.CreateDefault().ActionKeys[WindowAction.RestorePrevious]);
    }
}

public class ClampIntoTests
{
    private static readonly Rect Area = new(0, 0, 1920, 1040);

    [Fact] public void InsideStaysPut() => Assert.Equal(new Rect(10, 10, 100, 100), LayoutEngine.ClampInto(new Rect(10, 10, 100, 100), Area));
    [Fact] public void OutsideMovesIn() => Assert.Equal(new Rect(1120, 0, 800, 600), LayoutEngine.ClampInto(new Rect(4000, -50, 800, 600), Area));
    [Fact] public void TooBigShrinks() => Assert.Equal(Area, LayoutEngine.ClampInto(new Rect(-10, -10, 5000, 5000), Area));
}

public class StartupPlanTests
{
    [Theory]
    [InlineData(false, false, StartupMethod.None)]
    [InlineData(false, true, StartupMethod.None)]
    [InlineData(true, false, StartupMethod.RunKey)]
    [InlineData(true, true, StartupMethod.ScheduledTask)]
    public void Choose(bool launch, bool admin, StartupMethod expected) =>
        Assert.Equal(expected, StartupPlan.Choose(launch, admin));

    [Fact]
    public void TaskXml_IsValidAndRunsElevatedAtLogon()
    {
        var xml = StartupPlan.ScheduledTaskXml(@"C:\Program Files\Loop & Co\LoopForWindows.exe", "S-1-5-21-1-2-3-1001");
        var doc = XDocument.Parse(xml);
        XNamespace t = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal(@"C:\Program Files\Loop & Co\LoopForWindows.exe", doc.Descendants(t + "Command").Single().Value); // escaped and back
        Assert.Equal("HighestAvailable", doc.Descendants(t + "RunLevel").Single().Value);
        Assert.Equal("S-1-5-21-1-2-3-1001", doc.Descendants(t + "LogonTrigger").Single().Element(t + "UserId")!.Value);
        Assert.Equal("PT0S", doc.Descendants(t + "ExecutionTimeLimit").Single().Value);   // never killed after 72h
        Assert.Equal("false", doc.Descendants(t + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", doc.Descendants(t + "StopIfGoingOnBatteries").Single().Value);
    }

    [Fact]
    public void Settings_RoundTripAdminAndStartup()
    {
        var s = LoopSettings.CreateDefault();
        Assert.False(s.RunAsAdministrator);
        Assert.Null(s.LaunchAtStartup);
        s.RunAsAdministrator = true;
        s.LaunchAtStartup = true;
        var back = SettingsStore.Deserialize(SettingsStore.Serialize(s));
        Assert.True(back.RunAsAdministrator);
        Assert.True(back.LaunchAtStartup);
        Assert.Null(SettingsStore.Deserialize("{}").LaunchAtStartup); // older files: leave registration alone
    }
}
