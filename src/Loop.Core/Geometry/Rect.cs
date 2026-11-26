namespace Loop.Core.Geometry;

/// <summary>An integer rectangle in physical screen pixels.</summary>
public readonly record struct Rect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>The overlapping region, or an empty rect if they do not overlap.</summary>
    public Rect Intersect(Rect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromEdges(left, top, right, bottom) : default;
    }

    public bool Contains(Rect other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    public override string ToString() => $"({Left},{Top} {Width}x{Height})";
}
