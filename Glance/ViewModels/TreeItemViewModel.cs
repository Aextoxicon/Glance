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
    private IArtifact _artifact;
    private readonly IArtifactRepo? _repo;
    private readonly CancellationToken _owner;
    private readonly Action? _onDirsChanged;
    private GitIgnoreDirs _gitIgnored;
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
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
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
            ReconcileChildren(sorted);
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

    public void UpdateArtifact(IArtifact artifact)
    {
        _artifact = artifact;
        OnPropertyChanged(nameof(SizeDisplay));
    }

    // 按Artifact.Id就地增删移，复用已有节点以保住容器身份与展开/选中态
    public void ReconcileChildren(IReadOnlyList<IArtifact> sorted, GitIgnoreDirs? gitIgnored = null)
    {
        if (_repo is null) return;
        if (gitIgnored is not null) _gitIgnored = gitIgnored;

        for (var i = Children.Count - 1; i >= 0; i--)
            if (Children[i].IsPlaceholder) Children.RemoveAt(i);

        var byId = new Dictionary<string, TreeItemViewModel>(Children.Count, StringComparer.Ordinal);
        for (var i = Children.Count - 1; i >= 0; i--)
            if (!byId.TryAdd(Children[i].Artifact.Id, Children[i])) Children.RemoveAt(i);

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
                if (node is not null) Children.Remove(node);
                desired.Add(new TreeItemViewModel(artifact, _repo, _owner, _onDirsChanged, _gitIgnored));
            }
        }

        var keep = new HashSet<TreeItemViewModel>(desired);
        for (var i = Children.Count - 1; i >= 0; i--)
            if (!keep.Contains(Children[i])) Children.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            var node = desired[i];
            if (i < Children.Count && ReferenceEquals(Children[i], node)) continue;
            var from = Children.IndexOf(node);
            if (from >= 0) Children.Move(from, i);
            else Children.Insert(i, node);
        }

        ChildCount = sorted.Count;
        if (Children.Count == 0) Children.Add(new TreeItemViewModel());
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
