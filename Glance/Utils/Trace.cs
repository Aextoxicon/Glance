using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Glance.Utils;

public static class Trace
{
    public static bool Enabled { get; set; }

    private const int Capacity = 20000;
    private const int FlushThreshold = 100;
    private static readonly object _lock = new();
    private static readonly List<TraceEvent> _events = new();
    private static StreamWriter? _stream;
    private static string? _streamPath;

    private static readonly Stopwatch Base = Stopwatch.StartNew();
    private static long _last;

    private struct TraceEvent
    {
        public long TotalMs;
        public long DeltaMs;
        public string Label;
        public string Extra;
    }

    public static void Mark(string label, string extra = "")
    {
        if (!Enabled) return;
        var now = Base.ElapsedMilliseconds;
        var delta = now - System.Threading.Interlocked.Exchange(ref _last, now);
        var ev = new TraceEvent { TotalMs = now, DeltaMs = delta, Label = label, Extra = extra };
        lock (_lock)
        {
            _events.Add(ev);
            if (_events.Count > Capacity) _events.RemoveAt(0);
            if (_stream is not null)
            {
                _stream.WriteLine(Row(ev));
                if (_events.Count % FlushThreshold == 0) _stream.Flush();
            }
        }
    }

    public static string? Start(string directory)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(directory);
            _streamPath = Path.Combine(directory, $"glance_trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _stream = new StreamWriter(_streamPath, append: false) { AutoFlush = false };
            _stream.WriteLine("total_ms,delta_ms,label,extra");
            _events.Clear();
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
            return _events.Select(Row).ToArray();
        }
    }

    // 导出到指定目录，返回文件路径；无事件时返回null
    // 实时流已落盘CSV时仅补JSON汇总；无实时流则从缓冲写出CSV+JSON
    public static string? ExportTo(string directory)
    {
        var livePath = Stop();
        List<TraceEvent> evs;
        lock (_lock) { evs = new List<TraceEvent>(_events); }
        if (livePath is not null)
        {
            if (evs.Count > 0) WriteSummary(livePath, evs);
            lock (_lock) { _events.Clear(); }
            return livePath;
        }
        if (evs.Count == 0) return null;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"glance_trace_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        using (var w = new StreamWriter(path, append: false))
        {
            w.WriteLine("total_ms,delta_ms,label,extra");
            foreach (var e in evs) w.WriteLine(Row(e));
        }
        WriteSummary(path, evs);
        return path;
    }

    private static string Row(TraceEvent e) =>
        $"{e.TotalMs},{e.DeltaMs},{CsvField(e.Label)},{CsvField(e.Extra)}";

    private static string CsvField(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // 整段日志跨所有打开汇总平均/最快/最慢（外加中位与样本数）
    private static void WriteSummary(string csvPath, IReadOnlyList<TraceEvent> evs)
    {
        var opens = new List<long>();
        long? startTotal = null;
        foreach (var e in evs)
        {
            if (e.Label == "select.start") startTotal = e.TotalMs;
            else if (e.Label == "select.painted" && startTotal.HasValue)
            {
                opens.Add(e.TotalMs - startTotal.Value);
                startTotal = null;
            }
        }
        if (opens.Count == 0) return;
        var sorted = opens.OrderBy(x => x).ToList();
        var avg = Math.Round(sorted.Average(), 1);
        var median = sorted.Count % 2 == 0
            ? Math.Round((sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0, 1)
            : sorted[sorted.Count / 2];
        var summary = new TraceSummary(
            sorted.Count, avg, median, sorted[0], sorted[^1], sorted.ToArray());
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(Path.ChangeExtension(csvPath, ".summary.json"), json);
    }

    private sealed record TraceSummary(
        int opens, double averageMs, double medianMs, long fastestMs, long slowestMs, long[] allMs);
}
