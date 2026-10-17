using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
// 监听容错catch等价于KMP的register返回null / normalize兜底（权限不足、路径非法、平台不支持），属有意设计，屏蔽CA1031（https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031）。
#pragma warning disable CA1031

namespace Glance.Utils;

/// <summary>
/// 用BCL https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher
/// 实现（仅桌面/PC可用；Android/iOS不支持，调用方以 #if !ANDROID排除本类）
/// </summary>
public sealed class DirectoryWatcher : IDisposable
{
    private readonly Action _onBatchedChange;
    private readonly int _debounceMs;
    private readonly object _gate = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private readonly Timer _debounceTimer;
    private bool _disposed;

    public DirectoryWatcher(Action onBatchedChange, int debounceMs = 300)
    {
        _onBatchedChange = onBatchedChange ?? throw new ArgumentNullException(nameof(onBatchedChange));
        _debounceMs = debounceMs;
        _debounceTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 目标为空集合则停止全部监听。
    /// </summary>
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
            foreach (var path in _watchers.Keys.ToList())
            {
                if (!target.Contains(path)) RemoveWatcher(path);
            }
            if (target.Count == 0)
            {
                StopAll();
                return;
            }
            foreach (var path in target)
            {
                if (!_watchers.ContainsKey(path)) AddWatcher(path);
            }
        }
    }

    public void Close() => Dispose();

    private void AddWatcher(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            var fsw = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            fsw.Created += OnFsEvent;
            fsw.Deleted += OnFsEvent;
            fsw.Renamed += OnFsEvent;
            fsw.Changed += OnFsEvent;
            fsw.Error += OnFsError;
            fsw.EnableRaisingEvents = true;
            _watchers[path] = fsw;
        }
        catch (Exception)
        {
            // 监听失败（权限不足/平台不支持）静默跳过
        }
    }

    private void RemoveWatcher(string path)
    {
        if (_watchers.TryGetValue(path, out var fsw))
        {
            fsw.EnableRaisingEvents = false;
            fsw.Created -= OnFsEvent;
            fsw.Deleted -= OnFsEvent;
            fsw.Renamed -= OnFsEvent;
            fsw.Changed -= OnFsEvent;
            fsw.Error -= OnFsError;
            fsw.Dispose();
            _watchers.Remove(path);
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => ScheduleDebouncedNotify();

    private void OnFsError(object sender, ErrorEventArgs e) => ScheduleDebouncedNotify();

    private void ScheduleDebouncedNotify()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _debounceTimer.Change(_debounceMs, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }
    }

    private void Flush()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }
        _onBatchedChange();
    }

    private void StopAll()
    {
        foreach (var path in _watchers.Keys.ToList()) RemoveWatcher(path);
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception) { return path; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopAll();
        }
        _debounceTimer.Dispose();
    }
}
