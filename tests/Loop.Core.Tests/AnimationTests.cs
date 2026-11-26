using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Geometry;
using Loop.Core.Settings;

namespace Loop.Core.Tests;

public class EasingTests
{
    [Theory]
    [InlineData(EasingKind.EaseOut)]
    [InlineData(EasingKind.EaseInOut)]
    [InlineData(EasingKind.Linear)]
    public void StartsAtZero_EndsAtOne_AndNeverGoesBackwards(EasingKind kind)
    {
        Assert.Equal(0, Easing.Apply(kind, 0), 10);
        Assert.Equal(1, Easing.Apply(kind, 1), 10);
        var previous = 0.0;
        for (var t = 0.0; t <= 1.0; t += 0.01)
        {
            var v = Easing.Apply(kind, t);
            Assert.InRange(v, 0, 1);
            Assert.True(v >= previous - 1e-12, $"{kind} went backwards at {t}");
            previous = v;
        }
    }

    [Fact]
    public void EaseOut_IsAheadOfLinearMidway() => Assert.True(Easing.Apply(EasingKind.EaseOut, 0.5) > 0.8);

    [Fact]
    public void EaseInOut_IsSymmetric() =>
        Assert.Equal(1 - Easing.Apply(EasingKind.EaseInOut, 0.3), Easing.Apply(EasingKind.EaseInOut, 0.7), 10);

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    public void OutOfRangeInput_IsClamped(double t) => Assert.InRange(Easing.Apply(EasingKind.EaseOut, t), 0, 1);
}

public class RectTweenTests
{
    private static readonly Rect From = new(0, 0, 960, 1080);
    private static readonly Rect To = new(960, 0, 960, 1080);

    [Fact]
    public void Endpoints()
    {
        Assert.Equal(From, RectTween.At(From, To, 0));
        Assert.Equal(To, RectTween.At(From, To, 1));
        Assert.Equal(To, RectTween.At(From, To, 1.5));
    }

    [Fact]
    public void Midpoint() => Assert.Equal(new Rect(480, 0, 960, 1080), RectTween.At(From, To, 0.5));

    [Fact]
    public void EdgesInterpolate_SoTilesStayTiled()
    {
        // Left half shrinking to a left third, and right half growing to two thirds, meet at every step.
        var leftFrom = new Rect(0, 0, 960, 1080); var leftTo = new Rect(0, 0, 640, 1080);
        var rightFrom = new Rect(960, 0, 960, 1080); var rightTo = new Rect(640, 0, 1280, 1080);
        for (var p = 0.0; p <= 1; p += 0.05)
            Assert.Equal(RectTween.At(leftFrom, leftTo, p).Right, RectTween.At(rightFrom, rightTo, p).Left);
    }

    [Fact]
    public void Progress_FollowsTheClock()
    {
        var d = TimeSpan.FromMilliseconds(200);
        Assert.Equal(0, RectTween.Progress(TimeSpan.Zero, d, EasingKind.Linear));
        Assert.Equal(0.5, RectTween.Progress(TimeSpan.FromMilliseconds(100), d, EasingKind.Linear), 10);
        Assert.Equal(1, RectTween.Progress(TimeSpan.FromMilliseconds(500), d, EasingKind.Linear));
    }

    [Fact]
    public void ZeroDuration_IsImmediatelyDone() =>
        Assert.Equal(1, RectTween.Progress(TimeSpan.Zero, TimeSpan.Zero, EasingKind.EaseOut));
}

