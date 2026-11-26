namespace Loop.Core.Input;

/// <summary>
/// Holds back chord keys for a short grace period so that keys pressed almost together
/// (Up and Left within a few milliseconds) apply once as a corner, instead of first snapping to
/// the top half and then jumping to the corner.
///
/// Not thread-safe: call <see cref="Submit"/> and run scheduled callbacks on one thread.
/// </summary>
/// <typeparam name="T">The pending item (an action plus metadata).</typeparam>
public sealed class ChordDebouncer<T> where T : struct
{
    private readonly TimeSpan _grace;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly Action<T> _fire;
    private readonly Func<T, T, T> _merge;
    private T? _pending;
    private IDisposable? _timer;
    private int _generation;

    /// <param name="grace">How long to wait for further chord keys.</param>
    /// <param name="schedule">Runs a callback after a delay on the caller's thread; disposing cancels it.</param>
    /// <param name="fire">Applies an item.</param>
    /// <param name="merge">Combines the pending item with a newer one that replaces it.</param>
    public ChordDebouncer(TimeSpan grace, Func<TimeSpan, Action, IDisposable> schedule, Action<T> fire, Func<T, T, T> merge)
    {
        _grace = grace;
        _schedule = schedule;
        _fire = fire;
        _merge = merge;
    }

    public bool HasPending => _pending.HasValue;

    /// <param name="item">The item to apply.</param>
    /// <param name="isChordKey">
    /// True to wait for more chord keys. A newer chord item replaces a pending one (via merge);
    /// a non-chord item first fires any pending item, then fires immediately.
    /// </param>
    public void Submit(T item, bool isChordKey)
    {
        if (!isChordKey || _grace <= TimeSpan.Zero)
        {
            Flush();
            _fire(item);
            return;
        }

        if (_pending is { } pending)
        {
            // Keep the original deadline so a stream of keys can't postpone the action forever.
            _pending = _merge(pending, item);
            return;
        }

        _pending = item;
        var generation = ++_generation;
        _timer = _schedule(_grace, () =>
        {
            if (generation == _generation) Flush();
        });
    }

    /// <summary>Drops the pending item without firing it.</summary>
    public void Cancel()
    {
        _timer?.Dispose();
        _timer = null;
        _generation++;
        _pending = null;
    }

    /// <summary>Fires the pending item now, if any.</summary>
    public void Flush()
    {
        _timer?.Dispose();
        _timer = null;
        _generation++;
        if (_pending is not { } pending) return;
        _pending = null;
        _fire(pending);
    }
}
