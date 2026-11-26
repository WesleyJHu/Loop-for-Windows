using Loop.Core.Actions;

namespace Loop.Core.Input;

/// <summary>Which keys (pressed while the trigger is held) map to which directions and actions.</summary>
public sealed class KeyBindings
{
    public IReadOnlyDictionary<int, Direction> DirectionKeys { get; }
    public IReadOnlyDictionary<int, WindowAction> ActionKeys { get; }

    public KeyBindings(IReadOnlyDictionary<int, Direction> directionKeys, IReadOnlyDictionary<int, WindowAction> actionKeys)
    {
        foreach (var vk in directionKeys.Keys.Concat(actionKeys.Keys))
        {
            if (VirtualKeys.IsModifier(vk))
                throw new ArgumentException($"Modifier key 0x{vk:X2} cannot be bound to an action.");
        }

        var overlap = directionKeys.Keys.Intersect(actionKeys.Keys).ToList();
        if (overlap.Count > 0)
            throw new ArgumentException($"Key 0x{overlap[0]:X2} is bound to both a direction and an action.");

        DirectionKeys = directionKeys;
        ActionKeys = actionKeys;
    }

    public static KeyBindings Default { get; } = CreateDefault();

    private static KeyBindings CreateDefault()
    {
        var actions = new Dictionary<int, WindowAction>
        {
            [VirtualKeys.Enter] = WindowAction.Maximize,
            [VirtualKeys.C] = WindowAction.Center,
            [VirtualKeys.M] = WindowAction.Minimize,
            [VirtualKeys.Backspace] = WindowAction.RestorePrevious,
            [VirtualKeys.CloseBracket] = WindowAction.NextDisplay,
            [VirtualKeys.OpenBracket] = WindowAction.PreviousDisplay,
        };
        for (var vk = VirtualKeys.D1; vk <= VirtualKeys.D9; vk++)
            actions[vk] = WindowAction.Display1 + (vk - VirtualKeys.D1);

        return new KeyBindings(
            new Dictionary<int, Direction>
            {
                [VirtualKeys.Left] = Direction.Left,
                [VirtualKeys.Right] = Direction.Right,
                [VirtualKeys.Up] = Direction.Up,
                [VirtualKeys.Down] = Direction.Down,
            },
            actions);
    }
}
