using Loop.Core.Geometry;

namespace Loop.Platform;

/// <summary>Draws the "this is where the window will go" preview. Implemented by the UI layer.</summary>
public interface IPreviewOverlay
{
    /// <summary>Shows (or moves) the preview over <paramref name="bounds"/>, in physical screen pixels.</summary>
    void Show(Rect bounds);

    void Hide();
}
