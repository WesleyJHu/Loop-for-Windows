namespace Loop.Core.Actions;

public enum WindowAction
{
    /// <summary>Fill the monitor's work area.</summary>
    Maximize,
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    /// <summary>Center in the work area, keeping the current size (shrunk if it does not fit).</summary>
    Center,
    /// <summary>A corner-sized window (half width, half height) centered in the work area.</summary>
    CenterQuarter,
    Minimize,

    /// <summary>Move to the next display in left-to-right, top-to-bottom order (wraps around).</summary>
    NextDisplay,
    /// <summary>Move to the previous display (wraps around).</summary>
    PreviousDisplay,
    Display1,
    Display2,
    Display3,
    Display4,
    Display5,
    Display6,
    Display7,
    Display8,
    Display9,
}

public static class WindowActionExtensions
{
    /// <summary>True when the action places the window inside its current monitor's work area.</summary>
    public static bool IsLayout(this WindowAction action) =>
        action is not WindowAction.Minimize && !action.IsDisplayMove();

    public static bool IsDisplayMove(this WindowAction action) =>
        action is >= WindowAction.NextDisplay and <= WindowAction.Display9;

    /// <summary>For Display1..Display9, the zero-based display index; otherwise null.</summary>
    public static int? DisplayIndex(this WindowAction action) =>
        action is >= WindowAction.Display1 and <= WindowAction.Display9
            ? action - WindowAction.Display1
            : null;

    /// <summary>True for actions whose size cycles (1/2, 1/3, 2/3...) when repeated.</summary>
    public static bool Cycles(this WindowAction action) => action is
        WindowAction.LeftHalf or WindowAction.RightHalf or WindowAction.TopHalf or WindowAction.BottomHalf or
        WindowAction.TopLeft or WindowAction.TopRight or WindowAction.BottomLeft or WindowAction.BottomRight or
        WindowAction.CenterQuarter;
}
