using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Geometry;

namespace Loop.Platform;

/// <summary>Draws the "this is where the window will go" preview. Implemented by the UI layer.</summary>
public interface IPreviewOverlay
{
    /// <summary>
    /// Shows the preview over <paramref name="bounds"/> (physical pixels) on the display whose
    /// bounds are <paramref name="displayBounds"/>, gliding there if already visible on that display.
    /// </summary>
    void Show(Rect bounds, Rect displayBounds, bool animate);

    void Hide(bool animate);
}

/// <summary>The radial direction indicator shown near the cursor while the trigger is held.</summary>
public interface IDirectionIndicator
{
    /// <summary>Shows the indicator (at the cursor, if it wasn't already showing) highlighting <paramref name="segment"/>.</summary>
    void Show(IndicatorSegment segment, bool animate);

    void Hide(bool animate);
}
