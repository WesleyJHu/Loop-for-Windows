using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Input;
using Loop.Core.Settings;
using Loop.Platform.Input;
using Loop.Platform.Windows;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform;

/// <summary>
/// Wires the keyboard hook to the trigger state machine, the window manager, and the preview.
///
/// Threads: the keyboard hook runs on its own thread (see <see cref="KeyboardHook"/>), where key
/// handling — the trigger state machine and the chord debouncer, guarded by <see cref="_keys"/> —
/// decides quickly and posts work to the UI thread. Everything that touches windows, the preview,
/// or settings runs on the UI thread. Construct and <see cref="Start"/> on the UI thread.
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

    /// <summary>Guards the key-handling state (<see cref="_controller"/>, <see cref="_debouncer"/>, <see cref="_enabled"/>).</summary>
    private readonly object _keys = new();
    private TriggerController _controller = null!;
    private ChordDebouncer<PendingAction> _debouncer = null!;
    private bool _enabled = true;

    // UI thread only.
    private WindowManager _windows = null!;
    private LoopSettings _settings = null!;
    private SynchronizationContext? _ui;

    private readonly record struct PendingAction(WindowAction Action, bool NewGesture);

    /// <param name="settings">Validated settings.</param>
    /// <param name="overlay">Draws the preview; null to never show one.</param>
    /// <param name="indicator">Draws the radial direction indicator; null to never show one.</param>
    /// <remarks>Construct on the UI thread.</remarks>
    public LoopService(LoopSettings settings, IPreviewOverlay? overlay, IDirectionIndicator? indicator = null)
    {
        _overlay = overlay;
        _indicator = indicator;
        _hook = new KeyboardHook(OnKey);
        ApplySettings(settings);
    }

    private readonly IDirectionIndicator? _indicator;

    /// <summary>Whether visuals (preview, indicator) should animate right now.</summary>
    private bool AnimateVisuals => MotionPolicy.ShouldAnimatePreview(_settings.ToAnimationOptions(), SystemAnimations.Enabled);

    public LoopSettings Settings => _settings.Clone();

    public bool Enabled
    {
        get { lock (_keys) return _enabled; }
        set
        {
            lock (_keys)
            {
                _enabled = value;
                _controller.Enabled = value;
                if (!value) _debouncer.Cancel();
            }
            if (!value) Post(Cancel);
        }
    }

    /// <summary>Switches to new (validated) settings immediately. Call on the UI thread.</summary>
    public void ApplySettings(LoopSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(settings));

        if (_windows?.IsPreviewing == true) Cancel();
        _windows?.FinishAnimation();

        _settings = settings.Clone();
        var session = new LayoutSession(new CycleTracker(settings.ParseCycleSizes()), settings.Gap);
        _windows = new WindowManager(session, settings.ExcludedApps.ToList(), settings.ToAnimationOptions());

        lock (_keys)
        {
            _debouncer?.Cancel();
            _controller = new TriggerController(settings.Trigger, settings.ToKeyBindings()) { Enabled = _enabled };
            _debouncer = new ChordDebouncer<PendingAction>(
                TimeSpan.FromMilliseconds(settings.ChordGraceMs),
                Schedule,
                fire: p => Post(() => Execute(p)),
                // A chord key arriving during the grace period replaces the pending action; the gesture
                // still started with the first key.
                merge: (pending, next) => next with { NewGesture = pending.NewGesture || next.NewGesture });
        }
    }

    public void Start()
    {
        _ui = SynchronizationContext.Current
              ?? throw new InvalidOperationException("LoopService must be started on a UI thread.");
        _hook.Start();
    }

    public void Dispose()
    {
        _hook.Dispose();
        lock (_keys) _debouncer.Cancel();
        _windows.FinishAnimation();
        _overlay?.Hide(animate: false);
        _indicator?.Hide(animate: false);
    }

    /// <summary>Runs on the hook thread for every key event. Must be quick: it only decides and posts.</summary>
    private bool OnKey(KeyEvent e)
    {
        lock (_keys) return HandleKey(e);
    }

    private bool HandleKey(KeyEvent e)
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
            var animate = AnimateVisuals;
            if (_settings.ApplyMode == ApplyMode.Instant)
            {
                if (_windows.ApplyNow(p.Action, p.NewGesture)) ShowIndicator(p.Action, animate);
                return;
            }

            var planned = _windows.Preview(p.Action, p.NewGesture);
            if (planned is null) return;
            ShowIndicator(p.Action, animate);
            if (_overlay is null) return;
            if (planned is { Kind: not PlanKind.Minimize } move && _settings.ShowPreview) _overlay.Show(move.Target, move.Display.Bounds, animate);
            else _overlay.Hide(animate);
        });
    }

    private void ShowIndicator(WindowAction action, bool animate)
    {
        if (_settings.ShowIndicator) _indicator?.Show(Indicator.SegmentFor(action), animate);
    }

    private void Commit() => Guard("commit", () =>
    {
        var animate = AnimateVisuals;
        _overlay?.Hide(animate);
        _indicator?.Hide(animate);
        if (_windows.IsPreviewing) _windows.CommitPreview();
    });

    private void Cancel() => Guard("cancel", () =>
    {
        var animate = AnimateVisuals;
        _overlay?.Hide(animate);
        _indicator?.Hide(animate);
        _windows.CancelPreview();
    });

    private void Post(Action action)
    {
        var queued = System.Diagnostics.Stopwatch.GetTimestamp();
        _ui?.Post(_ =>
        {
            var waited = System.Diagnostics.Stopwatch.GetElapsedTime(queued);
            if (waited > TimeSpan.FromMilliseconds(50)) Log.Write($"UI thread busy: work waited {waited.TotalMilliseconds:0} ms");
            action();
        }, null);
    }

    /// <summary>Runs a debouncer callback after <paramref name="delay"/>, under the key-handling lock.</summary>
    private IDisposable Schedule(TimeSpan delay, Action callback) =>
        new Timer(_ =>
        {
            lock (_keys) callback();
        }, null, delay, Timeout.InfiniteTimeSpan);

    private static void Guard(string what, Action action)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            action();
            var took = System.Diagnostics.Stopwatch.GetElapsedTime(start);
            if (took > TimeSpan.FromMilliseconds(30)) Log.Write($"{what} took {took.TotalMilliseconds:0} ms");
        }
        catch (Exception ex)
        {
            Log.Write($"failed to {what}: {ex}");
        }
    }
}
