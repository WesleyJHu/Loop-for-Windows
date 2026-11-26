using Loop.Core.Actions;
using Loop.Core.Input;
using Loop.Platform.Input;
using Loop.Platform.Windows;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform;

/// <summary>
/// Wires the keyboard hook to the trigger state machine and the window manager.
/// Construct and <see cref="Start"/> on the UI thread: the hook needs its message loop, and
/// window actions are posted back to it so the hook callback returns immediately.
/// </summary>
public sealed class LoopService : IDisposable
{
    private static readonly int[] ModifierVks =
    {
        VirtualKeys.LControl, VirtualKeys.RControl, VirtualKeys.LMenu, VirtualKeys.RMenu,
        VirtualKeys.LShift, VirtualKeys.RShift, VirtualKeys.LWin, VirtualKeys.RWin,
    };

    private readonly TriggerController _controller;
    private readonly WindowManager _windows = new();
    private readonly KeyboardHook _hook;
    private SynchronizationContext? _ui;

    public LoopService(TriggerController controller)
    {
        _controller = controller;
        _hook = new KeyboardHook(OnKey);
    }

    public bool Enabled
    {
        get => _controller.Enabled;
        set => _controller.Enabled = value;
    }

    public void Start()
    {
        _ui = SynchronizationContext.Current
              ?? throw new InvalidOperationException("LoopService must be started on a UI thread.");
        _hook.Install();
    }

    public void Dispose() => _hook.Dispose();

    private bool OnKey(KeyEvent e)
    {
        // Before acting on a non-modifier press, re-read the real modifier state so a missed
        // key-up (e.g. after Ctrl+Alt+Del) can't leave the trigger stuck on.
        if (e.IsDown && !VirtualKeys.IsModifier(e.VirtualKey))
            _controller.SyncModifiers(ModifierVks.Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0));

        var decision = _controller.Process(e);

        if (decision.SendMaskKey) InputInjector.SendMaskKey();
        if (decision.Action is { } action)
        {
            var newGesture = decision.NewGesture;
            _ui?.Post(_ => Execute(action, newGesture), null);
        }

        return decision.Swallow;
    }

    private void Execute(WindowAction action, bool newGesture)
    {
        try
        {
            _windows.TryApply(action, newGesture);
        }
        catch (Exception ex)
        {
            Log.Write($"failed to apply {action}: {ex}");
        }
    }
}
