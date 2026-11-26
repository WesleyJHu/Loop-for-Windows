using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Loop.Core.Animation;
using Loop.Core.Settings;
using Loop.Platform;
using Rect = Loop.Core.Geometry.Rect;

namespace Loop.App;

/// <summary>
/// A ring of eight direction segments around a center dot, shown at the cursor while the trigger
/// is held. The segment for the chosen direction lights up (the center for maximize and centering).
/// </summary>
internal sealed class DirectionIndicator : IDirectionIndicator
{
    private const double Size = 120;       // DIPs; scaled to the monitor's DPI when shown
    private const double OuterRadius = 56, InnerRadius = 34, CenterRadius = 18, HalfSpan = 20;

    private static readonly Color Track = Color.FromArgb(0x99, 0x20, 0x20, 0x24);

    private readonly ClickThroughWindow _window;
    private readonly Dictionary<IndicatorSegment, SolidColorBrush> _fills = new();
    private AnimationOptions _animation = AnimationOptions.None;
    private Color _accent = Colors.DodgerBlue;
    private IndicatorSegment _lit = IndicatorSegment.None;

    public DirectionIndicator(string colorHex)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        canvas.Children.Add(new Ellipse
        {
            Width = OuterRadius * 2 + 4, Height = OuterRadius * 2 + 4,
            Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x10, 0x12)),
        });
        Canvas.SetLeft(canvas.Children[0], Size / 2 - OuterRadius - 2);
        Canvas.SetTop(canvas.Children[0], Size / 2 - OuterRadius - 2);

        for (var s = IndicatorSegment.Right; s <= IndicatorSegment.BottomRight; s++)
        {
            var fill = new SolidColorBrush(Track);
            _fills[s] = fill;
            canvas.Children.Add(new Path { Data = Wedge(Indicator.AngleOf(s)), Fill = fill });
        }

        var centerFill = new SolidColorBrush(Track);
        _fills[IndicatorSegment.Center] = centerFill;
        var center = new Ellipse { Width = CenterRadius * 2, Height = CenterRadius * 2, Fill = centerFill };
        Canvas.SetLeft(center, Size / 2 - CenterRadius);
        Canvas.SetTop(center, Size / 2 - CenterRadius);
        canvas.Children.Add(center);

        _window = new ClickThroughWindow("Loop indicator", new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
        SetColor(colorHex);
    }

    public void SetColor(string colorHex)
    {
        if (LoopSettings.TryParseColor(colorHex, out var c)) _accent = Color.FromRgb(c.R, c.G, c.B);
        Light(_lit, animate: false);
    }

    public void SetAnimation(AnimationOptions options) => _animation = options;

    /// <inheritdoc cref="ClickThroughWindow.Prewarm"/>
    public void Prewarm() => _window.Prewarm();

    public void Show(IndicatorSegment segment, bool animate, (int X, int Y)? center = null)
    {
        var fade = animate ? _animation.FadeDuration : TimeSpan.Zero;
        if (!_window.IsVisible)
        {
            Light(segment, animate: false);
            _window.EnsureShown(startTransparent: animate);
            _window.SetBounds(Around(center ?? CursorPosition()));
        }
        else
        {
            if (center is { } c) _window.SetBounds(Around(c)); // e.g. reopened while still fading out
            Light(segment, animate);
        }
        _window.Fade(1, fade);
    }

    public void Hide(bool animate)
    {
        if (animate) _window.FadeOutAndHide(_animation.FadeDuration);
        else _window.HideNow();
    }

    public void Close() => _window.Close();

    private void Light(IndicatorSegment segment, bool animate)
    {
        _lit = segment;
        var duration = animate ? _animation.FadeDuration : TimeSpan.Zero;
        foreach (var (s, brush) in _fills)
        {
            var to = s == segment ? _accent : Track;
            if (duration <= TimeSpan.Zero)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = to;
            }
            else
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, new Duration(duration)));
            }
        }
    }

    /// <summary>One ring segment centered on <paramref name="angle"/> (degrees, counter-clockwise from the right).</summary>
    private static Geometry Wedge(double angle)
    {
        Point At(double radius, double degrees)
        {
            var rad = degrees * Math.PI / 180;
            return new Point(Size / 2 + radius * Math.Cos(rad), Size / 2 - radius * Math.Sin(rad)); // screen y grows down
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(OuterRadius, angle - HalfSpan), isFilled: true, isClosed: true);
            ctx.ArcTo(At(OuterRadius, angle + HalfSpan), new Size(OuterRadius, OuterRadius), 0, false,
                SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(At(InnerRadius, angle + HalfSpan), true, true);
            ctx.ArcTo(At(InnerRadius, angle - HalfSpan), new Size(InnerRadius, InnerRadius), 0, false,
                SweepDirection.Clockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>The indicator's bounds in physical pixels, centered on a point and sized for its monitor's DPI.</summary>
    private static Rect Around((int X, int Y) center)
    {
        var p = new POINT { X = center.X, Y = center.Y };
        var dpi = GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out var x, out _) == 0 ? x : 96u;
        var px = (int)Math.Round(Size * dpi / 96.0);
        return new Rect(p.X - px / 2, p.Y - px / 2, px, px);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
