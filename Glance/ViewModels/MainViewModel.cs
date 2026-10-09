using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
// VM 顶层操作刻意吞掉具体异常、转成 MessageText 展示给用户纯属有意设计，屏蔽 CA1031（https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031）。
#pragma warning disable CA1031
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Native;
using Glance.Processing;
using Glance.Repositories;
using Glance.Utils;

namespace Glance.ViewModels;

/// <summary>
/// 平移自 KMP MainViewModel.kt；协程模型见
/// "https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/"
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private const int WideModeThreshold = 640;
    private const int ParseCacheMax = 32;
    private const long PreviewHardLimitBytes = 10L * 1024 * 1024;
    private const long PreviewPlainLimitBytes = 2L * 1024 * 1024;
    private const int SizeScanConcurrency = 8;
    private const int ExpandConcurrency = 8;

    public const int DefaultCodeFontSize = 13;
    public static readonly int[] CodeFontSizes = { 10, 12, 13, 14, 16, 18, 20, 24, 28 };

    private readonly ILocalFileSystem _fs;
    private readonly ICodeParser _parser;
    private readonly CancellationTokenSource _scopeCts = new();
    private readonly object _cacheLock = new();
    private readonly LruCache _parseCache;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _selectCts;
    private CancellationTokenSource? _sizeCts;
    private CancellationTokenSource? _treeRefreshCts;
    private CancellationTokenSource? _treeOwnerCts;
    private TreeItemViewModel? _selectedTreeItem;

    public Action? OnWorkspaceOpened { get; set; }
    public Action? OnTreeDirsChanged { get; set; }

    public Func<Task<string?>>? PickFolderAction { get; set; }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";

    [ObservableProperty]
    private ThemeMode _themeMode = ThemeMode.System;

    [ObservableProperty]
    private string _currentPath = "";

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private bool _isComputingSize;

    [ObservableProperty]
    private LocalFileInfo? _selectedArtifact;

    [ObservableProperty]
    private string? _selectedContent;

    [ObservableProperty]
    private string? _messageText;

    [ObservableProperty]
    private string? _previewNotice;

    [ObservableProperty]
    private ParsedCode? _selectedParseResult;

    [ObservableProperty]
    private ObservableCollection<TreeItemViewModel> _treeItems = new();

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isWide = true;

    [ObservableProperty]
    private int _codeFontSize = DefaultCodeFontSize;

    [ObservableProperty]
    private bool _outlineOpen;

    [ObservableProperty]
    private int? _outlineScrollTargetLine;

    public string TotalSizeReadable => FormatSize.Readable(TotalSize);
    public string SelectedSizeDisplay => SelectedArtifact is not null ? FormatSize.Readable(SelectedArtifact.Size) : "";
    public string CurrentFolderName => CurrentPath.Length == 0 ? "" : Path.GetFileName(CurrentPath);

    public IReadOnlyList<OutlineNode>? CurrentOutline =>
        SelectedParseResult?.Outline is { Count: > 0 } outline ? outline : null;

    public MainViewModel() : this(new LocalFileSystem(), new UniffiCodeParser()) { }

    public MainViewModel(ILocalFileSystem fs, ICodeParser parser)
    {
        _fs = fs;
        _parser = parser;
        _parseCache = new LruCache(ParseCacheMax);
    }

    partial void OnThemeModeChanged(ThemeMode value)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = value switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    public void SelectThemeMode(ThemeMode mode) => ThemeMode = mode;
    public void SelectCodeFontSize(int size) => CodeFontSize = size;

    public void OnWindowResized(double width) => IsWide = width > WideModeThreshold;

    public void ToggleOutline() => OutlineOpen = !OutlineOpen;
    public void CloseOutline() => OutlineOpen = false;
    public void RequestScrollToLine(int line) => OutlineScrollTargetLine = line;
    public void ConsumeScrollTargetLine() => OutlineScrollTargetLine = null;

    public async Task PickFolder()
    {
        if (PickFolderAction is null) return;
        var path = await PickFolderAction();
        if (path is not null) await LoadCore(path);
    }

    public void CloseWorkspace()
    {
        _selectedTreeItem?.IsSelected = false;
        _selectedTreeItem = null;
        CancelLoadJobs();
        IsComputingSize = false;
        CurrentPath = "";
        HasWorkspace = false;
        TreeItems = new ObservableCollection<TreeItemViewModel>();
        TotalSize = 0;
        SelectedArtifact = null;
        SelectedContent = null;
        SelectedParseResult = null;
        HasSelection = false;
        MessageText = null;
        PreviewNotice = null;
        OnTreeDirsChanged?.Invoke();
    }

    public async Task LoadCore(string path)
    {
        CancelLoadJobs();
        _selectedTreeItem?.IsSelected = false;
        _selectedTreeItem = null;
        _treeOwnerCts = new CancellationTokenSource();
        CurrentPath = path;
        HasWorkspace = true;
        lock (_cacheLock) _parseCache.Clear();
        TotalSize = 0;
        TreeItems = new ObservableCollection<TreeItemViewModel>();

        try
        {
            var items = await Task.Run(() => _fs.ListFiles(path), _scopeCts.Token);
            var ignored = await Task.Run(() => GitIgnore.Parse(ReadGitIgnore(path)), _scopeCts.Token);
            if (_scopeCts.IsCancellationRequested) return;
            TreeItems = new ObservableCollection<TreeItemViewModel>(
                GitIgnore.Filter(items, ignored).Select(info =>
                    new TreeItemViewModel(info, _fs, _treeOwnerCts.Token, () => OnTreeDirsChanged?.Invoke(), ignored)));
            OnWorkspaceOpened?.Invoke();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MessageText = $"加载失败: {ex.Message}";
        }

        IsComputingSize = true;
        _sizeCts = new CancellationTokenSource();
        var sizePath = path;
        try
        {
            var size = await ComputeTotalSize(sizePath, _sizeCts.Token);
            if (!_scopeCts.IsCancellationRequested && CurrentPath == sizePath)
            {
                TotalSize = size;
                IsComputingSize = false;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageText = $"计算大小失败: {ex.Message}";
        }
    }

    public void ClearSelection()
    {
        _selectedTreeItem?.IsSelected = false;
        _selectedTreeItem = null;
        SelectedArtifact = null;
        HasSelection = false;
        SelectedContent = null;
        SelectedParseResult = null;
        MessageText = null;
        PreviewNotice = null;
    }

    public async Task SelectItem(TreeItemViewModel? item)
    {
        if (item is null) return;
        if (item.IsDir)
        {
            item.ToggleExpanded();
            return;
        }
        _selectedTreeItem?.IsSelected = false;
        item.IsSelected = true;
        _selectedTreeItem = item;
        _selectCts = new CancellationTokenSource();
        await SelectFile(item.Info, _selectCts.Token);
    }

    private async Task SelectFile(LocalFileInfo artifact, CancellationToken ct)
    {
        SelectedArtifact = artifact;
        HasSelection = true;
        MessageText = null;
        SelectedContent = null;
        SelectedParseResult = null;
        PreviewNotice = null;

        Trace.Mark("select.start", $"name={artifact.Name} size={artifact.Size}");
        var loaded = await LoadFile(artifact, ct);
        if (ct.IsCancellationRequested) return;
        if (loaded is null) return;
        SelectedParseResult = loaded.ParseResult;
        SelectedContent = loaded.Content;
        PreviewNotice = loaded.Notice;
        Trace.Mark("select.end", $"name={artifact.Name}");
    }

    private async Task<LoadedFile?> LoadFile(LocalFileInfo artifact, CancellationToken ct)
    {
        if (artifact.IsDir) return null;
        var path = artifact.Path;
        if (path.Length == 0)
        {
            MessageText = $"无法读取文件: {artifact.Name}";
            return null;
        }
        if (artifact.Size > PreviewHardLimitBytes)
        {
            MessageText = $"文件过大（{FormatSize.Readable(artifact.Size)}），已跳过预览";
            return null;
        }

        string? error = null;
        LoadedFile? result = null;
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (!_fs.IsTextFile(path))
                {
                    Trace.Mark("istext", "binary=true");
                    error = $"[二进制文件] {artifact.Name} 无法预览";
                    return;
                }
                var key = CacheKey(artifact);
                lock (_cacheLock)
                {
                    if (_parseCache.TryGet(key, out var cached)) { result = cached; return; }
                }
                var content = _fs.TryReadText(path);
                if (content is null)
                {
                    Trace.Mark("io.read", "failed=true");
                    error = $"无法读取文件: {artifact.Name}";
                    return;
                }
                Trace.Mark("io.read", $"chars={content.Length}");
                var normalized = content.Replace("\t", "    ");
                Trace.Mark("tabs.replace", $"chars={normalized.Length}");
                result = BuildPreview(normalized, artifact);
                Trace.Mark("build.done", $"highlight={result.ParseResult is not null}");
                if (artifact.Size <= PreviewPlainLimitBytes)
                {
                    lock (_cacheLock) _parseCache.Set(key, result);
                }
            }, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            MessageText = $"读取失败: {ex.Message}";
            return null;
        }

        if (error is not null)
        {
            MessageText = error;
            return null;
        }
        return result;
    }

    private LoadedFile BuildPreview(string normalizedContent, LocalFileInfo artifact)
    {
        if (artifact.Size > PreviewPlainLimitBytes)
        {
            return new LoadedFile(null, normalizedContent,
                $"文件较大（{FormatSize.Readable(artifact.Size)}），已跳过语法高亮");
        }
        try
        {
            var parsed = _parser.ParseCode(normalizedContent, artifact.Path);
            Trace.Mark("parse.ok", artifact.Name);
            return new LoadedFile(parsed, normalizedContent);
        }
        catch (Exception ex)
        {
            Trace.Mark("parse.fail", ex.Message);
            return new LoadedFile(null, normalizedContent);
        }
    }

    private string CacheKey(LocalFileInfo artifact) => $"{artifact.Path}|{artifact.LastMod}|{artifact.Size}";

    private string? ReadGitIgnore(string path) => _fs.TryReadText(Path.Combine(path, ".gitignore"));

    // 总大小（BFS 批并发，避免对超大仓库发起同数量级并发系统调用）

    private async Task<long> ComputeTotalSize(string path, CancellationToken ct)
    {
        long total = 0;
        var queue = new Queue<string>();
        queue.Enqueue(path);
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<string>();
            while (batch.Count < SizeScanConcurrency && queue.Count > 0) batch.Add(queue.Dequeue());

            var lists = await Task.WhenAll(batch.Select(dir => Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try { return _fs.ListFiles(dir); }
                catch (Exception) { return (IReadOnlyList<LocalFileInfo>)Array.Empty<LocalFileInfo>(); }
            }, ct)));

            foreach (var items in lists)
            {
                foreach (var item in items)
                {
                    if (item.IsDir) queue.Enqueue(item.Path);
                    else total += item.Size;
                }
            }
        }
        return total;
    }

    public async Task ExpandAll()
    {
        var queue = new Queue<TreeItemViewModel>();
        foreach (var root in TreeItems) if (root.IsDir) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var batch = new List<TreeItemViewModel>();
            while (batch.Count < ExpandConcurrency && queue.Count > 0) batch.Add(queue.Dequeue());
            await Task.WhenAll(batch.Select(item => item.EnsureLoadedAsync(_treeOwnerCts?.Token ?? CancellationToken.None)));
            foreach (var item in batch)
            {
                foreach (var child in item.Children)
                {
                    if (!child.IsPlaceholder && child.IsDir) queue.Enqueue(child);
                }
            }
        }
    }

    public void CollapseAll()
    {
        foreach (var root in TreeItems) root.CollapseRecursive();
    }

    public async Task RefreshTree()
    {
        if (!HasWorkspace) return;
        CancelAndDispose(ref _treeRefreshCts);
        _treeRefreshCts = new CancellationTokenSource();
        var ct = _treeRefreshCts.Token;
        var path = CurrentPath;
        var oldRoots = TreeItems.ToList();
        List<TreeItemViewModel> newRoots;
        try
        {
            var items = await Task.Run(() => _fs.ListFiles(path), ct);
            if (ct.IsCancellationRequested) return;
            var ignored = await Task.Run(() => GitIgnore.Parse(ReadGitIgnore(path)), ct);
            if (ct.IsCancellationRequested) return;
            newRoots = GitIgnore.Filter(items, ignored).Select(info =>
                new TreeItemViewModel(info, _fs, _treeOwnerCts?.Token ?? CancellationToken.None,
                    () => OnTreeDirsChanged?.Invoke(), ignored)).ToList();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            MessageText = $"刷新失败: {ex.Message}";
            return;
        }

        TreeItems = new ObservableCollection<TreeItemViewModel>(newRoots);
        await RefreshExpandedSubtree(oldRoots, newRoots, ct);
        if (ct.IsCancellationRequested) return;
        ReattachSelection();
        OnTreeDirsChanged?.Invoke();
    }

    private async Task RefreshExpandedSubtree(
        List<TreeItemViewModel> oldRoots, List<TreeItemViewModel> newRoots, CancellationToken ct)
    {
        var queue = new Queue<(TreeItemViewModel New, TreeItemViewModel Old)>();
        foreach (var newRoot in newRoots)
        {
            var oldRoot = oldRoots.FirstOrDefault(o => o.IsDir && o.IsExpanded && o.Info.Name == newRoot.Info.Name);
            if (oldRoot is not null)
            {
                newRoot.IsExpanded = true;
                queue.Enqueue((newRoot, oldRoot));
            }
        }
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
        var batch = new List<(TreeItemViewModel New, TreeItemViewModel Old)>();
        while (batch.Count < ExpandConcurrency && queue.Count > 0) batch.Add(queue.Dequeue());
        await Task.WhenAll(batch.Select(p => p.New.RefreshFrom(p.Old, ct)));
            if (ct.IsCancellationRequested) return;
            foreach (var (node, old) in batch)
            {
                foreach (var child in node.Children)
                {
                    if (child.IsPlaceholder || !child.IsDir || !child.IsExpanded) continue;
                    var oldChild = old.Children.FirstOrDefault(c => c.Info.Name == child.Info.Name);
                    if (oldChild is not null) queue.Enqueue((child, oldChild));
                }
            }
        }
    }

    private void ReattachSelection()
    {
        var id = SelectedArtifact?.Path;
        if (id is null) return;
        var found = FindNodeById(TreeItems, id);
        if (found is not null && found != _selectedTreeItem)
        {
            _selectedTreeItem?.IsSelected = false;
            _selectedTreeItem = found;
            found.IsSelected = true;
        }
        else if (found is null)
        {
            _selectedTreeItem?.IsSelected = false;
            _selectedTreeItem = null;
        }
    }

    private static TreeItemViewModel? FindNodeById(IEnumerable<TreeItemViewModel> items, string id)
    {
        foreach (var item in items)
        {
            if (item.IsPlaceholder) continue;
            if (item.Info.Path == id) return item;
            if (item.IsDir && item.IsExpanded)
            {
                var found = FindNodeById(item.Children, id);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>供文件树实时监听（#9 DirectoryWatcher）注册已展开目录路径。</summary>
    public List<string> CurrentExpandedDirPaths()
    {
        var result = new List<string>();
        var queue = new Queue<TreeItemViewModel>();
        foreach (var root in TreeItems)
            if (root.IsDir && root.IsExpanded) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node.Info.Path);
            foreach (var child in node.Children)
                if (!child.IsPlaceholder && child.IsDir && child.IsExpanded) queue.Enqueue(child);
        }
        return result;
    }

    private void CancelLoadJobs()
    {
        CancelAndDispose(ref _loadCts);
        CancelAndDispose(ref _selectCts);
        CancelAndDispose(ref _sizeCts);
        CancelAndDispose(ref _treeRefreshCts);
        CancelAndDispose(ref _treeOwnerCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    public void Dispose()
    {
        CancelLoadJobs();
        _scopeCts.Cancel();
        _scopeCts.Dispose();
    }

    private sealed record LoadedFile(ParsedCode? ParseResult, string Content, string? Notice = null);

    /// <summary>访问序 LRU（容量上限 = 解析缓存上限），基于"https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlist-1"</summary>
    private sealed class LruCache
    {
        private readonly int _capacity;
        private readonly LinkedList<KeyValuePair<string, LoadedFile>> _list = new();
        private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, LoadedFile>>> _map = new();

        public LruCache(int capacity) => _capacity = capacity;

        public bool TryGet(string key, out LoadedFile? value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddLast(node); // 命中即移到最近端
                value = node.Value.Value;
                return true;
            }
            value = null;
            return false;
        }

        public void Set(string key, LoadedFile value)
        {
            if (_map.TryGetValue(key, out var existing)) _list.Remove(existing);
            else if (_map.Count >= _capacity)
            {
                var oldest = _list.First!;
                _list.RemoveFirst();
                _map.Remove(oldest.Value.Key);
            }
            var node = new LinkedListNode<KeyValuePair<string, LoadedFile>>(new KeyValuePair<string, LoadedFile>(key, value));
            _list.AddLast(node);
            _map[key] = node;
        }

        public void Clear()
        {
            _list.Clear();
            _map.Clear();
        }
    }
}
