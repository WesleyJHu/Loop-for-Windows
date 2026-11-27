using Loop.Core.Animation;

namespace Loop.Core.Input;

public enum MouseButton { Left, Right, Middle, X1, X2 }

/// <summary>What opens the radial menu with the mouse.</summary>
public enum MouseTrigger
{
    Off,
    /// <summary>Press the middle button (the scroll wheel) twice quickly.</summary>
    MiddleDoubleClick,
    /// <summary>Press the back side button (XButton1).</summary>
    BackButton,
    /// <summary>Press the forward side button (XButton2).</summary>
    ForwardButton,
}

public enum MouseEventKind { Move, Down, Up, Wheel }

/// <summary>A mouse event reported by the low-level mouse hook. Coordinates are physical pixels.</summary>
/// <param name="Time">The event's timestamp in milliseconds (GetTickCount; wraps around).</param>
/// <param name="Injected">True for an event Loop injected itself (a replayed click).</param>
public readonly record struct MouseEvent(
    MouseEventKind Kind,
    int X,
    int Y,
    uint Time,
    MouseButton Button = MouseButton.Left,
    bool Injected = false);

public enum MouseMenuEffect
{
    None,
    /// <summary>Show the menu centered on (<see cref="MouseDecision.X"/>, <see cref="MouseDecision.Y"/>), nothing selected.</summary>
    Open,
    /// <summary>The selection changed to <see cref="MouseDecision.Segment"/> (None: nothing selected).</summary>
    Select,
    /// <summary>Step the selected layout to its next cycle size.</summary>
    Cycle,
    /// <summary>Apply the selection and close the menu.</summary>
    Commit,
    /// <summary>Close the menu without doing anything.</summary>
    Cancel,
}

/// <summary>What the hook should do with a mouse event, and what it means for the menu.</summary>
public readonly record struct MouseDecision(
    bool Swallow,
    MouseMenuEffect Effect = MouseMenuEffect.None,
    IndicatorSegment Segment = IndicatorSegment.None,
    int X = 0,
    int Y = 0)
{
    public static MouseDecision PassThrough => default;
    public static MouseDecision Swallowed => new(Swallow: true);
}

/// <summary>
/// The mouse state machine for the radial menu. Fed every event from the low-level mouse hook,
/// it decides which to swallow and what the menu does.
///
/// Opening: with <see cref="MouseTrigger.MiddleDoubleClick"/>, a middle press is held back until
/// it's clear whether a second one follows. A second press within the double-click time (and
/// close by) opens the menu, and the app under the cursor never sees either click. Otherwise the
/// first click is replayed (injected), so a lone middle click still works, only a little later.
/// It is replayed early when the cursor moves away or another button is used, so middle-drags
/// (panning) start promptly. A side-button trigger opens the menu on a single press.
///
/// Choosing: the direction from the menu's center to the cursor selects one of eight segments;
/// near the center selects the center (maximize). There are two ways to use it:
/// <list type="bullet">
/// <item>Hold the opening press, move toward a direction, and release to apply it.</item>
/// <item>Release without moving: the menu stays open; move to a segment and click (left or the
/// trigger button) to apply it.</item>
/// </list>
/// While held, nothing is selected until the cursor first leaves the center, so a quick double
/// click doesn't maximize. Right-click (or any other button) cancels; the wheel cycles sizes.
/// Every press the menu swallows has its release swallowed too.
///
/// Replayed clicks are injected, and a real event for the same button could reach the hook before
/// the injected ones do. Until the hook has seen them, real events for that button are swallowed
/// and injected again behind them, so apps always see presses and releases in order.
/// </summary>
public sealed class MouseMenuController
{
    /// <summary>How far (px) the cursor may move between the presses of a double click.</summary>
    public const int DoubleClickDistance = 10;

    /// <summary>After this long without seeing an injected event, stop waiting for it (it was lost).</summary>
    private const uint InFlightTimeoutMs = 1000;

    private readonly Func<double, double, double> _deadZoneAt;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly Func<IReadOnlyList<(MouseButton Button, bool Down)>, bool> _inject;
    private readonly Func<uint> _clock;

