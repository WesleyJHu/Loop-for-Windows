using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Input;
using Loop.Core.Settings;

namespace Loop.Core.Tests;

public class MouseMenuControllerTests
{
    private const double DeadZone = 20;

    private readonly List<(Action Callback, Token Token)> _timers = new();
    private readonly List<(MouseButton Button, bool Down)> _injected = new();
    private bool _injectionWorks = true;
    private uint _now = 1000;
    private readonly MouseMenuController _menu;

    private sealed class Token : IDisposable
    {
        public bool Cancelled { get; private set; }
        public void Dispose() => Cancelled = true;
    }

    public MouseMenuControllerTests()
    {
        _menu = new MouseMenuController(
            (_, _) => DeadZone,
            (_, callback) =>
            {
                var token = new Token();
                _timers.Add((callback, token));
                return token;
            },
            events =>
            {
                if (!_injectionWorks) return false;
                _injected.AddRange(events);
                return true;
            },
            () => _now);
        _menu.Configure(MouseTrigger.MiddleDoubleClick, TimeSpan.FromMilliseconds(300));
    }

    private void Elapse()
    {
        var due = _timers.ToList();
        _timers.Clear();
        foreach (var (callback, token) in due)
            if (!token.Cancelled) callback();
    }

    private MouseDecision Down(MouseButton b, int x = 100, int y = 100, bool canOpen = true) =>
        _menu.Process(new MouseEvent(MouseEventKind.Down, x, y, _now, b), canOpen);

    private MouseDecision Up(MouseButton b, int x = 100, int y = 100) =>
        _menu.Process(new MouseEvent(MouseEventKind.Up, x, y, _now, b), canOpen: true);

    private MouseDecision Move(int x, int y) =>
        _menu.Process(new MouseEvent(MouseEventKind.Move, x, y, _now), canOpen: true);

    private MouseDecision Wheel() =>
        _menu.Process(new MouseEvent(MouseEventKind.Wheel, 0, 0, _now, MouseButton.Middle), canOpen: true);

    /// <summary>The hook seeing the events Loop injected (they arrive marked as injected).</summary>
    private void DeliverInjected()
    {
        foreach (var (button, down) in _injected.ToList())
        {
            var d = _menu.Process(new MouseEvent(down ? MouseEventKind.Down : MouseEventKind.Up, 0, 0, _now, button, Injected: true), true);
            Assert.False(d.Swallow);
        }
        _injected.Clear();
    }

    private MouseDecision OpenWithDoubleClick(int x = 100, int y = 100)
    {
        Assert.True(Down(MouseButton.Middle, x, y).Swallow);
        Assert.True(Up(MouseButton.Middle, x, y).Swallow);
        _now += 120;
        return Down(MouseButton.Middle, x, y);
    }

    // ---- Opening ------------------------------------------------------------------------------------

    [Fact]
    public void Middle_double_click_opens_the_menu_and_apps_see_neither_click()
    {
        var open = OpenWithDoubleClick(150, 250);

        Assert.True(open.Swallow);
        Assert.Equal(MouseMenuEffect.Open, open.Effect);
        Assert.Equal((150, 250), (open.X, open.Y));
        Assert.True(_menu.IsOpen);
        Assert.Empty(_injected);
    }

    [Fact]
    public void A_lone_middle_click_is_replayed_after_the_double_click_time()
    {
        Assert.True(Down(MouseButton.Middle).Swallow);
        Assert.True(Up(MouseButton.Middle).Swallow);
        Assert.Empty(_injected);

        Elapse();

        Assert.Equal(new[] { (MouseButton.Middle, true), (MouseButton.Middle, false) }, _injected);
        Assert.False(_menu.IsOpen);
    }

    [Fact]
    public void A_held_middle_press_is_replayed_as_a_press_and_its_release_passes_through()
    {
        Down(MouseButton.Middle);
        Elapse();
        Assert.Equal(new[] { (MouseButton.Middle, true) }, _injected);
        DeliverInjected();

        Assert.False(Up(MouseButton.Middle).Swallow);
    }

