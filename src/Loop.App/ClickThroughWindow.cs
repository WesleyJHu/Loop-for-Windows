using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Rect = Loop.Core.Geometry.Rect;

namespace Loop.App;

/// <summary>
/// A borderless, transparent, topmost WPF window that never takes focus and lets every click
/// through. Positioned in physical pixels with SetWindowPos; its content should stretch, so it
/// looks right at any DPI. Shared by the preview overlay and the direction indicator.
/// </summary>
internal sealed class ClickThroughWindow
{
    private readonly Window _window;
    private IntPtr _hwnd;
    private int _fadeGeneration;

    public ClickThroughWindow(string title, UIElement content)
    {
        _window = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            Content = content,
            Left = -10000, Top = -10000, Width = 1, Height = 1,
        };
        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            var ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public bool IsVisible => _window.IsVisible;

    /// <summary>
    /// Publishes a number as a window property (readable from other processes with GetProp), so
    /// tests can see what the window draws. Costs no messages, so it's fine to call every frame.
    /// </summary>
    public void SetProperty(string name, int value)
    {
        if (_hwnd != IntPtr.Zero) SetProp(_hwnd, name, new IntPtr(value));
    }

    /// <summary>Physical pixels per DIP at the window's current DPI.</summary>
    public double Scale => _hwnd != IntPtr.Zero && GetDpiForWindow(_hwnd) is var dpi and > 0 ? dpi / 96.0 : 1.0;

    /// <summary>
    /// Creates the native window and its rendering surface now (shown fully transparent, then
    /// hidden), so the first real show doesn't pay for it while the user is waiting.
    /// </summary>
    public void Prewarm()
    {
        if (_window.IsVisible || _hwnd != IntPtr.Zero) return;
        _window.Opacity = 0;
        _window.Show();
        _window.Hide();
    }

    /// <summary>Shows the window (fully transparent if it will fade in) without activating it.</summary>
    public void EnsureShown(bool startTransparent)
    {
        if (_window.IsVisible) return;
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        _window.Opacity = startTransparent ? 0 : 1;
        _window.Show();
    }

    /// <summary>Moves and sizes the window in physical screen pixels.</summary>
    public void SetBounds(Rect r)
    {
        if (_hwnd == IntPtr.Zero || r.IsEmpty) return;
        // Crossing onto a monitor with another DPI makes WPF resize the window to its own idea of
        // the right size; a second call puts it back where we want it.
        for (var i = 0; i < 2; i++)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (GetWindowRect(_hwnd, out var a) && a.Left == r.Left && a.Top == r.Top && a.Right == r.Right && a.Bottom == r.Bottom)
                break;
        }
    }

    /// <summary>Fades opacity to <paramref name="to"/>; <paramref name="done"/> runs when it gets there (unless superseded).</summary>
    public void Fade(double to, TimeSpan duration, Action? done = null)
    {
        var generation = ++_fadeGeneration;
        if (duration <= TimeSpan.Zero)
        {
            _window.BeginAnimation(UIElement.OpacityProperty, null);
            _window.Opacity = to;
            done?.Invoke();
            return;
        }

        // No From: starts at the current (possibly mid-fade) opacity, so reversing a fade is smooth.
        var animation = new DoubleAnimation(to, new Duration(duration)) { FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) =>
        {
            if (generation == _fadeGeneration) done?.Invoke();
        };
        _window.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>Hides immediately, cancelling any fade.</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        if (_window.IsVisible) _window.Hide();
    }

    /// <summary>Fades out (or hides at once when <paramref name="fade"/> is zero), then calls <paramref name="hidden"/>.</summary>
    public void FadeOutAndHide(TimeSpan fade, Action? hidden = null)
    {
        if (!_window.IsVisible) { hidden?.Invoke(); return; }
        Fade(0, fade, () =>
        {
            if (_window.IsVisible) _window.Hide();
            hidden?.Invoke();
        });
    }

    public void Close() => _window.Close();

    // ---- interop ---------------------------------------------------------------------------------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(IntPtr hWnd, string name, IntPtr data);
}
