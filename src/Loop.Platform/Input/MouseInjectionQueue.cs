using System.Collections.Concurrent;
using Loop.Core.Input;

namespace Loop.Platform.Input;

/// <summary>
/// Injects mouse buttons on a thread of its own, in the order they were queued.
///
/// SendInput doesn't return until every low-level hook has seen the injected events, Loop's own
/// mouse hook included. Callers hold the input-handling lock that the hook also takes (or are the
/// hook), so calling SendInput directly would make the hook wait on the lock until Windows gives
/// up on it (LowLevelHooksTimeout): each replayed click would be delayed, Loop would never see it
/// arrive, and Windows may drop a hook that keeps timing out.
/// </summary>
internal sealed class MouseInjectionQueue : IDisposable
{
    private readonly BlockingCollection<(MouseButton Button, bool Down)[]> _queue = new();
    private readonly Thread _thread;

    public MouseInjectionQueue()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Loop mouse injection",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
    }

    /// <summary>Queues the events; returns false only once the queue has been disposed.</summary>
    public bool Enqueue(IReadOnlyList<(MouseButton Button, bool Down)> events)
    {
        if (events.Count == 0) return true;
        try
        {
            _queue.Add(events.ToArray());
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // completed (shutting down)
        }
    }

    /// <summary>Sends what's still queued, then stops.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    private void Run()
    {
        foreach (var events in _queue.GetConsumingEnumerable())
        {
            try
            {
                if (!InputInjector.SendMouseButtons(events)) Log.Write("failed to inject a mouse click");
            }
            catch (Exception ex)
            {
                Log.Write($"mouse injection error: {ex.Message}");
            }
        }
    }
}
