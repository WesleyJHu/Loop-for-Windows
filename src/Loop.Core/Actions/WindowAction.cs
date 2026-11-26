namespace Loop.Core.Actions;

public enum WindowAction
{
    /// <summary>Fill the monitor's work area.</summary>
    Maximize,
    /// <summary>Stretch to the full work-area width; keep the current vertical position and height.</summary>
    MaximizeWidth,
    /// <summary>Stretch to the full work-area height; keep the current horizontal position and width.</summary>
    MaximizeHeight,
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
    Minimize,
}

public static class WindowActionExtensions
{
    /// <summary>True when the action produces a target rectangle (everything but Minimize).</summary>
    public static bool IsGeometric(this WindowAction action) => action != WindowAction.Minimize;
}