    [Fact]
    public void Dragging_with_the_middle_button_lets_the_press_through_at_once()
    {
        Down(MouseButton.Middle);
        Assert.False(Move(100 + MouseMenuController.DoubleClickDistance + 1, 100).Swallow);

        Assert.Equal(new[] { (MouseButton.Middle, true) }, _injected);
        Assert.Empty(_timers.Where(t => !t.Token.Cancelled));
    }

    [Fact]
    public void A_second_press_too_late_or_too_far_away_is_not_a_double_click()
    {
        Down(MouseButton.Middle);
        Up(MouseButton.Middle);
        _now += 301;
        var late = Down(MouseButton.Middle);
        Assert.NotEqual(MouseMenuEffect.Open, late.Effect);
        Assert.Equal(new[] { (MouseButton.Middle, true), (MouseButton.Middle, false) }, _injected);

        _menu.Reset();
        _injected.Clear();
        _now += 5000;
        Down(MouseButton.Middle, 100, 100);
        Up(MouseButton.Middle, 100, 100);
        var far = Down(MouseButton.Middle, 100 + MouseMenuController.DoubleClickDistance + 1, 100);
        Assert.NotEqual(MouseMenuEffect.Open, far.Effect);
        Assert.False(_menu.IsOpen);
    }

    [Fact]
    public void Another_button_lets_the_held_back_click_through_first()
    {
        Down(MouseButton.Middle);
        Up(MouseButton.Middle);
        Assert.False(Down(MouseButton.Left).Swallow);
        Assert.Equal(new[] { (MouseButton.Middle, true), (MouseButton.Middle, false) }, _injected);
    }

    [Fact]
    public void Middle_clicks_pass_untouched_when_the_menu_cannot_open()
    {
        Assert.False(Down(MouseButton.Middle, canOpen: false).Swallow);
        Assert.False(Up(MouseButton.Middle).Swallow);
        Assert.Empty(_injected);
    }

    [Fact]
    public void Middle_clicks_pass_untouched_when_the_trigger_is_off()
    {
        _menu.Configure(MouseTrigger.Off, TimeSpan.FromMilliseconds(300));
        Assert.False(Down(MouseButton.Middle).Swallow);
        Assert.False(Up(MouseButton.Middle).Swallow);
        Assert.False(Down(MouseButton.X1).Swallow);
    }

    [Fact]
    public void A_side_button_trigger_opens_on_a_single_press_without_touching_middle_clicks()
    {
        _menu.Configure(MouseTrigger.BackButton, TimeSpan.FromMilliseconds(300));
        Assert.False(Down(MouseButton.Middle).Swallow);
        Assert.False(Up(MouseButton.Middle).Swallow);

        var open = Down(MouseButton.X1);
        Assert.Equal(MouseMenuEffect.Open, open.Effect);
        Move(200, 100);
        var commit = Up(MouseButton.X1, 200, 100);
        Assert.Equal(MouseMenuEffect.Commit, commit.Effect);
        Assert.Equal(IndicatorSegment.Right, commit.Segment);
        Assert.Empty(_injected);
    }

    // ---- Hold, move, release ------------------------------------------------------------------------

    [Fact]
    public void Holding_and_moving_selects_a_direction_and_releasing_applies_it()
    {
        OpenWithDoubleClick();

        var left = Move(40, 100);
        Assert.False(left.Swallow); // the cursor must keep moving
        Assert.Equal((MouseMenuEffect.Select, IndicatorSegment.Left), (left.Effect, left.Segment));

        var corner = Move(40, 40);
        Assert.Equal(IndicatorSegment.TopLeft, corner.Segment);
        Assert.Equal(MouseMenuEffect.None, Move(30, 30).Effect); // same segment: nothing new

        var release = Up(MouseButton.Middle, 30, 30);
        Assert.True(release.Swallow);
        Assert.Equal((MouseMenuEffect.Commit, IndicatorSegment.TopLeft), (release.Effect, release.Segment));
        Assert.False(_menu.IsOpen);
    }

