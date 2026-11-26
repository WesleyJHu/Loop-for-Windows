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
    private ModifierKeys _trigger;

    public TrayIcon(ModifierKeys trigger, Action<bool> onEnabledChanged, Action onSettings, Action onQuit)
    {
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

        _icon = new NotifyIcon { Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => onSettings();

        SetTrigger(trigger);
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
        _header.Text = $"Loop — hold {trigger}";
        var text = _enabled.Checked ? $"Loop ({trigger})" : "Loop (disabled)";
        _icon.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon.Text is limited to 63 chars
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
