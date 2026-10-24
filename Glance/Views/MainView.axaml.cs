using System;
using System.Diagnostics;
using Trace = Glance.Utils.Trace;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Glance.Native;
using Glance.Processing;
using Glance.Utils;
using Glance.ViewModels;

namespace Glance.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _vm;
    private TreeItemViewModel? _lastSelected;
    private HighlightTransformer? _transformer;
    private bool _treeDrawerOpen;
    private DispatcherTimer? _scrollTimer;
    private bool _paintPending;
    private ScrollViewer? _editorScroll;
    private readonly Stopwatch _scrollSw = Stopwatch.StartNew();
    private bool _scrollPaintPending;
    private const double ScrollJankMs = 20;

    private const int OutlineScrollContextLines = 3;
    private const int OutlineScrollPreJumpLines = 30;
    private static readonly TimeSpan OutlineScrollDuration = TimeSpan.FromMilliseconds(250);

    public MainView()
    {
        InitializeComponent();
        this.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        FileTree.ContainerPrepared += OnFileTreeContainerPrepared;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        if (DataContext is MainViewModel vm) AttachVm(vm);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_vm is null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.OnWorkspaceOpened -= OnWorkspaceOpened;
        // 闭包捕获this，不清空则VM会一直持有已离树的视图
        _vm.PickFolderAction = null;
        _vm = null;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is MainViewModel vm) AttachVm(vm);
        // 模板应用后再取ScrollViewer，避免null
        Dispatcher.UIThread.Post(AttachEditorScrollProbe, DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) AttachVm(vm);
    }

    private void AttachVm(MainViewModel vm)
    {
        if (_vm == vm) return;
        if (_vm is not null) { _vm.PropertyChanged -= OnVmPropertyChanged; _vm.OnWorkspaceOpened -= OnWorkspaceOpened; }
        _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.OnWorkspaceOpened += OnWorkspaceOpened;
        UpdateDocument(_vm.SelectedContent);
        UpdateHighlighter(_vm.SelectedParseResult);
        PreviewEditor.FontSize = _vm.CodeFontSize;
        PreviewEditor.ShowLineNumbers = _vm.ShowLineNumbers;
        // 惰性解析TopLevel：构造时MainView可能尚未挂到视觉树，点击「打开文件夹」时再取StorageProvider
        vm.PickFolderAction = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } sp) return null;
            var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions());
            if (folders.Count == 0) return null;
            // file://走本地路径（DirectoryInfo不认URI），content://原样交给AndroidLocalFileSystem
            var uri = folders[0].Path;
            if (uri is null) return null;
            return uri.Scheme == Uri.UriSchemeFile ? uri.LocalPath : uri.ToString();
        };
        ApplyLayout();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.PropertyName == nameof(MainViewModel.SelectedContent))
        {
            UpdateDocument(_vm.SelectedContent);
            if (_vm.SelectedContent is not null) SchedulePaintProbe();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedParseResult))
        {
            UpdateHighlighter(_vm.SelectedParseResult);
            if (_vm.SelectedParseResult is not null) SchedulePaintProbe();
        }
        else if (e.PropertyName == nameof(MainViewModel.CodeFontSize))
            PreviewEditor.FontSize = _vm.CodeFontSize;
        else if (e.PropertyName == nameof(MainViewModel.ShowLineNumbers))
            PreviewEditor.ShowLineNumbers = _vm.ShowLineNumbers;
        else if (e.PropertyName == nameof(MainViewModel.IsDark) && _transformer is not null)
        {
            _transformer.IsDark = _vm.IsDark;
            PreviewEditor.TextArea.TextView.Redraw();
        }
        else if (e.PropertyName == nameof(MainViewModel.OutlineScrollTargetLine) && _vm.OutlineScrollTargetLine is { } line)
        {
            ScrollOutlineToLine(line);
            _vm.ConsumeScrollTargetLine();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWide) || e.PropertyName == nameof(MainViewModel.OutlineOpen))
            ApplyLayout();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            e.Handled = true;
            _vm?.ToggleBenchmarkMode();
        }
        else if (e.Key == Key.Escape && _vm?.BenchmarkMode == true)
        {
            e.Handled = true;
            _vm.StopBenchmark();
        }
    }

    private void OnWorkspaceOpened()
    {
        if (_vm?.BenchmarkMode == true)
            _ = _vm.RunBenchmarkSequence();
    }

    private void UpdateDocument(string? text)
    {
        var doc = PreviewEditor.Document;
        if (doc is null || doc.Text != (text ?? ""))
            PreviewEditor.Document = new TextDocument(text ?? "");
    }

    private void UpdateHighlighter(ParsedCode? parsed)
    {
        var view = PreviewEditor.TextArea.TextView;
        if (_transformer is not null) view.LineTransformers.Remove(_transformer);
        _transformer = parsed is not null ? new HighlightTransformer(parsed.Highlights, _vm?.IsDark ?? false) : null;
        if (_transformer is not null) view.LineTransformers.Add(_transformer);
        view.Redraw();
    }

    // 首帧信号：内容+高亮绑定完成后，等渲染帧跑完再发，覆盖"解析+高亮画完"全链路
    private void SchedulePaintProbe()
    {
        if (!Trace.Enabled || _paintPending) return;
        _paintPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _paintPending = false;
            Trace.Mark("select.painted", $"name={_vm?.SelectedArtifact?.Name}");
        }, DispatcherPriority.Render);
    }

    // 滚动卡顿探针：每次视口变化后等渲染帧，超阈值才发信号，避免刷屏
    private void AttachEditorScrollProbe()
    {
        if (_editorScroll is not null) return;
        _editorScroll = PreviewEditor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_editorScroll is not null) _editorScroll.ScrollChanged += OnEditorScrollChanged;
    }

    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!Trace.Enabled || _scrollPaintPending) return;
        _scrollPaintPending = true;
        var t0 = _scrollSw.Elapsed.TotalMilliseconds;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPaintPending = false;
            var delta = _scrollSw.Elapsed.TotalMilliseconds - t0;
            if (delta > ScrollJankMs)
                Trace.Mark("scroll.frame", $"delta={delta:F1}ms dy={e.OffsetDelta.Y:F0}");
        }, DispatcherPriority.Render);
    }

    // 占位符节点只为让目录显示展开箭头而存在，容器层直接隐藏避免占一整行
    private void OnFileTreeContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is TreeItemViewModel { IsPlaceholder: true })
            e.Container.IsVisible = false;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        // 跳过占位以免选中后报空文件名
        var node = e.AddedItems.OfType<TreeItemViewModel>().FirstOrDefault(n => !n.IsPlaceholder);
        if (node is null || node == _lastSelected) return;
        _lastSelected = node;
        _ = _vm.SelectItem(node);
    }

    private void OnOutlineSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        var row = e.AddedItems.OfType<OutlineRow>().FirstOrDefault();
        if (row is not null) _vm.RequestScrollToLine(LineOf(row.Node.StartByte));
    }

    private void OnToggleOutline(object? sender, RoutedEventArgs e) => _vm?.ToggleOutline();
    private void OnCloseOutline(object? sender, RoutedEventArgs e) => _vm?.CloseOutline();
    private void OnClosePreview(object? sender, RoutedEventArgs e) => _vm?.ClearSelection();

    private void OnToggleTreeDrawer(object? sender, RoutedEventArgs e)
    {
        _treeDrawerOpen = !_treeDrawerOpen;
        ApplyLayout();
    }
    private void OnTreeDrawerScrimPressed(object? sender, PointerPressedEventArgs e) => CloseTreeDrawer();
    private void OnOutlineDrawerScrimPressed(object? sender, PointerPressedEventArgs e) => _vm?.CloseOutline();

    private void CloseTreeDrawer()
    {
        if (_treeDrawerOpen)
        {
            _treeDrawerOpen = false;
            ApplyLayout();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0) _vm?.OnWindowResized(e.NewSize.Width);
    }

    private void ApplyLayout()
    {
        if (_vm is null) return;
        var wide = _vm.IsWide;
        RootGrid.ColumnDefinitions = wide
            ? new ColumnDefinitions(_vm.OutlineOpen ? "260,*,330" : "260,*,0")
            : new ColumnDefinitions("0,*,0");

        ApplyHostLayout(TreeColumnHost, TreeDrawerScrim, wide, _treeDrawerOpen, 40);
        ApplyHostLayout(OutlineColumnHost, OutlineDrawerScrim, wide, _vm.OutlineOpen, 42);
        // 宽屏大纲列归零时宿主也要隐藏
        OutlineColumnHost.IsVisible = !wide || _vm.OutlineOpen;
    }

    // 宽屏驻留列、窄屏铺满三列当抽屉
    private static void ApplyHostLayout(Border host, Border scrim, bool wide, bool open, int drawerZIndex)
    {
        Grid.SetColumnSpan(host, wide ? 1 : 3);
        host.SetValue(Panel.ZIndexProperty, wide ? 0 : drawerZIndex);
        host.Classes.Set("drawer", !wide);
        host.Classes.Set("open", !wide && open);
        scrim.IsVisible = !wide;
        scrim.SetValue(Panel.ZIndexProperty, drawerZIndex - 1);
        scrim.Opacity = !wide && open ? 1 : 0;
        scrim.IsHitTestVisible = !wide && open;
    }

    private int LineOf(ulong startByte)
    {
        var text = _vm?.SelectedContent;
        if (text is null) return 1;
        var limit = (int)Math.Min(startByte, (ulong)text.Length);
        var line = 1;
        for (var i = 0; i < limit; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e) => _ = _vm?.PickFolder() ?? Task.CompletedTask;
    private void OnCloseWorkspace(object? sender, RoutedEventArgs e) => _vm?.CloseWorkspace();
    private void OnExpandAll(object? sender, RoutedEventArgs e) => _ = _vm?.ExpandAll() ?? Task.CompletedTask;
    private void OnCollapseAll(object? sender, RoutedEventArgs e) => _vm?.CollapseAll();
    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = _vm?.RefreshTree() ?? Task.CompletedTask;
    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        SettingsOverlay.IsVisible = true;
        SettingsPanel.SyncFromViewModel();
    }
    private void OnSettingsClose(object? sender, EventArgs e) => SettingsOverlay.IsVisible = false;
    private void OnSettingsOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, SettingsOverlay)) SettingsOverlay.IsVisible = false;
    }

    // 3行上下文 + 远距离先瞬跳再平滑滚动最后30行
    private void ScrollOutlineToLine(int line)
    {
        var target = Math.Max(1, line - OutlineScrollContextLines);
        var sv = PreviewEditor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var lineHeight = PreviewEditor.TextArea.TextView.DefaultLineHeight;
        if (sv is null || lineHeight <= 0)
        {
            PreviewEditor.ScrollToLine(target);
            return;
        }
        var currentFirst = (int)(sv.Offset.Y / lineHeight) + 1;
        if (Math.Abs(target - currentFirst) > OutlineScrollPreJumpLines)
            PreviewEditor.ScrollToLine(Math.Max(1, target - OutlineScrollPreJumpLines));
        AnimateScrollToLine(sv, lineHeight, target);
    }

    private void AnimateScrollToLine(ScrollViewer sv, double lineHeight, int targetLine)
    {
        _scrollTimer?.Stop();
        var startOffset = sv.Offset.Y;
        var targetOffset = lineHeight * (targetLine - 1);
        var startTime = DateTime.Now;
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollTimer.Tick += (_, _) =>
        {
            var t = (DateTime.Now - startTime).TotalMilliseconds / OutlineScrollDuration.TotalMilliseconds;
            if (t >= 1)
            {
                sv.Offset = new Vector(sv.Offset.X, targetOffset);
                _scrollTimer?.Stop();
                _scrollTimer = null;
                return;
            }
            var eased = t * t * (3 - 2 * t); // smoothstep
            sv.Offset = new Vector(sv.Offset.X, startOffset + (targetOffset - startOffset) * eased);
        };
        _scrollTimer.Start();
    }
}