    [Fact]
    public void While_held_the_center_selects_nothing_until_the_cursor_has_left_it()
    {
        OpenWithDoubleClick();
        Assert.Equal(MouseMenuEffect.None, Move(105, 95).Effect);
        Assert.Equal(IndicatorSegment.None, _menu.Selected);

        Move(160, 100);
        var back = Move(100, 100);
        Assert.Equal((MouseMenuEffect.Select, IndicatorSegment.Center), (back.Effect, back.Segment));

        var release = Up(MouseButton.Middle);
        Assert.Equal((MouseMenuEffect.Commit, IndicatorSegment.Center), (release.Effect, release.Segment));
    }

    [Fact]
    public void Right_click_cancels_and_both_releases_are_swallowed()
    {
        OpenWithDoubleClick();
        Move(160, 100);

        var cancel = Down(MouseButton.Right);
        Assert.True(cancel.Swallow);
        Assert.Equal(MouseMenuEffect.Cancel, cancel.Effect);
        Assert.False(_menu.IsOpen);

        Assert.True(Up(MouseButton.Right).Swallow);
        Assert.True(Up(MouseButton.Middle).Swallow);
        Assert.False(Up(MouseButton.Middle).Swallow); // only once
    }

    [Fact]
    public void The_wheel_cycles_the_selected_direction_and_never_scrolls_the_app()
    {
        OpenWithDoubleClick();
        var nothing = Wheel();
        Assert.True(nothing.Swallow);
        Assert.Equal(MouseMenuEffect.None, nothing.Effect);

        Move(100, 180);
        var cycle = Wheel();
        Assert.True(cycle.Swallow);
        Assert.Equal((MouseMenuEffect.Cycle, IndicatorSegment.Bottom), (cycle.Effect, cycle.Segment));
    }

    // ---- Release without moving: click mode ---------------------------------------------------------

    [Fact]
    public void Releasing_without_moving_keeps_the_menu_open_with_the_center_selected()
    {
        OpenWithDoubleClick();
        var release = Up(MouseButton.Middle);

        Assert.True(release.Swallow);
        Assert.Equal((MouseMenuEffect.Select, IndicatorSegment.Center), (release.Effect, release.Segment));
        Assert.True(_menu.IsOpen);
    }

    [Fact]
    public void In_click_mode_moving_selects_and_a_left_click_applies()
    {
        OpenWithDoubleClick();
        Up(MouseButton.Middle);

        Assert.Equal(IndicatorSegment.BottomRight, Move(150, 150).Segment);
        var click = Down(MouseButton.Left, 150, 150);
        Assert.True(click.Swallow);
        Assert.Equal((MouseMenuEffect.Commit, IndicatorSegment.BottomRight), (click.Effect, click.Segment));
        Assert.True(Up(MouseButton.Left, 150, 150).Swallow); // the app never sees the click
    }

    [Fact]
    public void In_click_mode_the_trigger_button_also_applies_and_clicking_the_center_maximizes()
    {
        OpenWithDoubleClick();
        Up(MouseButton.Middle);

        var click = Down(MouseButton.Middle);
        Assert.Equal((MouseMenuEffect.Commit, IndicatorSegment.Center), (click.Effect, click.Segment));
        Assert.Equal(WindowAction.Maximize, Indicator.ActionFor(click.Segment));
        Assert.True(Up(MouseButton.Middle).Swallow);
        Assert.Empty(_injected);
    }

    [Fact]
    public void In_click_mode_right_click_cancels()
    {
        OpenWithDoubleClick();
        Up(MouseButton.Middle);
        Move(40, 100);
        Assert.Equal(MouseMenuEffect.Cancel, Down(MouseButton.Right).Effect);
        Assert.True(Up(MouseButton.Right).Swallow);
    }

    [Fact]
    public void Cancelling_from_outside_closes_the_menu_but_still_swallows_the_release()
    {
        OpenWithDoubleClick();
        Assert.True(_menu.CancelMenu());
        Assert.False(_menu.CancelMenu());
        Assert.True(Up(MouseButton.Middle).Swallow);
    }

    [Fact]
    public void Reset_lets_a_held_back_click_through()
    {
        Down(MouseButton.Middle);
        Up(MouseButton.Middle);
        Assert.False(_menu.Reset());
        Assert.Equal(new[] { (MouseButton.Middle, true), (MouseButton.Middle, false) }, _injected);
        Elapse(); // the cancelled timer does nothing more
        Assert.Equal(2, _injected.Count);
    }

