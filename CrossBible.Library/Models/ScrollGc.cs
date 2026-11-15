using System;
using System.Diagnostics;
using System.Runtime;
using Avalonia.Threading;

namespace CrossBible.Library;

/// <summary>
/// Keeps the garbage collector from running while the text scrolls. Moving the window of chapters allocates a few megabytes at once (the new
/// inlines, the text, its lines), and the collection that followed stopped the app for 40-50 ms on a phone, which is three frames and
/// seen as a stutter. While the reader scrolls, the program runs in a "no GC region" with a budget of <see cref="Budget"/> bytes (many
/// moves of the window); a moment after the last scrolling a collection runs, when nobody is looking, and the next region begins. If the budget
/// is used up the runtime ends the region and collects as it always did, so nothing can run out of memory because of this.
/// </summary>
public static class ScrollGc
{
    /// <summary>What may be allocated while scrolling before a collection runs after all.</summary>
    private const long Budget = 64L * 1024 * 1024;

    /// <summary>How long nothing is scrolled before the collection runs.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(700);

    private static DispatcherTimer? _idle;
    private static bool _refused; // the platform does not allow a region of this size: the collector then works as it does without this class

    /// <summary>The text scrolled or was laid out: the collector waits, until nothing has happened for a moment.</summary>
    /// <summary>Off: it made the stalls fewer but longer (the budget ran out every 6-9 seconds, and the collection then took 130-185 ms). Kept to try again.</summary>
    public static bool Enabled { get; set; } = false;

    public static void Activity()
    {
        if (!Enabled || _refused) return;
        if (_idle is null)
        {
            _idle = new DispatcherTimer { Interval = IdleDelay };
            _idle.Tick += (_, _) => OnIdle();
        }
        _idle.Stop();
        _idle.Start();
        if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion) Begin(); // the first scrolling, or the budget was used up: a collection may run now
    }

    private static void OnIdle()
    {
        _idle?.Stop();
        End();
        long t0 = Stopwatch.GetTimestamp(); // TEMPORARY (the measuring)
        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"[PERF] collection at rest: {(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency:0} ms; heap {GC.GetTotalMemory(false) / 1048576} MB");
        Begin(); // the region for the next scrolling, started now so its own collection also happens at rest
    }

    private static void Begin()
    {
        try
        {
            if (!GC.TryStartNoGCRegion(Budget)) { _refused = true; Console.WriteLine("[PERF] no-GC region: refused"); }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            _refused = true;
            Console.WriteLine($"[PERF] no-GC region: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void End()
    {
        try { if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion(); }
        catch (InvalidOperationException) { /* the region ended by itself (the budget was used up, or a collection was asked for) */ }
    }
}
