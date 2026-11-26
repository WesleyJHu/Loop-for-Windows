using Loop.Core.Input;

namespace Loop.Core.Tests;

public class ChordDebouncerTests
{
    private readonly record struct Item(string Name, bool NewGesture);

    /// <summary>A manual scheduler: callbacks run only when the test calls Elapse().</summary>
    private sealed class FakeScheduler
    {
        private readonly List<(Action Callback, Cancel Token)> _scheduled = new();

        public sealed class Cancel : IDisposable
        {
            public bool Cancelled { get; private set; }
            public void Dispose() => Cancelled = true;
        }

        public int Pending => _scheduled.Count(s => !s.Token.Cancelled);

        public IDisposable Schedule(TimeSpan _, Action callback)
        {
            var token = new Cancel();
            _scheduled.Add((callback, token));
            return token;
        }

        public void Elapse()
        {
            var due = _scheduled.ToList();
            _scheduled.Clear();
            foreach (var (callback, token) in due)
                if (!token.Cancelled) callback();
        }

        /// <summary>Runs callbacks even if cancelled, as a real timer racing with Dispose might.</summary>
        public void ElapseIgnoringCancellation()
        {
            var due = _scheduled.ToList();
            _scheduled.Clear();
            foreach (var (callback, _) in due) callback();
        }
    }

    private readonly FakeScheduler _scheduler = new();
    private readonly List<Item> _fired = new();

    private ChordDebouncer<Item> Create(int graceMs = 60) => new(
        TimeSpan.FromMilliseconds(graceMs),
        _scheduler.Schedule,
        _fired.Add,
        (pending, next) => next with { NewGesture = pending.NewGesture || next.NewGesture });

    [Fact]
    public void ChordKey_WaitsForGracePeriod()
    {
        var d = Create();
        d.Submit(new Item("Left", true), isChordKey: true);

        Assert.Empty(_fired);
        _scheduler.Elapse();
        Assert.Equal(new[] { new Item("Left", true) }, _fired);
    }

    [Fact]
    public void SecondChordKeyInGrace_ReplacesFirst_AndKeepsNewGesture()
    {
        var d = Create();
        d.Submit(new Item("TopHalf", NewGesture: true), isChordKey: true);
        d.Submit(new Item("TopLeft", NewGesture: false), isChordKey: true);
        _scheduler.Elapse();

        Assert.Equal(new[] { new Item("TopLeft", true) }, _fired);
    }

    [Fact]
    public void KeysAfterGrace_FireSeparately()
    {
        var d = Create();
        d.Submit(new Item("LeftHalf", true), isChordKey: true);
        _scheduler.Elapse();
        d.Submit(new Item("TopLeft", false), isChordKey: true);
        _scheduler.Elapse();

        Assert.Equal(new[] { new Item("LeftHalf", true), new Item("TopLeft", false) }, _fired);
    }

    [Fact]
    public void NonChordKey_FiresImmediately()
    {
        var d = Create();
        d.Submit(new Item("Maximize", true), isChordKey: false);
        Assert.Equal(new[] { new Item("Maximize", true) }, _fired);
        Assert.Equal(0, _scheduler.Pending);
    }

    [Fact]
    public void NonChordKey_FlushesPendingChordFirst()
    {
        var d = Create();
        d.Submit(new Item("LeftHalf", true), isChordKey: true);
        d.Submit(new Item("NextDisplay", true), isChordKey: false);

        Assert.Equal(new[] { new Item("LeftHalf", true), new Item("NextDisplay", true) }, _fired);
        _scheduler.Elapse();
        Assert.Equal(2, _fired.Count); // the cancelled timer doesn't fire it again
    }

    [Fact]
    public void StaleTimerCallback_IsIgnored()
    {
        var d = Create();
        d.Submit(new Item("A", true), isChordKey: true);
        d.Flush();
        d.Submit(new Item("B", true), isChordKey: true);

        _scheduler.ElapseIgnoringCancellation(); // both timers run; only B's is current

        Assert.Equal(new[] { new Item("A", true), new Item("B", true) }, _fired);
        Assert.False(d.HasPending);
    }

    [Fact]
    public void ZeroGrace_FiresImmediately()
    {
        var d = Create(graceMs: 0);
        d.Submit(new Item("Left", true), isChordKey: true);
        Assert.Single(_fired);
    }

    [Fact]
    public void Flush_WithNothingPending_DoesNothing()
    {
        var d = Create();
        d.Flush();
        Assert.Empty(_fired);
    }
}
