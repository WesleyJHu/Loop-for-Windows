using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Loop.Core.Settings;
using Rect = Loop.Core.Geometry.Rect;
using Loop.Platform;

namespace Loop.App;

/// <summary>
/// The translucent "the window will go here" rectangle. A borderless, transparent, topmost WPF
/// window that never takes focus and lets clicks through. It is positioned with SetWindowPos in
/// physical pixels; its content stretches, so it looks right at any DPI.
/// </summary>
internal sealed class PreviewOverlay : IPreviewOverlay
{
    private const int Inset = 4; // physical px, so the preview sits just inside the target

    private readonly Window _window;
    private readonly Border _border;
    private IntPtr _hwnd;

    public PreviewOverlay(string colorHex)
    {
        _border = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
        };
        _window = new Window
        {
            Title = "Loop preview",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            Content = _border,
            Left = -10000, Top = -10000, Width = 1, Height = 1,
        };
        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            var ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
        SetColor(colorHex);
    }

    public void SetColor(string colorHex)
    {
        if (!LoopSettings.TryParseColor(colorHex, out var c)) return;
        _border.Background = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B));
        _border.BorderBrush = new SolidColorBrush(Color.FromArgb(0xD0, c.R, c.G, c.B));
    }

    public void Show(Rect bounds)
    {
        if (bounds.Width <= Inset * 2 || bounds.Height <= Inset * 2) return;
        if (!_window.IsVisible) _window.Show();

        var r = new Rect(bounds.Left + Inset, bounds.Top + Inset, bounds.Width - Inset * 2, bounds.Height - Inset * 2);
        // Crossing onto a monitor with another DPI makes WPF resize the window to its own idea of
        // the right size; a second call puts it back where we want it.
        for (var i = 0; i < 2; i++)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (GetWindowRect(_hwnd, out var actual) &&
                actual.Left == r.Left && actual.Top == r.Top && actual.Right == r.Right && actual.Bottom == r.Bottom)
                break;
        }
    }

    public void Hide()
    {
        if (_window.IsVisible) _window.Hide();
    }

    public void Close() => _window.Close();

    // ---- interop (the overlay's own few calls; the rest lives in Loop.Platform) ------------------

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
}
