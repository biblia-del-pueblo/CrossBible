using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace CrossBible.Library;

/// <summary>
/// TEMPORARY, to find what makes scrolling stutter: a timer asks the UI thread for a turn every few milliseconds at the priority of drawing, and
/// the time between two turns is logged when it is long (a frame is 16 ms). Remove it, and its call in BibleView, when the stutter is found.
/// </summary>
public static class StallMonitor
{
    private static DispatcherTimer? _timer;
    private static long _last;
    private static int _gen0, _gen1, _gen2;
    private static double _pause;
    public static long StallsOver40, StallsOver100;

    public static void Start()
    {
        if (_timer is not null) return;
        _last = Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            double gap = (now - _last) * 1000.0 / Stopwatch.Frequency;
            _last = now;
            // Did the garbage collector run since the last turn, and how long did it stop the program?
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            double pause = GC.GetTotalPauseDuration().TotalMilliseconds;
            if (gap > 40)
            {
                StallsOver40++;
                if (gap > 100) StallsOver100++;
                Console.WriteLine($"[PERF] UI stall: {gap:0} ms; collections since the last turn: gen0 {gen0 - _gen0}, gen1 {gen1 - _gen1}, gen2 {gen2 - _gen2}, " +
                    $"stopped by them {pause - _pause:0} ms; heap {GC.GetTotalMemory(false) / 1048576} MB");
            }
            _gen0 = gen0; _gen1 = gen1; _gen2 = gen2; _pause = pause;
        });
        _timer.Start();
    }
}
