using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// 目录列表容错 catch 等价于 KMP 的 listResult.getOrNull() ?: emptyList()，属有意设计，屏蔽 CA1031（https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031）。
#pragma warning disable CA1031
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Repositories;
using Glance.Utils;

namespace Glance.ViewModels;

/// <summary>
/// 文件树节点 VM。平移自 KMP TreeItemViewModel.kt。
/// 集合用 <see href="https://learn.microsoft.com/dotnet/api/system.collections.objectmodel.observablecollection-1">ObservableCollection</see>
/// 替代 Compose SnapshotStateList；异步用
/// <see href="https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/">Task + CancellationToken</see> 替代协程。
/// </summary>
public partial class TreeItemViewModel : ViewModelBase
{
    private static readonly LocalFileInfo PlaceholderInfo = new("", "", "", false, 0, 0, "");

    private readonly ILocalFileSystem _fs;
    private readonly CancellationToken _owner;
    private readonly Action? _onDirsChanged;
    private readonly GitIgnoreDirs _gitIgnored;
    // https://learn.microsoft.com/dotnet/api/system.threading.semaphoreslim
    private readonly SemaphoreSlim _opGate = new(1, 1);

    public LocalFileInfo Info { get; }

    public bool IsDir => Info.IsDir;

    /// <summary>占位符节点（空目录 / 未加载目录的首个子项）</summary>
    public bool IsPlaceholder { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _childCount;

    public ObservableCollection<TreeItemViewModel> Children { get; } = new();

    /// <summary>目录显示「N 项」，文件显示可读大小。依赖ChildCount/Info.Size</summary>
    public string SizeDisplay =>
        IsDir ? (ChildCount == 0 ? "" : $"{ChildCount} 项") : FormatSize.Readable(Info.Size);

    public TreeItemViewModel(
        LocalFileInfo info,
        ILocalFileSystem fs,
        CancellationToken owner = default,
        Action? onDirsChanged = null,
        GitIgnoreDirs? gitIgnored = null)
    {
        Info = info;
        _fs = fs;
        _owner = owner;
        _onDirsChanged = onDirsChanged;
        _gitIgnored = gitIgnored ?? GitIgnoreDirs.Empty;
        IsPlaceholder = false;
        if (IsDir) Children.Add(new TreeItemViewModel()); // 占位符
    }

    private TreeItemViewModel()
    {
        Info = PlaceholderInfo;
        _fs = null!;
        _gitIgnored = GitIgnoreDirs.Empty;
        IsPlaceholder = true;
    }

    public void ToggleExpanded()
    {
        if (!IsDir) return;
        if (!IsExpanded) Expand();
        else Collapse();
    }

    public void Expand()
    {
        if (!IsDir) return;
        IsExpanded = true;
        _onDirsChanged?.Invoke();
        // 仅在展开时 + 尚未加载子节点时加载
        if (Children.Count > 0 && Children[0].IsPlaceholder)
        {
            _ = LoadChildrenAsync(_owner);
        }
    }

    public void Collapse()
    {
        if (!IsDir) return;
        IsExpanded = false;
        _onDirsChanged?.Invoke();
    }

    public async Task LoadChildrenAsync(CancellationToken ct = default)
    {
        if (!IsDir || _fs is null) return;
        await _opGate.WaitAsync(ct);
        try
        {
            IsLoading = true;
            IReadOnlyList<LocalFileInfo> items;
            try { items = await Task.Run(() => _fs.ListFiles(Info.Path), ct); }
            catch (Exception) { items = Array.Empty<LocalFileInfo>(); }
            var sorted = GitIgnore.Filter(items, _gitIgnored)
                .OrderBy(d => !d.IsDir)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ChildCount = sorted.Count;
            Children.Clear();
            foreach (var child in sorted)
            {
                Children.Add(new TreeItemViewModel(child, _fs, _owner, _onDirsChanged, _gitIgnored));
            }
            // 空目录：保留占位符，保证箭头始终显示
            if (Children.Count == 0) Children.Add(new TreeItemViewModel());
        }
        finally
        {
            IsLoading = false;
            _opGate.Release();
        }
    }

    public void CollapseRecursive()
    {
        IsExpanded = false;
        foreach (var child in Children) child.CollapseRecursive();
    }

    /// <summary>刷新重建：保留旧节点中已展开的子目录展开态</summary>
    public async Task RefreshFrom(TreeItemViewModel oldNode, CancellationToken ct = default)
    {
        if (!IsDir || _fs is null) return;
        await _opGate.WaitAsync(ct);
        try
        {
            IsLoading = true;
            IReadOnlyList<LocalFileInfo> items;
            try { items = await Task.Run(() => _fs.ListFiles(Info.Path), ct); }
            catch (Exception) { items = Array.Empty<LocalFileInfo>(); }
            var expandedChildNames = oldNode.Children
                .Where(c => c.IsDir && c.IsExpanded)
                .Select(c => c.Info.Name)
                .ToHashSet();
            var sorted = GitIgnore.Filter(items, _gitIgnored)
                .OrderBy(d => !d.IsDir)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ChildCount = sorted.Count;
            Children.Clear();
            foreach (var child in sorted)
            {
                var newChild = new TreeItemViewModel(child, _fs, _owner, _onDirsChanged, _gitIgnored);
                if (newChild.IsDir && expandedChildNames.Contains(child.Name))
                {
                    newChild.IsExpanded = true;
                }
                Children.Add(newChild);
            }
            if (Children.Count == 0) Children.Add(new TreeItemViewModel());
        }
        finally
        {
            IsLoading = false;
            _opGate.Release();
        }
    }

    /// <summary>展开并等待子节点加载（供MainViewModel.expandAll分批调用）</summary>
    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (!IsDir) return;
        IsExpanded = true;
        _onDirsChanged?.Invoke();
        if (Children.Count > 0 && Children[0].IsPlaceholder)
        {
            await LoadChildrenAsync(ct);
        }
    }
}
