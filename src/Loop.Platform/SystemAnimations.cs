using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform;

/// <summary>Windows' own "Animation effects" setting (Settings &gt; Accessibility &gt; Visual effects).</summary>
public static class SystemAnimations
{
    /// <summary>True unless the user turned animation effects off. Read fresh each time (it's cheap).</summary>
    public static bool Enabled => !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var on, 0) || on;
}
