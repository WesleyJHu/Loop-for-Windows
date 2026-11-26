using Loop.Core.Actions;
using Loop.Core.Input;
using Loop.Core.Settings;
using Loop.Platform.Input;
using Loop.Platform.Windows;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform;

/// <summary>
/// Wires the keyboard hook to the trigger state machine, the window manager, and the preview.
/// Construct and <see cref="Start"/> on the UI thread: the hook needs its message loop, hook
/// callbacks arrive on it, and window work runs on it (posted, so the hook returns immediately).
///
/// In <see cref="ApplyMode.OnRelease"/> mode, keys update a preview and the window moves when the
/// trigger is released (Esc cancels). In <see cref="ApplyMode.Instant"/> mode, each key applies at once.
/// </summary>
public sealed class LoopService : IDisposable
{
    private static readonly int[] ModifierVks =
    {
        VirtualKeys.LControl, VirtualKeys.RControl, VirtualKeys.LMenu, VirtualKeys.RMenu,
        VirtualKeys.LShift, VirtualKeys.RShift, VirtualKeys.LWin, VirtualKeys.RWin,
    };

    private readonly IPreviewOverlay? _overlay;
    private readonly KeyboardHook _hook;
    private TriggerController _controller = null!;
    private WindowManager _windows = null!;
    private ChordDebouncer<PendingAction> _debouncer = null!;
    private LoopSettings _settings = null!;
    private bool _enabled = true;
    private SynchronizationContext? _ui;

    private readonly record struct PendingAction(WindowAction Action, bool NewGesture);

    /// <param name="settings">Validated settings.</param>
    /// <param name="overlay">Draws the preview; null to never show one.</param>
    public LoopService(LoopSettings settings, IPreviewOverlay? overlay)
    {
        _overlay = overlay;
        _hook = new KeyboardHook(OnKey);
        ApplySettings(settings);
    }

    public LoopSettings Settings => _settings.Clone();

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _controller.Enabled = value;
            if (!value) Post(Cancel);
        }
    }

    /// <summary>Switches to new (validated) settings immediately. Call on the UI thread.</summary>
    public void ApplySettings(LoopSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(settings));

        _debouncer?.Cancel();
        if (_windows?.IsPreviewing == true) Cancel();

        _settings = settings.Clone();
        _controller = new TriggerController(settings.Trigger, settings.ToKeyBindings()) { Enabled = _enabled };
        var session = new LayoutSession(new CycleTracker(settings.ParseCycleSizes()), settings.Gap);
        _windows = new WindowManager(session, settings.ExcludedApps.ToList());
        _debouncer = new ChordDebouncer<PendingAction>(
            TimeSpan.FromMilliseconds(settings.ChordGraceMs),
            Schedule,
            fire: p => Post(() => Execute(p)),
            // A chord key arriving during the grace period replaces the pending action; the gesture
            // still started with the first key.
            merge: (pending, next) => next with { NewGesture = pending.NewGesture || next.NewGesture });
    }

    public void Start()
    {
        _ui = SynchronizationContext.Current
              ?? throw new InvalidOperationException("LoopService must be started on a UI thread.");
        _hook.Install();
    }

    public void Dispose()
    {
        _hook.Dispose();
        _debouncer.Cancel();
        _overlay?.Hide();
    }

    private bool OnKey(KeyEvent e)
    {
        var wasActive = _controller.IsTriggerActive;

        // Before acting on a non-modifier press, re-read the real modifier state so a missed
        // key-up (e.g. after Ctrl+Alt+Del) can't leave the trigger stuck on.
        if (e.IsDown && !VirtualKeys.IsModifier(e.VirtualKey))
            _controller.SyncModifiers(ModifierVks.Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0));

        var decision = _controller.Process(e);

        if (decision.SendMaskKey) InputInjector.SendMaskKey();

        if (decision.Cancel)
        {
            _debouncer.Cancel();
            Post(Cancel);
        }
        else if (decision.Action is { } action)
        {
            _debouncer.Submit(new PendingAction(action, decision.NewGesture), decision.IsChordKey);
        }

        if (wasActive && !_controller.IsTriggerActive)
        {
            _debouncer.Flush(); // a chord still in its grace period belongs to this activation
            Post(Commit);
        }

        return decision.Swallow;
    }

    private void Execute(PendingAction p)
    {
        Guard(p.Action.ToString(), () =>
        {
            if (_settings.ApplyMode == ApplyMode.Instant)
            {
                _windows.ApplyNow(p.Action, p.NewGesture);
                return;
            }

            var planned = _windows.Preview(p.Action, p.NewGesture);
            if (_overlay is null) return;
            if (planned is { Kind: not PlanKind.Minimize } move && _settings.ShowPreview) _overlay.Show(move.Target);
            else _overlay.Hide();
        });
    }

    private void Commit() => Guard("commit", () =>
    {
        _overlay?.Hide();
        if (_windows.IsPreviewing) _windows.CommitPreview();
    });

    private void Cancel() => Guard("cancel", () =>
    {
        _overlay?.Hide();
        _windows.CancelPreview();
    });

    private void Post(Action action) => _ui?.Post(_ => action(), null);

    /// <summary>Runs <paramref name="callback"/> on the UI thread after <paramref name="delay"/>.</summary>
    private IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var ui = _ui;
        return new Timer(_ => ui?.Post(_ => callback(), null), null, delay, Timeout.InfiniteTimeSpan);
    }

    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Write($"failed to {what}: {ex}");
        }
    }
}
