namespace Loop.Core.Actions;

/// <summary>
/// Turns the set of held direction keys into a window action.
///
/// | Held                      | Action                              |
/// |---------------------------|-------------------------------------|
/// | one arrow                 | that half                           |
/// | two perpendicular arrows  | that corner                         |
/// | left + right, or up + down| center (keep size)                  |
/// | three arrows              | the half named by the odd one out   |
/// | all four                  | corner-sized window, centered       |
/// </summary>
public static class ActionResolver
{
    public static WindowAction? Resolve(Direction held)
    {
        var horizontal = Axis(held.HasFlag(Direction.Left), held.HasFlag(Direction.Right));
        var vertical = Axis(held.HasFlag(Direction.Up), held.HasFlag(Direction.Down));

        return (horizontal, vertical) switch
        {
            (Span.None, Span.None) => null,

            // Opposite keys cancel out into "center".
            (Span.Both, Span.None) or (Span.None, Span.Both) => WindowAction.Center,
            (Span.Both, Span.Both) => WindowAction.CenterQuarter,

            // Halves. With three keys, the axis holding both is neutral and the odd key wins.
            (Span.First, Span.None or Span.Both) => WindowAction.LeftHalf,
            (Span.Second, Span.None or Span.Both) => WindowAction.RightHalf,
            (Span.None or Span.Both, Span.First) => WindowAction.TopHalf,
            (Span.None or Span.Both, Span.Second) => WindowAction.BottomHalf,

            // Corners.
            (Span.First, Span.First) => WindowAction.TopLeft,
            (Span.Second, Span.First) => WindowAction.TopRight,
            (Span.First, Span.Second) => WindowAction.BottomLeft,
            (Span.Second, Span.Second) => WindowAction.BottomRight,

            _ => null,
        };
    }

    private enum Span { None, First, Second, Both }

    private static Span Axis(bool first, bool second) => (first, second) switch
    {
        (true, true) => Span.Both,
        (true, false) => Span.First,
        (false, true) => Span.Second,
        _ => Span.None,
    };
}
