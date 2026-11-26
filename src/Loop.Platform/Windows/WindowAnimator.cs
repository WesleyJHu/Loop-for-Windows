using System.Diagnostics;
using Loop.Core.Animation;
using Loop.Core.Geometry;
using static Loop.Platform.Interop.NativeMethods;

namespace Loop.Platform.Windows;

/// <summary>
/// Glides a window between two rectangles on a background thread, one SetWindowPos per desktop
/// composition frame. Only the intermediate frames run here: the caller places the final frame
/// itself (through the normal placement path) once <see cref="Run"/>'s callback fires or after
/// <see cref="Stop"/>. Running off the UI thread keeps the keyboard hook responsive.
/// </summary>
internal sealed class WindowAnimator
{
    private const uint FrameFlags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    /// <summary>Starts animating; stops any animation in progress first.</summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="from">Its visible bounds now.</param>
    /// <param name="to">The target visible bounds.</param>
    /// <param name="windowRect">Its outer rect now (with <paramref name="from"/>, gives the invisible-border widths).</param>
    /// <param name="frameBounds">Intermediate frames' outer rects are clipped to this.</param>
    /// <param name="options">Duration and easing.</param>
    /// <param name="framesDone">Called on the animation thread when the last intermediate frame is done.</param>
    public void Run(IntPtr hwnd, Rect from, Rect to, Rect windowRect, Rect frameBounds, AnimationOptions options, Action framesDone)
    {
        Stop();
        var cts = new CancellationTokenSource();
        var thread = new Thread(() => Frames(hwnd, from, to, windowRect, frameBounds, options, cts.Token, framesDone))
        {
            IsBackground = true,
            Name = "Loop window animation",
            Priority = ThreadPriority.AboveNormal,
        };
        lock (_gate)
        {
            _cts = cts;
            _thread = thread;
        }
        thread.Start();
    }

    /// <summary>Stops the animation (if any) and waits for its thread, so the caller can place the final frame.</summary>
    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            _cts?.Cancel();
            thread = _thread;
            _cts = null;
            _thread = null;
        }
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromMilliseconds(250));
    }

    private static void Frames(IntPtr hwnd, Rect from, Rect to, Rect windowRect, Rect frameBounds,
        AnimationOptions options, CancellationToken ct, Action framesDone)
    {
        var clock = Stopwatch.StartNew();
        var detector = new SlowFrameDetector();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var progress = RectTween.Progress(clock.Elapsed, options.Duration, options.Easing);
                if (progress >= 1) break;

                var visible = RectTween.At(from, to, progress);
                var outer = FrameAdjuster.ToWindowRect(visible, windowRect, from);
                var clipped = outer.Intersect(frameBounds);
                if (!clipped.IsEmpty) outer = clipped;

                var before = clock.Elapsed;
                if (!IsWindow(hwnd) || !SetWindowPos(hwnd, IntPtr.Zero, outer.Left, outer.Top, outer.Width, outer.Height, FrameFlags))
                    break;
                if (detector.Record(clock.Elapsed - before))
                {
                    Platform.Log.Write("animation abandoned: the window repaints too slowly");
                    break;
                }

                if (DwmFlush() != 0) Thread.Sleep(8); // composition unavailable: pace roughly instead
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested) framesDone();
        }
    }
}
