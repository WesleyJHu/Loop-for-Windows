namespace Loop.Core.Geometry;

/// <summary>An integer rectangle in physical screen pixels.</summary>
public readonly record struct Rect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public static Rect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public override string ToString() => $"({Left},{Top} {Width}x{Height})";
}
