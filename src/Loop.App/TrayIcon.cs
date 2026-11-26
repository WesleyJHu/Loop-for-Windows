using System;
using System.Drawing;
using System.Windows.Forms;
using Loop.Core.Input;

namespace Loop.App;

/// <summary>The notification-area icon and its context menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _header;
    private readonly ToolStripMenuItem _enabled;
    private readonly bool _elevated;
    private ModifierKeys _trigger;

    public TrayIcon(ModifierKeys trigger, bool elevated, Action<bool> onEnabledChanged, Action onSettings, Action onQuit)
    {
        _elevated = elevated;
        _header = new ToolStripMenuItem { Enabled = false };

        _enabled = new ToolStripMenuItem("Enabled") { Checked = true, CheckOnClick = true };
        _enabled.CheckedChanged += (_, _) =>
        {
            onEnabledChanged(_enabled.Checked);
            UpdateText();
        };

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => onSettings();

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => onQuit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { _header, new ToolStripSeparator(), _enabled, settings, quit });

        _icon = new NotifyIcon { Icon = LoadIcon(), ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => onSettings();

        SetTrigger(trigger);
    }

    /// <summary>Loop's icon at the size the notification area uses.</summary>
    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/loop.ico"));
        if (resource is null) return SystemIcons.Application;
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void SetTrigger(ModifierKeys trigger)
    {
        _trigger = trigger;
        UpdateText();
    }

    public void ShowWarning(string message) =>
        _icon.ShowBalloonTip(10_000, "Loop", message, ToolTipIcon.Warning);

    private void UpdateText()
    {
        var trigger = KeyNames.Format(_trigger);
        var admin = _elevated ? " (administrator)" : "";
        _header.Text = $"Loop — hold {trigger}{admin}";
        var text = _enabled.Checked ? $"Loop ({trigger}){admin}" : "Loop (disabled)";
        _icon.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon.Text is limited to 63 chars
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        if (!ReferenceEquals(_icon.Icon, SystemIcons.Application)) _icon.Icon?.Dispose(); // never dispose the shared fallback
        _icon.Dispose();
    }
}