    private MouseTrigger _trigger = MouseTrigger.Off;
    private TimeSpan _doubleClick = TimeSpan.FromMilliseconds(200);

    private readonly HashSet<MouseButton> _swallowUp = new();
    private readonly Dictionary<MouseButton, int> _inFlight = new();
    private uint _lastInjected;

    // A held-back first middle click.
    private bool _pending;
    private bool _pendingReleased;
    private int _pendingX, _pendingY;
    private uint _pendingTime;
    private IDisposable? _timer;
    private int _timerGeneration;

    // The open menu.
    private bool _holding;          // the opening press is still held
    private bool _leftCenter;       // the cursor has left the center since the menu opened
    private bool _byKeyboard;       // opened by holding the keyboard trigger (its release applies)
    private IndicatorSegment? _keyGuard; // after a key chose: the cursor's segment then, which doesn't override it
    private int _anchorX, _anchorY, _lastX, _lastY;
    private double _deadZone;

    /// <param name="deadZoneAt">The center's radius in physical pixels for a menu opened at (x, y).</param>
    /// <param name="schedule">Runs a callback after a delay (on any thread, under the caller's lock); dispose to cancel.</param>
    /// <param name="inject">Injects button presses (true) and releases in order; returns false if that failed.</param>
    /// <param name="clock">The current time in GetTickCount milliseconds.</param>
    public MouseMenuController(
        Func<double, double, double> deadZoneAt,
        Func<TimeSpan, Action, IDisposable> schedule,
        Func<IReadOnlyList<(MouseButton Button, bool Down)>, bool> inject,
        Func<uint>? clock = null)
    {
        _deadZoneAt = deadZoneAt;
        _schedule = schedule;
        _inject = inject;
        _clock = clock ?? (() => unchecked((uint)Environment.TickCount));
    }

    public MouseTrigger Trigger => _trigger;

    public bool IsOpen { get; private set; }

    /// <summary>The highlighted segment: what a release or click would apply.</summary>
    public IndicatorSegment Selected { get; private set; } = IndicatorSegment.None;

    /// <summary>Changes the trigger. Closes the menu and replays a held-back click; returns true if the menu was open.</summary>
    public bool Configure(MouseTrigger trigger, TimeSpan doubleClick)
    {
        var wasOpen = Reset();
        _trigger = trigger;
        _doubleClick = doubleClick;
        return wasOpen;
    }

    /// <summary>Closes the menu (its pending releases are still swallowed). Returns true if it was open.</summary>
    public bool CancelMenu()
    {
        if (!IsOpen) return false;
        Close();
        return true;
    }

    /// <summary>True when the open menu was opened by the keyboard trigger.</summary>
    public bool IsOpenByKeyboard => IsOpen && _byKeyboard;

    /// <summary>
    /// Opens the menu at (x, y) because the keyboard trigger is held: moving the cursor chooses
    /// (nothing until it first leaves the center), and the caller applies the choice when the
    /// trigger is released. A click applies it right away; another button cancels.
    /// </summary>
    public MouseDecision OpenForKeyboard(int x, int y)
    {
        if (_pending) Flush();
        var decision = Open(x, y);
        _holding = false;
        _byKeyboard = true;
        return decision;
    }

    /// <summary>
    /// A key chose <paramref name="segment"/> while the keyboard trigger's menu is open. It stays
    /// chosen until the cursor moves into a different segment than the one it is in now, so the
    /// mouse and the keys can take turns. False when no keyboard-opened menu is open.
    /// </summary>
    public bool KeyboardChose(IndicatorSegment segment)
    {
        if (!IsOpenByKeyboard) return false;
        Selected = segment;
        _keyGuard = SegmentAt(_lastX - _anchorX, _lastY - _anchorY, _deadZone);
        return true;
    }

    /// <summary>Closes the menu and replays a held-back click. Returns true if the menu was open.</summary>
    public bool Reset()
    {
        if (_pending) Flush();
        return CancelMenu();
    }

