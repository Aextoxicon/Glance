using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
// VM顶层操作刻意吞掉具体异常、转成MessageText展示给用户纯属有意设计，屏蔽CA1031（https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031）
#pragma warning disable CA1031
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Models;
using Glance.Native;
using Glance.Processing;
using Glance.Repositories;
using Glance.Utils;

namespace Glance.ViewModels;

/// <summary>
/// 平移自KMP MainViewModel.kt；协程模型见
/// "https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/"
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int WideModeThreshold = 640;
    private const int ParseCacheMax = 32;
    private const long PreviewHardLimitBytes = 10L * 1024 * 1024;
    private const long PreviewPlainLimitBytes = 2L * 1024 * 1024;
    private const int SizeScanConcurrency = 8;
    private const int ExpandConcurrency = 8;
    private const string NativeUnavailableNotice = "本机解析库不可用，已降级为纯文本预览";

    public const int DefaultCodeFontSize = 13;
    public static readonly int[] CodeFontSizes = { 10, 12, 13, 14, 16, 18, 20, 24, 28 };

    private readonly ILocalFileSystem _fs;
    private readonly IArtifactRepo _repo;
    private readonly ICodeParser _parser;
    private readonly CancellationTokenSource _scopeCts = new();
    private readonly object _cacheLock = new();
    private readonly LruCache _parseCache;
    private bool _nativeAvailable = true;
    private Application? _app;
    private bool _disposed;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _selectCts;
    private CancellationTokenSource? _sizeCts;
    private CancellationTokenSource? _treeRefreshCts;
    private CancellationTokenSource? _treeOwnerCts;
    private TreeItemViewModel? _selectedTreeItem;

#if !ANDROID
    private readonly DirectoryWatcher? _dirWatcher;
