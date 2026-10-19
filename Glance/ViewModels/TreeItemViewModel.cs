using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// 目录列表容错catch等价于KMP的listResult.getOrNull() ?: emptyList()，属有意设计，屏蔽CA1031（https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1031）。
#pragma warning disable CA1031
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.Models;
using Glance.Repositories;
using Glance.Utils;
using Material.Icons;

namespace Glance.ViewModels;

/// <summary>
/// 文件树节点VM
/// 集合用"https://learn.microsoft.com/dotnet/api/system.collections.objectmodel.observablecollection-1"
/// 替代Compose SnapshotStateList；异步用
/// "https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/"替代协程。
/// </summary>
public partial class TreeItemViewModel : ViewModelBase
{
    private readonly IArtifact _artifact;
    private readonly IArtifactRepo? _repo;
    private readonly CancellationToken _owner;
    private readonly Action? _onDirsChanged;
    private readonly GitIgnoreDirs _gitIgnored;
    // https://learn.microsoft.com/dotnet/api/system.threading.semaphoreslim
    private readonly SemaphoreSlim _opGate = new(1, 1);

    public IArtifact Artifact => _artifact;

    // 平移自KMP fileIconFor
    public MaterialIconKind IconKind { get; }

    public bool IsDir => _artifact.Payload is LocalPayload p && p.IsDir;

    public string Name => _artifact.Name;

    /// <summary>占位符节点（空目录或未加载目录的首个子项）</summary>
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

    // 目录显示N项、文件显示可读大小
    public string SizeDisplay =>
        IsDir ? (ChildCount == 0 ? "" : $"{ChildCount} 项") : FormatSize.Readable(_artifact.Size);

    public TreeItemViewModel(
        IArtifact artifact,
        IArtifactRepo repo,
        CancellationToken owner = default,
        Action? onDirsChanged = null,
        GitIgnoreDirs? gitIgnored = null)
    {
        _artifact = artifact;
        _repo = repo;
        _owner = owner;
        _onDirsChanged = onDirsChanged;
        _gitIgnored = gitIgnored ?? GitIgnoreDirs.Empty;
        IsPlaceholder = false;
        IconKind = FileIcon.For(artifact.Extension, IsDir);
        if (IsDir) Children.Add(new TreeItemViewModel()); // 占位符
    }

    private TreeItemViewModel()
    {
        _artifact = new PlaceholderArtifact();
        _repo = null;
        _gitIgnored = GitIgnoreDirs.Empty;
        IsPlaceholder = true;
        IconKind = MaterialIconKind.FolderOutline;
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
        if (!IsDir || _repo is null) return;
        await _opGate.WaitAsync(ct);
        try
        {
            IsLoading = true;
            var path = _artifact.Payload is LocalPayload p ? p.AbsolutePath : _artifact.Id;
            var items = await Task.Run(async () => await _repo!.ListAsync(path), ct);
            var sorted = GitIgnore.Filter(items.GetOrNull() ?? Array.Empty<IArtifact>(), _gitIgnored)
                .OrderBy(a => !(a.Payload is LocalPayload lp && lp.IsDir))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ChildCount = sorted.Count;
            Children.Clear();
            foreach (var child in sorted)
            {
                Children.Add(new TreeItemViewModel(child, _repo, _owner, _onDirsChanged, _gitIgnored));
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

    // 刷新重建保留旧节点已展开的子目录展开态
    public async Task RefreshFrom(TreeItemViewModel oldNode, CancellationToken ct = default)
    {
        if (!IsDir || _repo is null) return;
        await _opGate.WaitAsync(ct);
        try
        {
            IsLoading = true;
            var path = _artifact.Payload is LocalPayload p ? p.AbsolutePath : _artifact.Id;
            var items = await Task.Run(async () => await _repo!.ListAsync(path), ct);
            var expandedChildNames = oldNode.Children
                .Where(c => c.IsDir && c.IsExpanded)
                .Select(c => c.Name)
                .ToHashSet();
            var sorted = GitIgnore.Filter(items.GetOrNull() ?? Array.Empty<IArtifact>(), _gitIgnored)
                .OrderBy(a => !(a.Payload is LocalPayload lp && lp.IsDir))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ChildCount = sorted.Count;
            Children.Clear();
            foreach (var child in sorted)
            {
                var newChild = new TreeItemViewModel(child, _repo, _owner, _onDirsChanged, _gitIgnored);
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

    // 供MainViewModel.expandAll分批调用
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

    private sealed class PlaceholderArtifact : IArtifact
    {
        public string Id => "";
        public string Name => "";
        public long Size => 0;
        public long LastMod => 0;
        public string Extension => "";
        public ArtifactKind Kind => ArtifactKind.Text;
        public ArtifactSource Source => ArtifactSource.Local;
        public ArtifactStatus Status => ArtifactStatus.Available;
        public ArtifactMetadata? Metadata => null;
        public IArtifactPayload Payload => new LocalPayload("", "", false);
    }
}
