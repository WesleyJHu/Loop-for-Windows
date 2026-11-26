namespace Loop.Core.Actions;

/// <summary>
/// Turns the set of held direction keys into a window action.
/// Each axis is resolved independently:
///   one key: that half;  both keys: the full span of that axis;  no key: unconstrained.
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

            // Opposite keys on one axis stretch that axis only.
            (Span.Both, Span.None) => WindowAction.MaximizeWidth,
            (Span.None, Span.Both) => WindowAction.MaximizeHeight,
            (Span.Both, Span.Both) => WindowAction.Maximize,

            // Halves ("Both" on the other axis is the same as its full span).
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
