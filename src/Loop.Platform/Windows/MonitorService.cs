using System.Runtime.InteropServices;
using Loop.Core.Geometry;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Windows;

/// <summary>
/// Enumerates displays. Queried fresh for every activation (it takes microseconds), so plugging or
/// unplugging monitors, changing resolution, or moving the taskbar needs no change notifications.
/// </summary>
public static class MonitorService
{
    /// <summary>All displays, ordered left-to-right then top-to-bottom (display 1 is the leftmost).</summary>
    public static IReadOnlyList<DisplayArea> GetDisplays()
    {
        var displays = new List<DisplayArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(handle, ref info))
                displays.Add(new DisplayArea(handle, ToRect(info.rcMonitor), ToRect(info.rcWork)));
            return true;
        }, IntPtr.Zero);
        return DisplayOrder.Sort(displays, d => d.Bounds);
    }

    private static Rect ToRect(RECT r) => Rect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