    // ---- Keeping replayed clicks in order -----------------------------------------------------------

    [Fact]
    public void A_real_release_racing_a_replayed_press_is_queued_behind_it()
    {
        Down(MouseButton.Middle);
        Elapse(); // replays the press; the hook hasn't seen it yet
        Assert.Equal(new[] { (MouseButton.Middle, true) }, _injected);

        Assert.True(Up(MouseButton.Middle).Swallow);
        Assert.Equal(new[] { (MouseButton.Middle, true), (MouseButton.Middle, false) }, _injected);

        DeliverInjected();
        Assert.False(Down(MouseButton.Middle, canOpen: false).Swallow); // back to normal
    }

    [Fact]
    public void Lost_injected_events_stop_being_waited_for()
    {
        Down(MouseButton.Middle);
        Elapse();
        _now += 5000; // never delivered
        Assert.False(Up(MouseButton.Middle).Swallow);
    }

    [Fact]
    public void Failed_injection_holds_nothing_back()
    {
        _injectionWorks = false;
        Down(MouseButton.Middle);
        Elapse();
        Assert.False(Up(MouseButton.Middle).Swallow);
    }

    // ---- Geometry -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(50, 0, IndicatorSegment.Right)]
    [InlineData(50, -50, IndicatorSegment.TopRight)]
    [InlineData(0, -50, IndicatorSegment.Top)]
    [InlineData(-50, -50, IndicatorSegment.TopLeft)]
    [InlineData(-50, 0, IndicatorSegment.Left)]
    [InlineData(-50, 50, IndicatorSegment.BottomLeft)]
    [InlineData(0, 50, IndicatorSegment.Bottom)]
    [InlineData(50, 50, IndicatorSegment.BottomRight)]
    [InlineData(50, 15, IndicatorSegment.Right)]        // within 22.5 degrees of the right
    [InlineData(50, -25, IndicatorSegment.TopRight)]    // past 22.5 degrees
    [InlineData(10, 10, IndicatorSegment.Center)]
    [InlineData(20, 0, IndicatorSegment.Center)]
    public void SegmentAt_divides_the_ring_into_eight_around_a_center(double dx, double dy, IndicatorSegment expected) =>
        Assert.Equal(expected, MouseMenuController.SegmentAt(dx, dy, DeadZone));

    [Fact]
    public void Every_direction_segment_maps_to_its_layout_and_back()
    {
        for (var s = IndicatorSegment.Right; s <= IndicatorSegment.BottomRight; s++)
            Assert.Equal(s, Indicator.SegmentFor(Indicator.ActionFor(s)!.Value));
        Assert.Null(Indicator.ActionFor(IndicatorSegment.None));
    }

    // ---- Settings -----------------------------------------------------------------------------------

    [Fact]
    public void Mouse_settings_default_off_and_round_trip()
    {
        var defaults = LoopSettings.CreateDefault();
        Assert.Equal(MouseTrigger.Off, defaults.MouseTrigger);

        var s = LoopSettings.CreateDefault();
        s.MouseTrigger = MouseTrigger.MiddleDoubleClick;
        s.MouseDoubleClickMs = 400;
        var back = SettingsStore.Deserialize(SettingsStore.Serialize(s));
        Assert.Equal(MouseTrigger.MiddleDoubleClick, back.MouseTrigger);
        Assert.Equal(400, back.MouseDoubleClickMs);
        Assert.Equal(400, s.Clone().MouseDoubleClickMs);
        Assert.Contains("\"MiddleDoubleClick\"", SettingsStore.Serialize(s));
    }

    [Theory]
    [InlineData(LoopSettings.MinMouseDoubleClickMs - 1)]
    [InlineData(LoopSettings.MaxMouseDoubleClickMs + 1)]
    public void Mouse_double_click_speed_is_validated(int ms)
    {
        var s = LoopSettings.CreateDefault();
        s.MouseDoubleClickMs = ms;
        Assert.Contains(s.Validate(), e => e.Contains("Double-click speed"));
    }
}
