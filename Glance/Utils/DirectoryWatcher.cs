using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
// 扫描容错catch等价于KMP的normalize兜底（权限不足、路径非法），属有意设计，屏蔽CA1031。
#pragma warning disable CA1031

namespace Glance.Utils;

/// <summary>
/// 定期扫描目录变化（仅桌面/PC；Android/iOS不支持，调用方以 #if !ANDROID排除本类）。
/// 快照对比：无变化不触发回调，避免树反复重建。
/// </summary>
public sealed class DirectoryWatcher : IDisposable
{
    private readonly Action _onChange;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly Timer _timer;
    private HashSet<string>? _lastSnapshot;
    private bool _disposed;

    public DirectoryWatcher(Action onChange, TimeSpan? interval = null)
    {
        _onChange = onChange ?? throw new ArgumentNullException(nameof(onChange));
        _interval = interval ?? TimeSpan.FromSeconds(3);
        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SyncRoot(string? root, IReadOnlyList<string>? expandedDirs)
    {
        var target = new HashSet<string>(StringComparer.Ordinal);
        var normRoot = Normalize(root);
        if (!string.IsNullOrEmpty(normRoot)) target.Add(normRoot);
        if (expandedDirs is not null)
        {
            foreach (var dir in expandedDirs)
            {
                var n = Normalize(dir);
                if (!string.IsNullOrEmpty(n) && !string.Equals(n, normRoot, StringComparison.Ordinal))
                    target.Add(n);
            }
        }

        lock (_gate)
        {
            if (_disposed) return;
            _paths.Clear();
            foreach (var p in target) _paths.Add(p);
            _lastSnapshot = null;
            if (target.Count == 0)
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            else
                _timer.Change(_interval, _interval);
        }
    }

    private void OnTick(object? state)
    {
        HashSet<string> paths;
        lock (_gate)
        {
            if (_disposed || _paths.Count == 0) return;
            paths = new HashSet<string>(_paths, StringComparer.Ordinal);
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        var snapshot = ScanSnapshot(paths);

        lock (_gate)
        {
            if (_disposed || _paths.Count == 0) return;
            if (_lastSnapshot is null)
            {
                _lastSnapshot = snapshot;
                _timer.Change(_interval, _interval);
                return;
            }
            if (_lastSnapshot.SetEquals(snapshot))
            {
                _timer.Change(_interval, _interval);
                return;
            }
            _lastSnapshot = snapshot;
        }

        try { _onChange(); } catch { }

        lock (_gate)
        {
            if (!_disposed && _paths.Count > 0)
                _timer.Change(_interval, _interval);
        }
    }

    private static HashSet<string> ScanSnapshot(HashSet<string> paths)
    {
        var snapshot = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in paths)
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    try
                    {
                        var attrs = File.GetAttributes(entry);
                        if ((attrs & FileAttributes.Directory) != 0)
                            snapshot.Add($"{entry}|D");
                        else
                        {
                            var fi = new FileInfo(entry);
                            snapshot.Add($"{entry}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}");
                        }
                    }
                    catch { snapshot.Add(entry); }
                }
            }
            catch { }
        }
        return snapshot;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return path; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
    }
}