public class MotionPolicyTests
{
    private static readonly AnimationOptions On = new(true, true, TimeSpan.FromMilliseconds(180), EasingKind.EaseOut);
    private static readonly DisplayArea Primary = new(1, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1080), 96);
    private static readonly DisplayArea SameDpiRight = new(2, new Rect(1920, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080), 96);
    private static readonly DisplayArea Scaled125 = new(3, new Rect(-348, 1080, 1920, 1080), new Rect(-348, 1080, 1920, 1080), 120);
    private static readonly Rect A = new(0, 0, 960, 1080);
    private static readonly Rect B = new(960, 0, 960, 1080);

    private static bool Window(AnimationOptions? o = null, bool system = true, PlanKind kind = PlanKind.Move,
        Rect? from = null, Rect? to = null, DisplayArea? fromD = null, DisplayArea? toD = null, bool wasMax = false) =>
        MotionPolicy.ShouldAnimateWindow(o ?? On, system, kind, from ?? A, to ?? B, fromD ?? Primary, toD ?? Primary, wasMax);

    [Fact] public void NormalMove_Animates() => Assert.True(Window());
    [Fact] public void SystemAnimationsOff_DoesNot() => Assert.False(Window(system: false));
    [Fact] public void UserTurnedItOff_DoesNot() => Assert.False(Window(On with { AnimateWindows = false }));
    [Fact] public void ZeroDuration_DoesNot() => Assert.False(Window(On with { Duration = TimeSpan.Zero }));

    [Theory]
    [InlineData(PlanKind.Maximize)]
    [InlineData(PlanKind.Restore)]
    [InlineData(PlanKind.Minimize)]
    public void WindowsAnimatesThese_Itself(PlanKind kind) => Assert.False(Window(kind: kind));

    [Fact] public void ComingOutOfMaximized_DoesNot() => Assert.False(Window(wasMax: true));
    [Fact] public void TinyMove_DoesNot() => Assert.False(Window(to: A with { Left = 2, Width = 958 }));
    [Fact] public void ResizeOnly_Animates() => Assert.True(Window(to: A with { Width = 640 }));
    [Fact] public void AcrossSameDpiDisplays_Animates() => Assert.True(Window(toD: SameDpiRight, to: new Rect(1920, 0, 960, 1080)));
    [Fact] public void AcrossDifferentDpiDisplays_DoesNot() => Assert.False(Window(toD: Scaled125, to: new Rect(-348, 1080, 960, 1080)));
    [Fact] public void EmptyRect_DoesNot() => Assert.False(Window(from: new Rect(0, 0, 0, 0)));

    [Fact]
    public void Preview_FollowsItsOwnSwitch_AndTheSystem()
    {
        Assert.True(MotionPolicy.ShouldAnimatePreview(On, true));
        Assert.False(MotionPolicy.ShouldAnimatePreview(On, false));
        Assert.False(MotionPolicy.ShouldAnimatePreview(On with { AnimatePreview = false }, true));
        Assert.True(MotionPolicy.ShouldAnimatePreview(On with { AnimateWindows = false }, true));
    }

    [Fact]
    public void FrameBounds_SameDisplay_IsThatDisplay() =>
        Assert.Equal(Primary.Bounds, MotionPolicy.FrameBounds(Primary, Primary));

    [Fact]
    public void FrameBounds_TwoDisplays_SpansBoth() =>
        Assert.Equal(new Rect(0, 0, 3840, 1080), MotionPolicy.FrameBounds(Primary, SameDpiRight));

    [Fact]
    public void FrameBounds_KeepsBottomTileFramesOffTheMonitorBelow()
    {
        // Bottom half on the primary: outer rect pokes 7px down onto the 125% display; frames are clipped.
        var outer = new Rect(-7, 540, 1934, 547);
        var clipped = outer.Intersect(MotionPolicy.FrameBounds(Primary, Primary));
        Assert.Equal(1080, clipped.Bottom);
    }

    [Fact]
    public void FadeDuration_IsShortAndNeverLongerThanTheMove()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), On.FadeDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(60), (On with { Duration = TimeSpan.FromMilliseconds(60) }).FadeDuration);
    }
}

