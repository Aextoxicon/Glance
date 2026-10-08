using System.Diagnostics;

namespace Glance.Utils;

public static class Trace
{
    public static bool Enabled { get; set; }

    private static readonly Stopwatch Base = Stopwatch.StartNew();
    private static long _last;

    public static void Mark(string label, string extra = "")
    {
        if (!Enabled) return;
        var now = Base.ElapsedMilliseconds;
        var delta = now - System.Threading.Interlocked.Exchange(ref _last, now);
        System.Console.WriteLine($"TRACE total_ms={now} delta_ms={delta} {label} {extra}".Trim());
    }
}