    /// <param name="e">The event.</param>
    /// <param name="canOpen">False while the menu must not open (Loop is disabled, or the keyboard trigger is held).</param>
    public MouseDecision Process(MouseEvent e, bool canOpen)
    {
        if (e.Kind is MouseEventKind.Down or MouseEventKind.Up)
        {
            if (e.Injected)
            {
                if (_inFlight.TryGetValue(e.Button, out var n) && n > 0) _inFlight[e.Button] = n - 1;
                return MouseDecision.PassThrough;
            }

            if (IsInFlight(e.Button, e.Time))
            {
                // Keep the order: this one goes after the injected events still on their way.
                Inject(new[] { (e.Button, e.Kind == MouseEventKind.Down) });
                return MouseDecision.Swallowed;
            }
        }
        else if (e.Injected)
        {
            return MouseDecision.PassThrough;
        }

        if (IsOpen) return HandleOpen(e);

        if (_pending && HandlePending(e, canOpen) is { } decision) return decision;

        return HandleIdle(e, canOpen);
    }

    /// <summary>Which segment a cursor offset from the menu's center selects. Screen y grows downwards.</summary>
    public static IndicatorSegment SegmentAt(double dx, double dy, double deadZone)
    {
        if (dx * dx + dy * dy <= deadZone * deadZone) return IndicatorSegment.Center;
        var degrees = Math.Atan2(-dy, dx) * 180 / Math.PI; // counter-clockwise from the right
        var sector = (int)Math.Round(degrees / 45.0);
        return (IndicatorSegment)(((sector % 8) + 8) % 8);
    }

    private MouseButton? TriggerButton => _trigger switch
    {
        MouseTrigger.MiddleDoubleClick => MouseButton.Middle,
        MouseTrigger.BackButton => MouseButton.X1,
        MouseTrigger.ForwardButton => MouseButton.X2,
        _ => null,
    };

    private MouseDecision HandleIdle(MouseEvent e, bool canOpen)
    {
        if (e.Kind == MouseEventKind.Up && _swallowUp.Remove(e.Button)) return MouseDecision.Swallowed;
        if (!canOpen || e.Kind != MouseEventKind.Down || e.Button != TriggerButton) return MouseDecision.PassThrough;

        if (_trigger == MouseTrigger.MiddleDoubleClick)
        {
            // Hold this click back until we know whether a second one follows.
            _pending = true;
            _pendingReleased = false;
            (_pendingX, _pendingY, _pendingTime) = (e.X, e.Y, e.Time);
            var generation = ++_timerGeneration;
            _timer = _schedule(_doubleClick, () =>
            {
                if (generation == _timerGeneration && _pending) Flush();
            });
            return MouseDecision.Swallowed;
        }

        _swallowUp.Add(e.Button);
        return Open(e.X, e.Y);
    }

    /// <summary>Handles an event while a first middle click is held back; null to handle it as idle.</summary>
    private MouseDecision? HandlePending(MouseEvent e, bool canOpen)
    {
        switch (e.Kind)
        {
            case MouseEventKind.Up when e.Button == MouseButton.Middle && !_pendingReleased:
                _pendingReleased = true;
                return MouseDecision.Swallowed;

            case MouseEventKind.Down when e.Button == MouseButton.Middle && _pendingReleased:
                if (canOpen && unchecked(e.Time - _pendingTime) <= _doubleClick.TotalMilliseconds && IsNearPending(e))
                {
                    CancelTimer();
                    _pending = false;
                    _swallowUp.Add(MouseButton.Middle);
                    return Open(e.X, e.Y);
                }
                Flush();
                return null; // may be the first click of a new double click

            case MouseEventKind.Move:
                if (!IsNearPending(e)) Flush(); // not a double click: a drag, or the cursor moved on
                return MouseDecision.PassThrough;

            default:
                Flush(); // another button or the wheel: let the click through first
                return null;
        }
    }

