using System;
using System.Diagnostics;
using System.IO;

namespace Glance.Utils;

public static class Trace
{
    public static bool Enabled { get; set; }

    private const int Capacity = 20000;
    private const int FlushThreshold = 100;
    private static readonly object _lock = new();
    private static readonly System.Collections.Generic.Queue<string> _buffer = new();
    private static StreamWriter? _stream;
    private static string? _streamPath;

    private static readonly Stopwatch Base = Stopwatch.StartNew();
    private static long _last;

    public static void Mark(string label, string extra = "")
    {
        if (!Enabled) return;
        var now = Base.ElapsedMilliseconds;
        var delta = now - System.Threading.Interlocked.Exchange(ref _last, now);
        var line = $"TRACE total_ms={now} delta_ms={delta} {label} {extra}".Trim();
        lock (_lock)
        {
            _buffer.Enqueue(line);
            if (_buffer.Count > Capacity) _buffer.Dequeue();
            if (_stream is not null)
            {
                _stream.WriteLine(line);
                if (_buffer.Count % FlushThreshold == 0) _stream.Flush();
            }
        }
    }

    public static string? Start(string directory)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(directory);
            _streamPath = Path.Combine(directory, $"glance_trace_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _stream = new StreamWriter(_streamPath, append: false) { AutoFlush = false };
            _buffer.Clear();
            return _streamPath;
        }
    }

    public static string? Stop()
    {
        lock (_lock)
        {
            _stream?.Flush();
            _stream?.Dispose();
            _stream = null;
            var path = _streamPath;
            _streamPath = null;
            return path;
        }
    }

    public static string[] Drain()
    {
        lock (_lock)
        {
            return _buffer.ToArray();
        }
    }

    // 导出到指定目录，返回文件路径；环形缓冲为空时返回null
    public static string? ExportTo(string directory)
    {
        var stopped = Stop();
        if (stopped is not null) return stopped;
        var lines = Drain();
        if (lines.Length == 0) return null;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"glance_trace_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        File.WriteAllText(path, string.Join('\n', lines));
        return path;
    }
}
