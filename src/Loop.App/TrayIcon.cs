using System;
using System.Drawing;
using System.Windows.Forms;
using Loop.Core.Input;
using Loop.Platform;

namespace Loop.App;

/// <summary>The notification-area icon and its context menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(LoopService loop, ModifierKeys trigger, Action onQuit)
    {
        var triggerText = FormatTrigger(trigger);

        var header = new ToolStripMenuItem($"Loop — hold {triggerText}") { Enabled = false };

        var enabled = new ToolStripMenuItem("Enabled") { Checked = loop.Enabled, CheckOnClick = true };
        enabled.CheckedChanged += (_, _) =>
        {
            loop.Enabled = enabled.Checked;
            _icon!.Text = loop.Enabled ? $"Loop ({triggerText})" : "Loop (disabled)";
        };

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => onQuit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { header, new ToolStripSeparator(), enabled, quit });

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"Loop ({triggerText})",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private static string FormatTrigger(ModifierKeys trigger)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (trigger.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
        if (trigger.HasFlag(ModifierKeys.Win)) parts.Add("Win");
        if (trigger.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (trigger.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        return string.Join("+", parts);
    }
}