    private MouseDecision HandleOpen(MouseEvent e)
    {
        switch (e.Kind)
        {
            case MouseEventKind.Move:
            {
                (_lastX, _lastY) = (e.X, e.Y);
                var segment = SegmentAt(e.X - _anchorX, e.Y - _anchorY, _deadZone);
                if (_keyGuard is { } guard)
                {
                    if (segment == guard) return MouseDecision.PassThrough; // the key's choice stands
                    _keyGuard = null;
                    _leftCenter = true;
                }
                if ((_holding || _byKeyboard) && !_leftCenter)
                {
                    if (segment == IndicatorSegment.Center) return MouseDecision.PassThrough;
                    _leftCenter = true;
                }
                return Select(segment, swallow: false);
            }

            case MouseEventKind.Wheel:
                return new MouseDecision(true, IsDirection(Selected) ? MouseMenuEffect.Cycle : MouseMenuEffect.None, Selected);

            case MouseEventKind.Down:
                _swallowUp.Add(e.Button);
                var confirm = !_holding && (e.Button == MouseButton.Left || e.Button == TriggerButton);
                return Close(confirm && Selected != IndicatorSegment.None ? MouseMenuEffect.Commit : MouseMenuEffect.Cancel);

            case MouseEventKind.Up:
                if (!_swallowUp.Remove(e.Button)) return MouseDecision.PassThrough;
                if (!_holding || e.Button != TriggerButton) return MouseDecision.Swallowed;

                if (!_leftCenter)
                {
                    // Released without choosing: keep the menu open, to click a segment.
                    _holding = false;
                    return Select(SegmentAt(_lastX - _anchorX, _lastY - _anchorY, _deadZone), swallow: true);
                }
                return Close(Selected == IndicatorSegment.None ? MouseMenuEffect.Cancel : MouseMenuEffect.Commit);

            default:
                return MouseDecision.PassThrough;
        }
    }

    private MouseDecision Open(int x, int y)
    {
        IsOpen = true;
        _holding = true;
        _leftCenter = false;
        _byKeyboard = false;
        _keyGuard = null;
        (_anchorX, _anchorY, _lastX, _lastY) = (x, y, x, y);
        _deadZone = _deadZoneAt(x, y);
        Selected = IndicatorSegment.None;
        return new MouseDecision(true, MouseMenuEffect.Open, IndicatorSegment.None, x, y);
    }

    private MouseDecision Select(IndicatorSegment segment, bool swallow)
    {
        if (segment == Selected) return new MouseDecision(swallow);
        Selected = segment;
        return new MouseDecision(swallow, MouseMenuEffect.Select, segment);
    }

    private MouseDecision Close(MouseMenuEffect effect)
    {
        var segment = Selected;
        Close();
        return new MouseDecision(true, effect, segment);
    }

    private void Close()
    {
        IsOpen = false;
        _holding = false;
        _byKeyboard = false;
        Selected = IndicatorSegment.None;
    }

    /// <summary>Lets the held-back click through: a whole click if it was released, else just the press.</summary>
    private void Flush()
    {
        CancelTimer();
        _pending = false;
        Inject(_pendingReleased
            ? new[] { (MouseButton.Middle, true), (MouseButton.Middle, false) }
            : new[] { (MouseButton.Middle, true) });
    }

    private void CancelTimer()
    {
        ++_timerGeneration;
        _timer?.Dispose();
        _timer = null;
    }

    private void Inject((MouseButton Button, bool Down)[] events)
    {
        // Count them before injecting: the hook may see them before the injector returns.
        foreach (var (button, _) in events) _inFlight[button] = _inFlight.GetValueOrDefault(button) + 1;
        _lastInjected = _clock();
        if (_inject(events)) return;

        // Nothing was injected, so there's nothing to wait for.
        foreach (var (button, _) in events)
            if (_inFlight.TryGetValue(button, out var n)) _inFlight[button] = Math.Max(0, n - 1);
    }

    private bool IsInFlight(MouseButton button, uint now)
    {
        if (_inFlight.GetValueOrDefault(button) <= 0) return false;
        if (unchecked(now - _lastInjected) <= InFlightTimeoutMs) return true;
        _inFlight.Clear(); // lost (e.g. blocked by Windows); don't hold real events back forever
        return false;
    }

    private bool IsNearPending(MouseEvent e) =>
        Math.Abs(e.X - _pendingX) <= DoubleClickDistance && Math.Abs(e.Y - _pendingY) <= DoubleClickDistance;

    private static bool IsDirection(IndicatorSegment segment) => segment <= IndicatorSegment.BottomRight;
}