public class SlowFrameDetectorTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void FastFrames_KeepGoing()
    {
        var d = new SlowFrameDetector();
        for (var i = 0; i < 20; i++) Assert.False(d.Record(Ms(4)));
    }

    [Fact]
    public void OneHiccup_IsForgiven() => Assert.False(new SlowFrameDetector().Record(Ms(40)));

    [Fact]
    public void RepeatedSlowFrames_GiveUp()
    {
        var d = new SlowFrameDetector();
        d.Record(Ms(40));
        Assert.True(d.Record(Ms(45)));
    }

    [Fact]
    public void BadlyStuckFrame_GivesUpAtOnce() => Assert.True(new SlowFrameDetector().Record(Ms(150)));
}

public class IndicatorTests
{
    [Theory]
    [InlineData(WindowAction.RightHalf, IndicatorSegment.Right)]
    [InlineData(WindowAction.TopRight, IndicatorSegment.TopRight)]
    [InlineData(WindowAction.TopHalf, IndicatorSegment.Top)]
    [InlineData(WindowAction.TopLeft, IndicatorSegment.TopLeft)]
    [InlineData(WindowAction.LeftHalf, IndicatorSegment.Left)]
    [InlineData(WindowAction.BottomLeft, IndicatorSegment.BottomLeft)]
    [InlineData(WindowAction.BottomHalf, IndicatorSegment.Bottom)]
    [InlineData(WindowAction.BottomRight, IndicatorSegment.BottomRight)]
    [InlineData(WindowAction.Maximize, IndicatorSegment.Center)]
    [InlineData(WindowAction.Center, IndicatorSegment.Center)]
    [InlineData(WindowAction.CenterQuarter, IndicatorSegment.Center)]
    [InlineData(WindowAction.Minimize, IndicatorSegment.None)]
    [InlineData(WindowAction.NextDisplay, IndicatorSegment.None)]
    public void SegmentForAction(WindowAction action, IndicatorSegment expected) =>
        Assert.Equal(expected, Indicator.SegmentFor(action));

    [Theory]
    [InlineData(IndicatorSegment.Right, 0)]
    [InlineData(IndicatorSegment.Top, 90)]
    [InlineData(IndicatorSegment.Left, 180)]
    [InlineData(IndicatorSegment.BottomRight, 315)]
    public void Angles(IndicatorSegment s, double degrees) => Assert.Equal(degrees, Indicator.AngleOf(s));

    [Fact]
    public void CenterHasNoAngle() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Indicator.AngleOf(IndicatorSegment.Center));
}

public class AnimationSettingsTests
{
    [Fact]
    public void Defaults()
    {
        var s = LoopSettings.CreateDefault();
        Assert.True(s.AnimateWindows);
        Assert.True(s.AnimatePreview);
        Assert.False(s.ShowIndicator);
        Assert.Equal(new AnimationOptions(true, true, TimeSpan.FromMilliseconds(180), EasingKind.EaseOut), s.ToAnimationOptions());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidDuration(int ms)
    {
        var s = LoopSettings.CreateDefault();
        s.AnimationDurationMs = ms;
        Assert.Contains(s.Validate(), e => e.Contains("Animation duration"));
    }

    [Fact]
    public void RoundTrip()
    {
        var s = LoopSettings.CreateDefault();
        s.AnimationEasing = EasingKind.EaseInOut;
        s.AnimationDurationMs = 250;
        s.ShowIndicator = true;
        s.AnimateWindows = false;
        var json = SettingsStore.Serialize(s);
        Assert.Contains("\"AnimationEasing\": \"EaseInOut\"", json);
        var back = SettingsStore.Deserialize(json);
        Assert.Equal(s.ToAnimationOptions(), back.ToAnimationOptions());
        Assert.True(back.ShowIndicator);
    }

    [Fact]
    public void OlderSettingsFile_GetsAnimationDefaults()
    {
        var s = SettingsStore.Deserialize("""{ "Gap": 4 }""");
        Assert.True(s.AnimateWindows);
        Assert.Equal(180, s.AnimationDurationMs);
    }
}
