using Loop.Core.Actions;

namespace Loop.Core.Input;

/// <summary>A key transition reported by the keyboard hook.</summary>
public readonly record struct KeyEvent(int VirtualKey, bool IsDown);

/// <summary>What the hook should do with a key event.</summary>
/// <param name="Swallow">Block the key so the focused app and the shell never see it.</param>
/// <param name="Action">An action to perform (asynchronously, off the hook callback), if any.</param>
/// <param name="SendMaskKey">Inject <see cref="VirtualKeys.MaskKey"/> so releasing Win/Alt does nothing.</param>
/// <param name="NewGesture">
/// True for the first key of a gesture (no direction was held before it). Later keys of the same
/// chord refine the gesture, so their action must be computed from the window's rect as it was
/// when the gesture started. Otherwise Left then Right would apply "full width" to a window that
/// Left already made full height.
/// </param>
public readonly record struct KeyDecision(bool Swallow, WindowAction? Action = null, bool SendMaskKey = false, bool NewGesture = false)
{
    public static KeyDecision PassThrough => default;
}

/// <summary>
/// The keyboard state machine. It is fed every key event from the low-level hook and decides
/// whether to swallow it and which window action (if any) it triggers.
///
/// Phase 1 semantics ("instant mode"): while exactly the trigger modifiers are held, every new
/// press of a bound key applies immediately. Direction keys combine: the action is resolved
/// from all direction keys currently held, so pressing Up while Left is held gives TopLeft.
/// </summary>
public sealed class TriggerController
{
    private readonly HashSet<int> _modifiersDown = new();
    private readonly HashSet<int> _swallowed = new();
    private Direction _heldDirections;

    public TriggerController(ModifierKeys trigger, KeyBindings bindings)
    {
        if (trigger == ModifierKeys.None)
            throw new ArgumentException("The trigger needs at least one modifier.", nameof(trigger));
        Trigger = trigger;
        Bindings = bindings;
    }

    public ModifierKeys Trigger { get; }
    public KeyBindings Bindings { get; }

    /// <summary>When false, no new keys are swallowed (keys already swallowed still have their key-up swallowed).</summary>
    public bool Enabled { get; set; } = true;

    public ModifierKeys CurrentModifiers
    {
        get
        {
            var mods = ModifierKeys.None;
            foreach (var vk in _modifiersDown) mods |= VirtualKeys.ToModifier(vk);
            return mods;
        }
    }

    /// <summary>True when exactly the trigger modifiers are held (extra modifiers disable it).</summary>
    public bool IsTriggerActive => Enabled && CurrentModifiers == Trigger;

    /// <summary>
    /// Replace the tracked modifier state with the real one. Key-ups can be missed (for example,
    /// Ctrl+Alt+Del switches to the secure desktop), which would leave a modifier stuck "down";
    /// the platform layer calls this with GetAsyncKeyState results to self-heal.
    /// </summary>
    public void SyncModifiers(IEnumerable<int> modifierKeysDown)
    {
        _modifiersDown.Clear();
        foreach (var vk in modifierKeysDown)
        {
            if (VirtualKeys.IsModifier(vk)) _modifiersDown.Add(vk);
        }
        if (!IsTriggerActive) _heldDirections = Direction.None;
    }

    public KeyDecision Process(KeyEvent e)
    {
        var vk = e.VirtualKey;

        if (VirtualKeys.IsModifier(vk))
        {
            if (e.IsDown) _modifiersDown.Add(vk);
            else _modifiersDown.Remove(vk);
            if (!IsTriggerActive) _heldDirections = Direction.None;
            return KeyDecision.PassThrough; // never swallow modifiers: apps must see them released
        }

        if (!e.IsDown)
        {
            if (!_swallowed.Remove(vk)) return KeyDecision.PassThrough;
            if (Bindings.DirectionKeys.TryGetValue(vk, out var released)) _heldDirections &= ~released;
            return new KeyDecision(Swallow: true);
        }

        // Auto-repeat of a key whose press we already consumed: keep swallowing, do nothing.
        if (_swallowed.Contains(vk)) return new KeyDecision(Swallow: true);

        if (!IsTriggerActive) return KeyDecision.PassThrough;

        WindowAction? action;
        bool newGesture;
        if (Bindings.DirectionKeys.TryGetValue(vk, out var direction))
        {
            newGesture = _heldDirections == Direction.None;
            _heldDirections |= direction;
            action = ActionResolver.Resolve(_heldDirections);
        }
        else if (Bindings.ActionKeys.TryGetValue(vk, out var bound))
        {
            newGesture = true;
            action = bound;
        }
        else
        {
            return KeyDecision.PassThrough; // unbound keys keep working normally
        }

        _swallowed.Add(vk);
        var mask = (Trigger & (ModifierKeys.Win | ModifierKeys.Alt)) != 0;
        return new KeyDecision(Swallow: true, action, mask, newGesture);
    }
}
