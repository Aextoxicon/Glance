using System;
using System.Diagnostics;
using System.IO;

namespace Glance.Utils;

public static class Trace
{
    public static bool Enabled { get; set; }

    private const int Capacity = 2000;
    private static readonly object _lock = new();
    private static readonly System.Collections.Generic.Queue<string> _buffer = new();

    private static readonly Stopwatch Base = Stopwatch.StartNew();
    private static long _last;

    public static void Mark(string label, string extra = "")
    {
        if (!Enabled) return;
        var now = Base.ElapsedMilliseconds;
        var delta = now - System.Threading.Interlocked.Exchange(ref _last, now);
        var line = $"TRACE total_ms={now} delta_ms={delta} {label} {extra}".Trim();
        Console.WriteLine(line);
        lock (_lock)
        {
            _buffer.Enqueue(line);
            while (_buffer.Count > Capacity) _buffer.Dequeue();
        }
    }

    public static string[] Drain()
    {
        lock (_lock)
        {
            return _buffer.ToArray();
        }
    }

    public static string DrainText() => string.Join('\n', Drain());

    // 导出到指定目录，返回文件路径；环形缓冲为空时返回null
    public static string? ExportTo(string directory)
    {
        var lines = Drain();
        if (lines.Length == 0) return null;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"glance_trace_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        File.WriteAllText(path, string.Join('\n', lines));
        return path;
    }
}