#endif

    public Action? OnWorkspaceOpened { get; set; }
    public Action? OnTreeDirsChanged { get; set; }

    public Func<Task<string?>>? PickFolderAction { get; set; }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";

    [ObservableProperty]
    private ThemeMode _themeMode = ThemeMode.System;

    [ObservableProperty]
    private bool _isDark;

    [ObservableProperty]
    private bool _rememberLastDir;

    [ObservableProperty]
    private string? _lastDirectory;

    [ObservableProperty]
    private string _currentPath = "";

    // 工作区根的显示名，由LoadCore从FileInfo取一次
    [ObservableProperty]
    private string _currentFolderName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSizeReadable))]
    private long _totalSize;

    [ObservableProperty]
    private bool _isComputingSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSizeDisplay))]
    private IArtifact? _selectedArtifact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    private string? _selectedContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    private string? _messageText;

    public bool HasMessage => !string.IsNullOrEmpty(MessageText);

    // 已选中但拿不到内容（二进制/超大/读取失败）时，把原因画在预览区，避免看起来像一个空文件
    public bool HasPreviewMessage =>
        HasSelection && SelectedContent is null && IsLoadingPreview is false && HasMessage;

    [ObservableProperty]
    private string? _previewNotice;

    [ObservableProperty]
    private ParsedCode? _selectedParseResult;

    [ObservableProperty]
    private ObservableCollection<TreeItemViewModel> _treeItems = new();

    [ObservableProperty]
    private bool _hasWorkspace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    private bool _hasSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    private bool _isLoadingPreview;

    [ObservableProperty]
    private string? _treeLoadMessage;

    [ObservableProperty]
    private bool _showNarrowFileName;

    [ObservableProperty]
    private bool _isWide = true;

    [ObservableProperty]
    private int _codeFontSize = DefaultCodeFontSize;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    [ObservableProperty]
    private bool _traceEnabled;

    partial void OnTraceEnabledChanged(bool value) => Trace.Enabled = value;

    [ObservableProperty]
    private bool _outlineOpen = false;

    [ObservableProperty]
    private int? _outlineScrollTargetLine;

    public string TotalSizeReadable => FormatSize.Readable(TotalSize);
    public string SelectedSizeDisplay => SelectedArtifact is not null ? FormatSize.Readable(SelectedArtifact.Size) : "";

    // 走ILocalFileSystem而不是Path.*：Android的路径是content://的SAF URI
    private string SafeDisplayName(string path)
    {
        try
        {
            var name = _fs.FileInfo(path).Name;
            return name.Length > 0 ? name : "工作区";
        }
        catch (Exception)
        {
            return "工作区";
        }
    }

    public IReadOnlyList<OutlineNode>? CurrentOutline =>
        SelectedParseResult?.Outline is { Count: > 0 } outline ? outline : null;

    public bool HasOutline => CurrentOutline is not null;
    public bool OutlineEmpty => !HasOutline;

    public IReadOnlyList<OutlineRow> OutlineRows
    {
        get
        {
            var src = CurrentOutline;
            if (src is null) return Array.Empty<OutlineRow>();
            var rows = new List<OutlineRow>();
            FlattenOutline(src, 0, rows);
            return rows;
        }
    }

    private static void FlattenOutline(IEnumerable<OutlineNode> nodes, int depth, List<OutlineRow> rows)
    {
        foreach (var node in nodes)
        {
            rows.Add(new OutlineRow(node, depth));
            if (node.Children is { Length: > 0 }) FlattenOutline(node.Children, depth + 1, rows);
        }
    }

    public MainViewModel() : this(new LocalFileSystem(), new UniffiCodeParser()) { }

    public MainViewModel(ILocalFileSystem fs, ICodeParser parser) : this(fs, new LocalArtifactRepo(fs), parser) { }

    public MainViewModel(ILocalFileSystem fs, IArtifactRepo repo, ICodeParser parser)
    {
        _fs = fs;
        _repo = repo;
        _parser = parser;
        _parseCache = new LruCache(ParseCacheMax);
        _nativeAvailable = ProbeNative();
#if !ANDROID
        _dirWatcher = new DirectoryWatcher(OnDirChanged);
        OnTreeDirsChanged += SyncWatcher;
#endif
        var saved = SettingsStore.Load();
        _themeMode = saved.ThemeMode;
        _codeFontSize = saved.CodeFontSize > 0 ? saved.CodeFontSize : DefaultCodeFontSize;
        _rememberLastDir = saved.RememberLastDir;
        _lastDirectory = saved.LastDirectory;
        _showLineNumbers = saved.ShowLineNumbers;
        IsDark = ResolveDark();
        _app = Application.Current;
        if (_app is not null)
            _app.ActualThemeVariantChanged += OnAppThemeVariantChanged;
    }

    public void TryRestoreLastDirectory()
    {
        if (RememberLastDir && !string.IsNullOrEmpty(LastDirectory))
            _ = LoadCore(LastDirectory!);
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
        IsDark = ResolveDark();
        PersistSettings();
    }

    partial void OnCodeFontSizeChanged(int value) => PersistSettings();

    partial void OnShowLineNumbersChanged(bool value) => PersistSettings();

    partial void OnIsWideChanged(bool value) => ShowNarrowFileName = !value && HasSelection;
    partial void OnHasSelectionChanged(bool value) => ShowNarrowFileName = !IsWide && value;

    partial void OnRememberLastDirChanged(bool value)
    {
        if (!value) LastDirectory = null;
        PersistSettings();
    }

    private void PersistSettings()
    {
        SettingsStore.Save(new AppSettings
        {
            ThemeMode = ThemeMode,
            CodeFontSize = CodeFontSize,
            ShowLineNumbers = ShowLineNumbers,
            RememberLastDir = RememberLastDir,
            LastDirectory = RememberLastDir ? LastDirectory : null,
        });
    }

    private bool ResolveDark()
    {
        if (ThemeMode == ThemeMode.Light) return false;
        if (ThemeMode == ThemeMode.Dark) return true;
        var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        return variant == ThemeVariant.Dark;
    }

    private void OnAppThemeVariantChanged(object? sender, EventArgs e) => IsDark = ResolveDark();

    partial void OnPreviewNoticeChanged(string? value) =>
        OnPropertyChanged(nameof(HasPreviewNotice));

    partial void OnSelectedParseResultChanged(ParsedCode? value)
    {
        OnPropertyChanged(nameof(HasOutline));
        OnPropertyChanged(nameof(OutlineEmpty));
        OnPropertyChanged(nameof(OutlineRows));
    }

    public bool HasPreviewNotice => PreviewNotice is not null;
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
        CurrentFolderName = "";
        HasWorkspace = false;
        TreeItems = new ObservableCollection<TreeItemViewModel>();
        TotalSize = 0;
        SelectedArtifact = null;
        SelectedContent = null;
        SelectedParseResult = null;
        HasSelection = false;
        IsLoadingPreview = false;
        MessageText = null;
        TreeLoadMessage = null;
        PreviewNotice = null;
        OnTreeDirsChanged?.Invoke();
    }

    public string? ExportTraceLog()
    {
        if (!Trace.Enabled)
        {
            MessageText = "诊断追踪未启用，无可导出日志";
            return null;
        }
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance");
            var path = Trace.ExportTo(dir);
            MessageText = path is null ? "暂无可导出日志" : $"日志已导出: {path}";
            return path;
        }
        catch (Exception ex)
        {
            MessageText = $"导出日志失败: {ex.Message}";
            return null;
        }
    }

    public async Task LoadCore(string path)
    {
        CancelLoadJobs();
        _loadCts = new CancellationTokenSource();
        _selectedTreeItem?.IsSelected = false;
        _selectedTreeItem = null;
        _treeOwnerCts = new CancellationTokenSource();
        CurrentPath = path;
        LastDirectory = path;
        HasWorkspace = true;
        TreeLoadMessage = null;
        lock (_cacheLock) _parseCache.Clear();
        TotalSize = 0;
        TreeItems = new ObservableCollection<TreeItemViewModel>();

        var ignored = GitIgnoreDirs.Empty;
        try
        {
            // 根目录只列一次
            var listed = await Task.Run(async () =>
            {
                var list = await _repo.ListAsync(path);
                var entries = list.GetOrNull() ?? Array.Empty<IArtifact>();
                var gitignore = entries.FirstOrDefault(i => i.Name == ".gitignore");
                var text = gitignore is null
                    ? null
                    : (await _repo.TryReadTextAsync(gitignore.Id)).GetOrNull();
                return (List: list, Entries: entries, GitIgnoreText: text, Name: SafeDisplayName(path));
            }, _loadCts.Token);

            ignored = GitIgnore.Parse(listed.GitIgnoreText);
            if (CurrentPath != path) return;
            if (!listed.List.IsSuccess)
            {
                var failure = listed.List as Failure<IReadOnlyList<IArtifact>>;
                TreeLoadMessage = $"加载失败: {failure?.Error.Message ?? "未知错误"}";
                return;
            }
            CurrentFolderName = listed.Name;
            TreeItems = new ObservableCollection<TreeItemViewModel>(
                GitIgnore.Filter(listed.Entries, ignored).Select(artifact =>
                    new TreeItemViewModel(artifact, _repo, _treeOwnerCts.Token, () => OnTreeDirsChanged?.Invoke(), ignored)));
            TreeLoadMessage = null;
            OnWorkspaceOpened?.Invoke();
            PersistSettings();
#if !ANDROID
            SyncWatcher();
#endif
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var msg = $"加载失败: {ex.Message}";
            MessageText = msg;
            TreeLoadMessage = msg;
        }

        IsComputingSize = true;
        _sizeCts = new CancellationTokenSource();
        var sizePath = path;
        try
        {
            var size = await ComputeTotalSize(sizePath, ignored, _sizeCts.Token);
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
        IsLoadingPreview = false;
        MessageText = null;
        PreviewNotice = null;
    }

    public async Task SelectItem(TreeItemViewModel? item)
    {
        if (item is null || item.IsPlaceholder) return;
        if (item.IsDir)
        {
            item.ToggleExpanded();
            return;
        }
        _selectedTreeItem?.IsSelected = false;
        item.IsSelected = true;
        _selectedTreeItem = item;
        CancelAndDispose(ref _selectCts);
        _selectCts = new CancellationTokenSource();
        await SelectFile(item.Artifact, _selectCts.Token);
    }

    private async Task SelectFile(IArtifact artifact, CancellationToken ct)
    {
        SelectedArtifact = artifact;
        HasSelection = true;
        MessageText = null;
        SelectedContent = null;
        SelectedParseResult = null;
        PreviewNotice = null;
        IsLoadingPreview = true;

        Trace.Mark("select.start", $"name={artifact.Name} size={artifact.Size}");
        var loaded = await LoadFile(artifact, ct);
        if (ct.IsCancellationRequested) return;
        IsLoadingPreview = false;
        if (loaded is null) return;
        SelectedParseResult = loaded.ParseResult;
        SelectedContent = loaded.Content;
        PreviewNotice = loaded.Notice;
        Trace.Mark("select.end", $"name={artifact.Name}");
    }

    private async Task<LoadedFile?> LoadFile(IArtifact artifact, CancellationToken ct)
    {
        if (artifact.Payload is LocalPayload lp && lp.IsDir) return null;
        var path = artifact.Id;
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
            await Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                if (!_fs.IsTextFile(path))
                {
                    Trace.Mark("istext", "binary=true");
                    error = $"[二进制文件] {artifact.Name} 无法预览";
                    return;
                }
                Trace.Mark("istext", "binary=false");
                var key = CacheKey(artifact);
                lock (_cacheLock)
                {
                    if (_parseCache.TryGet(key, out var cached))
                    {
                        Trace.Mark("cache.hit", $"bytes={artifact.Size}");
                        result = cached;
                        return;
                    }
                }
                Trace.Mark("cache.miss", $"bytes={artifact.Size}");
                var content = (await _repo.TryReadTextAsync(path)).GetOrNull();
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

    private LoadedFile BuildPreview(string normalizedContent, IArtifact artifact)
    {
        if (!_nativeAvailable)
        {
            return new LoadedFile(null, normalizedContent, NativeUnavailableNotice);
        }
        if (artifact.Size > PreviewPlainLimitBytes)
        {
            return new LoadedFile(null, normalizedContent,
                $"文件较大（{FormatSize.Readable(artifact.Size)}），已跳过语法高亮");
        }
        if (FileProcessor.IsPlainText(artifact))
        {
            return new LoadedFile(null, normalizedContent);
        }
        var parseName = FileProcessor.ResolveParseName(artifact);
        try
        {
            var parsed = _parser.ParseCode(normalizedContent, parseName);
            Trace.Mark("parse.ok", artifact.Name);
            return new LoadedFile(parsed, normalizedContent);
        }
        catch (Exception ex) when (IsNativeUnavailable(ex))
        {
            _nativeAvailable = false;
            Trace.Mark("parse.native_unavailable", artifact.Name);
            return new LoadedFile(null, normalizedContent, NativeUnavailableNotice);
        }
        catch (Exception ex)
        {
            Trace.Mark("parse.fail", ex.Message);
            return new LoadedFile(null, normalizedContent);
        }
    }

    // 避免Android无.so时每个文件重复触发DllNotFoundException
    private static bool ProbeNative()
    {
        if (NativeLibrary.TryLoad("uniffi_code_parser", out var handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }
        return false;
    }

    private static bool IsNativeUnavailable(Exception ex) =>
        ex is DllNotFoundException or TypeLoadException or BadImageFormatException;

    private string CacheKey(IArtifact artifact) => $"{artifact.Id}|{artifact.LastMod}|{artifact.Size}";

    // 总大小（BFS批并发，避免对超大仓库发起同数量级并发系统调用）

    private async Task<long> ComputeTotalSize(string path, GitIgnoreDirs ignored, CancellationToken ct)
    {
        long total = 0;
        var queue = new Queue<string>();
        queue.Enqueue(path);
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<string>();
            while (batch.Count < SizeScanConcurrency && queue.Count > 0) batch.Add(queue.Dequeue());

            var lists = await Task.WhenAll(batch.Select(dir => Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                return (await _repo.ListAsync(dir)).GetOrNull() ?? Array.Empty<IArtifact>();
            }, ct)));

            foreach (var items in lists)
            {
                foreach (var item in GitIgnore.Filter(items, ignored))
                {
                    if (item.Payload is LocalPayload p && p.IsDir) queue.Enqueue(p.AbsolutePath);
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
        try
        {
            while (queue.Count > 0)
            {
                var batch = new List<TreeItemViewModel>();
                while (batch.Count < ExpandConcurrency && queue.Count > 0) batch.Add(queue.Dequeue());
                await Task.WhenAll(batch.Select(item => item.EnsureLoadedAsync()));
                foreach (var item in batch)
                {
                    foreach (var child in item.Children)
                    {
                        if (!child.IsPlaceholder && child.IsDir) queue.Enqueue(child);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void CollapseAll()
    {
        foreach (var root in TreeItems) root.CollapseRecursive();
        OnTreeDirsChanged?.Invoke();
    }

    public async Task RefreshTree()
    {
        if (!HasWorkspace) return;
        CancelAndDispose(ref _treeRefreshCts);
        _treeRefreshCts = new CancellationTokenSource();
        var ct = _treeRefreshCts.Token;
        var path = CurrentPath;
        try
        {
            var listed = await Task.Run(async () =>
            {
                var list = await _repo.ListAsync(path);
                var entries = list.GetOrNull() ?? Array.Empty<IArtifact>();
                var gitignore = entries.FirstOrDefault(i => i.Name == ".gitignore");
                var text = gitignore is null
                    ? null
                    : (await _repo.TryReadTextAsync(gitignore.Id)).GetOrNull();
                return (List: list, Entries: entries, GitIgnoreText: text);
            }, ct);
            if (ct.IsCancellationRequested) return;
            if (!listed.List.IsSuccess)
            {
                var failure = listed.List as Failure<IReadOnlyList<IArtifact>>;
                MessageText = $"刷新失败: {failure?.Error.Message ?? "未知错误"}";
                return;
            }
            var ignored = GitIgnore.Parse(listed.GitIgnoreText);
            if (ct.IsCancellationRequested) return;
            ReconcileRoots(GitIgnore.Filter(listed.Entries, ignored), ignored);
            await ReconcileExpandedDirs(ignored, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            MessageText = $"刷新失败: {ex.Message}";
            return;
        }

        if (ct.IsCancellationRequested) return;
        ReattachSelection();
        OnTreeDirsChanged?.Invoke();
    }

    private void ReconcileRoots(IReadOnlyList<IArtifact> sorted, GitIgnoreDirs ignored)
    {
        var byId = new Dictionary<string, TreeItemViewModel>(TreeItems.Count, StringComparer.Ordinal);
        foreach (var root in TreeItems)
            if (!root.IsPlaceholder) byId.TryAdd(root.Artifact.Id, root);

        var desired = new List<TreeItemViewModel>(sorted.Count);
        foreach (var artifact in sorted)
        {
            var isDir = artifact.Payload is LocalPayload p && p.IsDir;
            if (byId.TryGetValue(artifact.Id, out var node) && node.IsDir == isDir)
            {
                node.UpdateArtifact(artifact);
                desired.Add(node);
            }
            else
            {
                desired.Add(new TreeItemViewModel(artifact, _repo, _treeOwnerCts?.Token ?? CancellationToken.None,
                    () => OnTreeDirsChanged?.Invoke(), ignored));
            }
        }

        var keep = new HashSet<TreeItemViewModel>(desired);
        for (var i = TreeItems.Count - 1; i >= 0; i--)
            if (!keep.Contains(TreeItems[i])) TreeItems.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            var node = desired[i];
            if (i < TreeItems.Count && ReferenceEquals(TreeItems[i], node)) continue;
            var from = TreeItems.IndexOf(node);
            if (from >= 0) TreeItems.Move(from, i);
            else TreeItems.Insert(i, node);
        }
    }

    private async Task ReconcileExpandedDirs(GitIgnoreDirs ignored, CancellationToken ct)
    {
        var queue = new Queue<TreeItemViewModel>();
        foreach (var root in TreeItems)
            if (root.IsDir && root.IsExpanded) queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<TreeItemViewModel>();
            while (batch.Count < ExpandConcurrency && queue.Count > 0) batch.Add(queue.Dequeue());

            var loaded = await Task.WhenAll(batch.Select(dir => LoadSortedChildrenAsync(dir, ignored, ct)));
            if (ct.IsCancellationRequested) return;

            for (var i = 0; i < batch.Count; i++)
            {
                if (loaded[i] is not { } children) continue;
                batch[i].ReconcileChildren(children, ignored);
                foreach (var child in batch[i].Children)
                    if (child.IsDir && child.IsExpanded) queue.Enqueue(child);
            }
        }
    }

    private async Task<IReadOnlyList<IArtifact>?> LoadSortedChildrenAsync(
        TreeItemViewModel dir, GitIgnoreDirs ignored, CancellationToken ct)
    {
        var path = dir.Artifact.Payload is LocalPayload p ? p.AbsolutePath : dir.Artifact.Id;
        var result = await Task.Run(async () => await _repo.ListAsync(path), ct);
        if (result.GetOrNull() is not { } items) return null;
        return GitIgnore.Filter(items, ignored)
            .OrderBy(a => !(a.Payload is LocalPayload lp && lp.IsDir))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReattachSelection()
    {
        var id = SelectedArtifact?.Id;
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
            if (item.Artifact.Id == id) return item;
            if (item.IsDir && item.IsExpanded)
            {
                var found = FindNodeById(item.Children, id);
                if (found is not null) return found;
            }
        }
        return null;
    }

    // 供文件树定期扫描(DirectoryWatcher)注册已展开目录路径
    public List<string> CurrentExpandedDirPaths()
    {
        var result = new List<string>();
        var queue = new Queue<TreeItemViewModel>();
        foreach (var root in TreeItems)
            if (root.IsDir && root.IsExpanded) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node.Artifact.Id);
            foreach (var child in node.Children)
                if (!child.IsPlaceholder && child.IsDir && child.IsExpanded) queue.Enqueue(child);
        }
        return result;
    }

#if !ANDROID
    private void OnDirChanged()
    {
        Dispatcher.UIThread.Post(() => _ = RefreshTree(), DispatcherPriority.Background);
    }

    private void SyncWatcher() => _dirWatcher?.SyncRoot(CurrentPath, CurrentExpandedDirPaths());
#endif

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
        if (_disposed)
            return;
        _disposed = true;

        if (_app is not null)
        {
            _app.ActualThemeVariantChanged -= OnAppThemeVariantChanged;
            _app = null;
        }
        CancelLoadJobs();
#if !ANDROID
        _dirWatcher?.Dispose();
#endif
        _scopeCts.Cancel();
        _scopeCts.Dispose();
    }

    private sealed record LoadedFile(ParsedCode? ParseResult, string Content, string? Notice = null);

    /// <summary>访问序LRU（容量上限 = 解析缓存上限），基于"https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlist-1"</summary>
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