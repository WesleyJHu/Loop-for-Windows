namespace Loop.Core.Actions;

/// <summary>Direction keys currently held while the trigger is active. Combinable.</summary>
[Flags]
public enum Direction
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Up = 1 << 2,
    Down = 1 << 3,
}
