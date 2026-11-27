using Loop.Core.Actions;
using Loop.Core.Animation;
using Loop.Core.Input;
using Loop.Core.Settings;
using Loop.Platform.Input;
using Loop.Platform.Windows;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform;

/// <summary>
/// Wires the input hooks to the trigger and mouse-menu state machines, the window manager, and the preview.
///
/// Threads: the hooks run on their own thread (see <see cref="InputHook"/>), where input
/// handling — the trigger state machine, the chord debouncer, and the mouse menu, guarded by
/// <see cref="_keys"/> — decides quickly and posts work to the UI thread. Everything that touches
/// windows, the preview, or settings runs on the UI thread. Construct and <see cref="Start"/> on the UI thread.
///
/// In <see cref="ApplyMode.OnRelease"/> mode, keys update a preview and the window moves when the
/// trigger is released (Esc cancels). In <see cref="ApplyMode.Instant"/> mode, each key applies at once.
///
/// The mouse's radial menu (see <see cref="MouseMenuController"/>) always previews, and applies on
/// release or click, to the window under the cursor where it opened.
/// </summary>
public sealed class LoopService : IDisposable
{
    private static readonly int[] ModifierVks =
    {
        VirtualKeys.LControl, VirtualKeys.RControl, VirtualKeys.LMenu, VirtualKeys.RMenu,
        VirtualKeys.LShift, VirtualKeys.RShift, VirtualKeys.LWin, VirtualKeys.RWin,
    };

    private readonly IPreviewOverlay? _overlay;
    private readonly InputHook _hook;

    /// <summary>The radius of the mouse menu's center, in DIPs (between the indicator's center dot and its ring).</summary>
    private const double MouseDeadZoneDips = 28;

    /// <summary>
    /// Guards the input-handling state (<see cref="_controller"/>, <see cref="_debouncer"/>,
    /// <see cref="_mouse"/>, <see cref="_enabled"/>, <see cref="_swallowEscapeUp"/>).
    /// </summary>
    private readonly object _keys = new();
    private TriggerController _controller = null!;
    private ChordDebouncer<PendingAction> _debouncer = null!;
    private readonly MouseMenuController _mouse;
    private bool _enabled = true;
    private bool _swallowEscapeUp;

    // UI thread only.
    private WindowManager _windows = null!;
    private LoopSettings _settings = null!;
    private SynchronizationContext? _ui;
    private IntPtr _mouseTarget;            // the window the open mouse menu acts on
    private WindowAction? _mouseAction;     // what the mouse menu is previewing

    private readonly record struct PendingAction(WindowAction Action, bool NewGesture);

    /// <param name="settings">Validated settings.</param>
    /// <param name="overlay">Draws the preview; null to never show one.</param>
    /// <param name="indicator">Draws the radial direction indicator; null to never show one.</param>
    /// <remarks>Construct on the UI thread.</remarks>
    public LoopService(LoopSettings settings, IPreviewOverlay? overlay, IDirectionIndicator? indicator = null)
    {
        _overlay = overlay;
        _indicator = indicator;
        _hook = new InputHook(OnKey, OnMouse);
        _mouse = new MouseMenuController(DeadZoneAt, Schedule, InputInjector.SendMouseButtons);
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
                if (!value)
                {
                    _debouncer.Cancel();
                    _mouse.Reset();
                }
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
            if (_mouse.Configure(settings.MouseTrigger, TimeSpan.FromMilliseconds(settings.MouseDoubleClickMs))) Post(Cancel);
        }
        _mouseTarget = IntPtr.Zero;
        _hook.MouseEnabled = settings.MouseTrigger != MouseTrigger.Off;
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
        lock (_keys)
        {
            _debouncer.Cancel();
            _mouse.Reset(); // lets a held-back middle click through
        }
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
        // Esc closes the mouse menu.
        if (e.VirtualKey == VirtualKeys.Escape)
        {
            if (e.IsDown && _mouse.CancelMenu())
            {
                _swallowEscapeUp = true;
                Post(Cancel);
                return true;
            }
            if (!e.IsDown && _swallowEscapeUp)
            {
                _swallowEscapeUp = false;
                return true;
            }
        }

        var wasActive = _controller.IsTriggerActive;

