using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Loop.Core.Animation;
using Loop.Core.Settings;
using Loop.Platform;
using Rect = Loop.Core.Geometry.Rect;

namespace Loop.App;

/// <summary>
/// The translucent "the window will go here" rectangle. Fades in and out, and glides between
/// targets while you cycle sizes or build a chord.
///
/// The overlay window covers the whole display and stays put; only the rectangle inside it moves.
/// Resizing a transparent WPF window every frame is slow and re-entrant (WPF renders, and fires the
/// next frame, inside the resize call), which would starve the UI thread. Moving an element inside
/// the window is cheap. The window itself moves only when the preview switches displays.
/// </summary>
internal sealed class PreviewOverlay : IPreviewOverlay
{
    private const int Inset = 4; // physical px, so the preview sits just inside the target

    private readonly ClickThroughWindow _window;
    private readonly Border _border;
    private readonly Stopwatch _clock = new();
    private AnimationOptions _animation = AnimationOptions.None;
    private Rect? _display;   // physical bounds the overlay window covers
    private double _scale = 1; // physical px per DIP on that display
    private Rect? _current;   // where the rectangle is drawn now (physical)
    private Rect _from, _to;
    private bool _gliding;

    public PreviewOverlay(string colorHex)
    {
        _border = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2) };
        var canvas = new Canvas();
        canvas.Children.Add(_border);
        _window = new ClickThroughWindow("Loop preview", canvas);
        SetColor(colorHex);
    }

    public void SetColor(string colorHex)
    {
        if (!LoopSettings.TryParseColor(colorHex, out var c)) return;
        _border.Background = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B));
        _border.BorderBrush = new SolidColorBrush(Color.FromArgb(0xD0, c.R, c.G, c.B));
    }

    public void SetAnimation(AnimationOptions options) => _animation = options;

    /// <inheritdoc cref="ClickThroughWindow.Prewarm"/>
    public void Prewarm() => _window.Prewarm();

    public void Show(Rect bounds, Rect displayBounds, bool animate)
    {
        if (bounds.Width <= Inset * 2 || bounds.Height <= Inset * 2) return;
        var r = new Rect(bounds.Left + Inset, bounds.Top + Inset, bounds.Width - Inset * 2, bounds.Height - Inset * 2);
        var fade = animate ? _animation.FadeDuration : TimeSpan.Zero;
        var appearing = !_window.IsVisible || _current is null;

        if (_display != displayBounds)
        {
            // Another display (or the first time): cover it, and jump there rather than glide.
            _window.EnsureShown(startTransparent: animate && appearing);
            _window.SetBounds(displayBounds);
            _display = displayBounds;
            _scale = _window.Scale;
            Jump(r);
            _window.Fade(1, fade);
            return;
        }

        if (appearing)
        {
            Jump(r);
            _window.EnsureShown(startTransparent: animate);
            _window.Fade(1, fade);
            return;
        }

        _window.Fade(1, fade); // in case it was fading out
        if (!animate || _animation.Duration <= TimeSpan.Zero)
        {
            Jump(r);
            return;
        }

        // Already showing: glide from wherever it is now (even mid-glide) to the new target.
        _from = _current!.Value;
        _to = r;
        _clock.Restart();
        if (!_gliding)
        {
            _gliding = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    public void Hide(bool animate)
    {
        StopGlide();
        if (animate) _window.FadeOutAndHide(_animation.FadeDuration, () => _current = null);
        else
        {
            _window.HideNow();
            _current = null;
        }
    }

    public void Close()
    {
        StopGlide();
        _window.Close();
    }

    private void Jump(Rect r)
    {
        StopGlide();
        Draw(r);
    }

    /// <summary>Positions the rectangle (physical screen pixels) inside the display-sized window.</summary>
    private void Draw(Rect r)
    {
        _current = r;
        if (_display is not { } d) return;
        Canvas.SetLeft(_border, (r.Left - d.Left) / _scale);
        Canvas.SetTop(_border, (r.Top - d.Top) / _scale);
        _border.Width = r.Width / _scale;
        _border.Height = r.Height / _scale;

        // Where the rectangle is, in physical pixels, for the smoke test.
        _window.SetProperty("Loop.Preview.Left", r.Left);
        _window.SetProperty("Loop.Preview.Top", r.Top);
        _window.SetProperty("Loop.Preview.Width", r.Width);
        _window.SetProperty("Loop.Preview.Height", r.Height);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var progress = RectTween.Progress(_clock.Elapsed, _animation.Duration, _animation.Easing);
        Draw(RectTween.At(_from, _to, progress));
        if (progress >= 1) StopGlide();
    }

    private void StopGlide()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnRendering;
    }
}
