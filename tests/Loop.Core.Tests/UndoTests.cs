using Loop.Core.Actions;
using Loop.Core.Geometry;
using Loop.Core.Input;
using Loop.Core.Settings;

namespace Loop.Core.Tests;

public class UndoTests
{
    private static readonly DisplayArea Primary = new(1, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040));
    private static readonly DisplayArea Lower = new(2, new Rect(-348, 1080, 1920, 1080), new Rect(-348, 1080, 1920, 1080));
    private static readonly DisplayArea[] Displays = { Lower, Primary };
    private const nint Hwnd = 9;
    private static readonly Rect Floating = new(300, 200, 800, 600);

    private readonly LayoutSession _session = new();
    private readonly UndoHistory _history = new();
    private Rect _window = Floating;
    private bool _maximized;
    private bool _minimized;
    private Rect? _restored;

    /// <summary>One trigger hold; applies the result exactly and records it, as the platform does.</summary>
    private PlannedAction? Activation(params WindowAction[] presses)
    {
        var p = new GesturePlanner(_session);
        p.Begin(Hwnd, _window, _maximized, Displays, _maximized ? _restored : null);
        foreach (var a in presses) p.Step(a, newGesture: true);
        if (p.Finish() is not { } r) return null;
        var before = new WindowSnapshot(_window, _maximized, _minimized, _maximized ? _restored : null);
        Land(p, r);
        _history.Push(new UndoEntry(Hwnd, r.Action, before, r.PreviousCycle, r.PreviousOrigin));
        return r;
    }

    private PlannedAction? Undo()
    {
        if (!_history.TryPop(_ => true, out var entry)) return null;
        var r = UndoHistory.Plan(entry, Displays, _minimized);
        Land(new GesturePlanner(_session), r);
        return r;
    }

    private void Land(GesturePlanner p, PlannedAction r)
    {
        p.ReportLanded(Hwnd, r, r.Target);
        switch (r.Kind)
        {
            case PlanKind.Minimize: _minimized = true; break;
            case PlanKind.Restore when _minimized: _minimized = false; break;
            case PlanKind.Maximize:
                if (!_maximized) _restored = r.NormalBounds ?? _window;
                _window = r.Target; _maximized = true; _minimized = false; break;
            default:
                _window = r.Target; _maximized = false; _minimized = false; break;
        }
    }

    [Fact]
    public void StepsBackOneActionAtATime()
    {
        var left = Activation(WindowAction.LeftHalf)!.Value.Target;
        Activation(WindowAction.TopRight);

        Assert.Equal(left, Undo()!.Value.Target);
        Assert.Equal(Floating, Undo()!.Value.Target);
        Assert.Null(Undo());
    }

    [Fact]
    public void UndoesAMinimize_ByRestoringTheWindow()
    {
        var left = Activation(WindowAction.LeftHalf)!.Value.Target;
        Activation(WindowAction.Minimize);

        var undo = Undo()!.Value;
        Assert.Equal(PlanKind.Restore, undo.Kind);
        Assert.False(_minimized);
        Assert.Equal(left, _window);
        Assert.Equal(Floating, Undo()!.Value.Target); // and then the layout before it
    }

    [Fact]
    public void UndoingAMinimizeOfAMaximizedWindow_ComesBackMaximized()
    {
        Activation(WindowAction.Maximize);
        Activation(WindowAction.Minimize);
        Assert.Equal(PlanKind.Restore, Undo()!.Value.Kind);
        Assert.True(_maximized);
    }

    [Fact]
    public void AMinimizedWindowTheUserAlreadyRestored_IsJustPutBack()
    {
        Activation(WindowAction.Minimize);
        _minimized = false;                          // restored from the taskbar
        var undo = Undo()!.Value;
        Assert.Equal(PlanKind.Move, undo.Kind);
        Assert.Equal(Floating, undo.Target);
    }

    [Fact]
    public void UndoesAMaximize_ToTheFreeWindow()
    {
        Activation(WindowAction.Maximize);
        var undo = Undo()!.Value;
        Assert.Equal(PlanKind.Move, undo.Kind);
        Assert.Equal(Floating, undo.Target);
    }

    [Fact]
    public void UndoesARestore_ByMaximizingAgainWithTheOldSize()
    {
        _window = Primary.WorkArea;
        _maximized = true;
        _restored = Floating;
        Activation(WindowAction.LeftHalf);

        var undo = Undo()!.Value;
        Assert.Equal(PlanKind.Maximize, undo.Kind);
        Assert.Equal(Primary, undo.Display);
        Assert.Equal(Floating, undo.NormalBounds);
    }

    [Fact]
    public void UndoesADisplayMove()
    {
        Activation(WindowAction.LeftHalf);
        var left = _window;
        Activation(WindowAction.PreviousDisplay);
        var undo = Undo()!.Value;
        Assert.Equal(Primary, undo.Display);
        Assert.Equal(left, undo.Target);
    }

    [Fact]
    public void PutsTheCycleBack_SoTheNextPressContinuesFromThere()
    {
        Activation(WindowAction.LeftHalf);                     // 1/2
        Activation(WindowAction.LeftHalf);                     // 1/3
        Undo();                                                 // back to 1/2
        var next = Activation(WindowAction.LeftHalf)!.Value;   // 1/3 again, not 2/3
        Assert.Equal(1, next.Plan.Index);
    }

    [Fact]
    public void PutsTheOriginBack()
    {
        Activation(WindowAction.LeftHalf);
        Undo();                                             // free again: nothing for "restore previous"
        Assert.Null(_session.OriginOf(Hwnd));

        Activation(WindowAction.LeftHalf);
        Activation(WindowAction.RightHalf);
        Undo();                                             // still in Loop's layouts: origin kept
        Assert.Equal(Floating, _session.OriginOf(Hwnd)!.Value.Bounds);
    }

    [Fact]
    public void ClosedWindows_AreSkipped()
    {
        var history = new UndoHistory();
        var before = new WindowSnapshot(Floating, false, false);
        history.Push(new UndoEntry(1, WindowAction.LeftHalf, before, null, null));
        history.Push(new UndoEntry(2, WindowAction.Minimize, before, null, null));

        Assert.True(history.TryPop(w => w != 2, out var entry));
        Assert.Equal(1, entry.Window);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void KeepsOnlyTheMostRecent()
    {
        var history = new UndoHistory(capacity: 2);
        var before = new WindowSnapshot(Floating, false, false);
        for (nint w = 1; w <= 3; w++) history.Push(new UndoEntry(w, WindowAction.LeftHalf, before, null, null));
        Assert.Equal(2, history.Count);
        history.TryPop(_ => true, out _);
        history.TryPop(_ => true, out var oldest);
        Assert.Equal(2, oldest.Window);
    }

    [Fact]
    public void UndoIsNotRecorded()
    {
        var history = new UndoHistory();
        history.Push(new UndoEntry(1, WindowAction.Undo, new WindowSnapshot(Floating, false, false), null, null));
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void OriginOnAnUnpluggedDisplay_IsBroughtOntoANearbyOne()
    {
        var entry = new UndoEntry(1, WindowAction.LeftHalf, new WindowSnapshot(new Rect(4000, 200, 800, 600), false, false), null, null);
        var undo = UndoHistory.Plan(entry, Displays, minimizedNow: false);
        Assert.True(undo.Display.WorkArea.Contains(undo.Target), $"{undo.Target} not inside {undo.Display.WorkArea}");
    }

    [Fact]
    public void IsBoundToBackspaceByDefault()
    {
        Assert.False(WindowAction.Undo.IsLayout());
        Assert.Equal(WindowAction.Undo, KeyBindings.Default.ActionKeys[VirtualKeys.Backspace]);
        Assert.Equal("Backspace", LoopSettings.CreateDefault().ActionKeys[WindowAction.Undo]);
    }

    [Fact]
    public void OldSettings_MoveBackspaceFromRestorePreviousToUndo()
    {
        var s = SettingsStore.Deserialize("""{ "ActionKeys": { "Maximize": "Enter", "RestorePrevious": "Backspace" } }""");
        Assert.Equal("Backspace", s.ActionKeys[WindowAction.Undo]);
        Assert.False(s.ActionKeys.ContainsKey(WindowAction.RestorePrevious));
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void OldSettings_KeepAUserBindingOfBackspace()
    {
        var s = SettingsStore.Deserialize("""{ "ActionKeys": { "Center": "Backspace", "RestorePrevious": "R" } }""");
        Assert.False(s.ActionKeys.ContainsKey(WindowAction.Undo));
        Assert.Equal("Backspace", s.ActionKeys[WindowAction.Center]);
        Assert.Equal("R", s.ActionKeys[WindowAction.RestorePrevious]);
    }

    [Fact]
    public void Settings_KeepAnUnboundUndoUnbound()
    {
        var s = SettingsStore.Deserialize("""{ "ActionKeys": { "Undo": "" } }""");
        Assert.Equal("", s.ActionKeys[WindowAction.Undo]);
    }
}