        // Before acting on a non-modifier press, re-read the real modifier state so a missed
        // key-up (e.g. after Ctrl+Alt+Del) can't leave the trigger stuck on.
        if (e.IsDown && !VirtualKeys.IsModifier(e.VirtualKey))
            _controller.SyncModifiers(ModifierVks.Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0));

        var decision = _controller.Process(e);

        if (decision.SendMaskKey) InputInjector.SendMaskKey();

        // The keyboard trigger takes over from an open mouse menu (its release would otherwise
        // commit the mouse's preview).
        if (_controller.IsTriggerActive && _mouse.CancelMenu()) Post(Cancel);

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

    /// <summary>Runs on the hook thread for every mouse event (including every move). Must be very quick.</summary>
    private bool OnMouse(MouseEvent e)
    {
        lock (_keys)
        {
            var d = _mouse.Process(e, canOpen: _enabled && !_controller.IsTriggerActive);
            switch (d.Effect)
            {
                case MouseMenuEffect.Open: Post(() => MouseOpen(d.X, d.Y)); break;
                case MouseMenuEffect.Select: Post(() => MouseSelect(d.Segment)); break;
                case MouseMenuEffect.Cycle: Post(MouseCycle); break;
                case MouseMenuEffect.Commit: Post(Commit); break;
                case MouseMenuEffect.Cancel: Post(Cancel); break;
            }
            return d.Swallow;
        }
    }

    private void MouseOpen(int x, int y) => Guard("open the mouse menu", () =>
    {
        if (_windows.IsPreviewing)
        {
            _windows.CancelPreview();
            _overlay?.Hide(AnimateVisuals);
        }

        _mouseAction = null;
        _mouseTarget = WindowManager.GetWindowAt(x, y);
        if (_mouseTarget == IntPtr.Zero)
        {
            // Nothing Loop can move here (the desktop, the taskbar): don't show a menu that does nothing.
            lock (_keys) _mouse.CancelMenu();
            return;
        }
        _indicator?.Show(IndicatorSegment.None, AnimateVisuals, (x, y));
    });

    private void MouseSelect(IndicatorSegment segment) => Guard("select in the mouse menu", () =>
    {
        if (_mouseTarget == IntPtr.Zero) return; // the menu was closed

        // Each selection is planned from where the window really is, so moving the cursor from
        // left to top and back previews the left half again rather than cycling it.
        _windows.CancelPreview();
        _mouseAction = Indicator.ActionFor(segment);
        var animate = AnimateVisuals;
        _indicator?.Show(segment, animate);
        if (_mouseAction is { } action) ShowPreview(_windows.Preview(action, newGesture: true, _mouseTarget), animate);
        else _overlay?.Hide(animate);
    });

    /// <summary>The wheel steps the selected layout through its cycle sizes, like pressing its key again.</summary>
    private void MouseCycle() => Guard("cycle in the mouse menu", () =>
    {
        if (_mouseTarget == IntPtr.Zero || _mouseAction is not { } action) return;
        ShowPreview(_windows.Preview(action, newGesture: true, _mouseTarget), AnimateVisuals);
    });

    private void ShowPreview(PlannedAction? planned, bool animate)
    {
        if (_overlay is null || planned is null) return;
        if (planned is { Kind: not PlanKind.Minimize } move && _settings.ShowPreview) _overlay.Show(move.Target, move.Display.Bounds, animate);
        else _overlay.Hide(animate);
    }

    /// <summary>The mouse menu's center radius in physical pixels at a point.</summary>
    private static double DeadZoneAt(double x, double y)
    {
        var monitor = MonitorFromPoint(new POINT { X = (int)x, Y = (int)y }, MONITOR_DEFAULTTONEAREST);
        var dpi = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96u;
        return MouseDeadZoneDips * dpi / 96.0;
    }

    private void Execute(PendingAction p)
    {
        Guard(p.Action.ToString(), () =>
        {
            var animate = AnimateVisuals;
            if (p.Action == WindowAction.Undo)
            {
                // A preview not yet applied is the latest thing to take back; otherwise undo the
                // last applied action right away (in either apply mode).
                if (_windows.HasPendingPreview)
                {
                    _windows.CancelPreview();
                    _overlay?.Hide(animate);
                    _indicator?.Hide(animate);
                }
                else _windows.Undo();
                return;
            }

            if (_settings.ApplyMode == ApplyMode.Instant)
            {
                if (_windows.ApplyNow(p.Action, p.NewGesture)) ShowIndicator(p.Action, animate);
                return;
            }

            var planned = _windows.Preview(p.Action, p.NewGesture);
            if (planned is null) return;
            ShowIndicator(p.Action, animate);
            ShowPreview(planned, animate);
        });
    }

    private void ShowIndicator(WindowAction action, bool animate)
    {
        if (_settings.ShowIndicator) _indicator?.Show(Indicator.SegmentFor(action), animate);
    }

    private void Commit() => Guard("commit", () =>
    {
        _mouseTarget = IntPtr.Zero;
        var animate = AnimateVisuals;
        _overlay?.Hide(animate);
        _indicator?.Hide(animate);
        if (_windows.IsPreviewing) _windows.CommitPreview();
    });

    private void Cancel() => Guard("cancel", () =>
    {
        _mouseTarget = IntPtr.Zero;
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

    /// <summary>Runs a debouncer or mouse-menu callback after <paramref name="delay"/>, under the input-handling lock.</summary>
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
