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
#if ANDROID
    private AndroidCodeView? _androidCode;
    private string? _lastText;
    private ParsedCode? _lastParsed;
    private bool _androidVisible;
    // 抽屉开合动画(0.25s)期间强制隐藏原生岛
    private bool _drawerAnimating;
    private bool _drawerAnimPrev;
    private readonly DispatcherTimer _drawerAnimTimer = new() { Interval = TimeSpan.FromMilliseconds(260) };
#endif

    private const int OutlineScrollContextLines = 3;
    private const int OutlineScrollPreJumpLines = 30;
    private static readonly TimeSpan OutlineScrollDuration = TimeSpan.FromMilliseconds(250);

    // Avalonia无内置GridLength过渡，手动插值
    private bool _firstLayout = true;
    private readonly DispatcherTimer _treeColAnim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _outlineColAnim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _treeColFrom, _treeColTo, _outlineColFrom, _outlineColTo;
    private DateTime _treeColStart, _outlineColStart;

    public MainView()
    {
        InitializeComponent();
#if ANDROID
        // 原生TextView替代AvaloniaEdit
        // 其次原生视图永远在最上层必须主动隐藏
        _androidCode = new AndroidCodeView();
        PreviewHost.Children.Add(_androidCode);
#endif
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
        if (_vm is not null) { _vm.PropertyChanged -= OnVmPropertyChanged; _vm.OnWorkspaceOpened -= OnWorkspaceOpened; _vm.BenchmarkScrollToEnd -= ScrollEditorToEnd; }
        _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.OnWorkspaceOpened += OnWorkspaceOpened;
        _vm.BenchmarkScrollToEnd += ScrollEditorToEnd;
        UpdateDocument(_vm.SelectedContent);
        UpdateHighlighter(_vm.SelectedParseResult);
        PreviewEditor.FontSize = _vm.CodeFontSize;
        PreviewEditor.ShowLineNumbers = _vm.ShowLineNumbers;
#if ANDROID
        UpdateAndroidContent();
        UpdateAndroidVisibility();
#endif
        // 惰性解析TopLevel
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
#if ANDROID
        UpdateAndroidVisibility();
#endif
        if (e.PropertyName == nameof(MainViewModel.SelectedContent))
        {
            UpdateDocument(_vm.SelectedContent);
#if ANDROID
            UpdateAndroidContent();
#endif
            if (_vm.SelectedContent is not null) SchedulePaintProbe();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedParseResult))
        {
            UpdateHighlighter(_vm.SelectedParseResult);
#if ANDROID
            UpdateAndroidContent();
#endif
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
#if ANDROID
            UpdateAndroidContent();
#endif
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
#if ANDROID
        _lastText = text;
#endif
        var doc = PreviewEditor.Document;
        if (doc is null || doc.Text != (text ?? ""))
            PreviewEditor.Document = new TextDocument(text ?? "");
    }

    private void UpdateHighlighter(ParsedCode? parsed)
    {
#if ANDROID
        _lastParsed = parsed;
#endif
        var view = PreviewEditor.TextArea.TextView;
        if (_transformer is not null) view.LineTransformers.Remove(_transformer);
        _transformer = parsed is not null ? new HighlightTransformer(parsed.Highlights, _vm?.IsDark ?? false) : null;
        if (_transformer is not null) view.LineTransformers.Add(_transformer);
        view.Redraw();
    }

    // 首帧信号：内容+高亮绑定完成后，等渲染帧跑完再发
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

    // 每次视口变化后等渲染帧，超阈值才发信号
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
#if ANDROID
        var drawerOpenNow = _treeDrawerOpen || (_vm.OutlineOpen && !_vm.IsWide);
        if (drawerOpenNow != _drawerAnimPrev)
        {
            _drawerAnimPrev = drawerOpenNow;
            _drawerAnimating = true;
            _drawerAnimTimer.Tick -= OnDrawerAnimTick;
            _drawerAnimTimer.Tick += OnDrawerAnimTick;
            _drawerAnimTimer.Stop();
            _drawerAnimTimer.Start();
        }
        UpdateAndroidVisibility();
#endif
        var wide = _vm.IsWide;
        // 抽屉占真实列宽，主内容(*)被挤窄，而非浮层盖在上面
        var col0Target = wide ? 260 : (_treeDrawerOpen ? 280 : 0);
        var col2Target = _vm.OutlineOpen ? (wide ? 330 : 360) : 0;

        var cols = RootGrid.ColumnDefinitions;
        if (_firstLayout)
        {
            cols[0].Width = new GridLength(col0Target, GridUnitType.Pixel);
            cols[2].Width = new GridLength(col2Target, GridUnitType.Pixel);
            _firstLayout = false;
        }
        else
        {
            AnimateColumnWidth(cols[0], col0Target, true);
            AnimateColumnWidth(cols[2], col2Target, false);
        }

        // 抽屉宿主待在自己列内填满，由列宽控制显隐/挤压
        Grid.SetColumnSpan(TreeColumnHost, 1);
        Grid.SetColumnSpan(OutlineColumnHost, 1);
        TreeColumnHost.SetValue(Panel.ZIndexProperty, wide ? 0 : 40);
        OutlineColumnHost.SetValue(Panel.ZIndexProperty, wide ? 0 : 42);
        OutlineColumnHost.IsVisible = !wide || _vm.OutlineOpen;
        TreeDrawerScrim.IsVisible = false;
        TreeDrawerScrim.IsHitTestVisible = false;
        OutlineDrawerScrim.IsVisible = false;
        OutlineDrawerScrim.IsHitTestVisible = false;
    }

    private void AnimateColumnWidth(ColumnDefinition col, double to, bool isTree)
    {
        var from = col.Width.IsAbsolute ? col.Width.Value : 0;
        if (Math.Abs(from - to) < 0.5)
        {
            col.Width = new GridLength(to, GridUnitType.Pixel);
            return;
        }
        var timer = isTree ? _treeColAnim : _outlineColAnim;
        if (isTree) { _treeColFrom = from; _treeColTo = to; _treeColStart = DateTime.Now; }
        else { _outlineColFrom = from; _outlineColTo = to; _outlineColStart = DateTime.Now; }
        timer.Tick -= OnColAnimTick;
        timer.Tick += OnColAnimTick;
        timer.Stop();
        timer.Start();
    }

    private void OnColAnimTick(object? sender, EventArgs e)
    {
        var timer = (DispatcherTimer)sender!;
        var isTree = ReferenceEquals(timer, _treeColAnim);
        var col = isTree ? RootGrid.ColumnDefinitions[0] : RootGrid.ColumnDefinitions[2];
        var from = isTree ? _treeColFrom : _outlineColFrom;
        var to = isTree ? _treeColTo : _outlineColTo;
        var start = isTree ? _treeColStart : _outlineColStart;
        var t = (DateTime.Now - start).TotalMilliseconds / 250.0;
        if (t >= 1)
        {
            col.Width = new GridLength(to, GridUnitType.Pixel);
            timer.Stop();
        }
        else
        {
            var k = 1 - Math.Pow(1 - t, 3);
            col.Width = new GridLength(from + (to - from) * k, GridUnitType.Pixel);
        }
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

    private void OnOpenFolder(object? sender, RoutedEventArgs e) =>
        _ = _vm?.PickFolder() ?? Task.CompletedTask;
    private void OnCloseWorkspace(object? sender, RoutedEventArgs e) => _vm?.CloseWorkspace();
    private void OnExpandAll(object? sender, RoutedEventArgs e) => _ = _vm?.ExpandAll() ?? Task.CompletedTask;
    private void OnCollapseAll(object? sender, RoutedEventArgs e) => _vm?.CollapseAll();
    private void OnRefresh(object? sender, RoutedEventArgs e) =>
        _ = _vm?.RefreshTree() ?? Task.CompletedTask;
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

    // 3行上下文
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

    // 基准模式：跳到文本末尾，逼出整篇滚动+重绘耗时（CAD路径不调用）
    private void ScrollEditorToEnd()
    {
#if ANDROID
        _androidCode?.ScrollToEnd();
        return;
#else
        if (_editorScroll is null) return;
        var maxY = _editorScroll.Extent.Height - _editorScroll.Viewport.Height;
        if (maxY <= 0) return;
        _editorScroll.Offset = new Vector(_editorScroll.Offset.X, maxY);
#endif
    }

#if ANDROID
    private void UpdateAndroidContent()
    {
        if (_androidCode is null || _vm is null) return;
        _androidCode.CodeFontSize = _vm.CodeFontSize;
        _androidCode.SetContent(_lastText, _lastParsed?.Highlights, _vm.IsDark);
    }

    private void UpdateAndroidVisibility()
    {
        if (_androidCode is null || _vm is null) return;
        // Avalonia Android的NativeControlHost在隐藏后不重建原生控件（IsVisible与零尺寸两种路都实锤失败），
        // 所以控件永远保持真实尺寸存活，隐藏时用负Margin移出屏外靠根裁剪不可见
        var visible = _vm.HasSelection && !_vm.IsCadPreview && !_vm.IsLoadingPreview
            && !_vm.HasPreviewMessage && !_vm.HasPreviewNotice
            && !_treeDrawerOpen && !(_vm.OutlineOpen && !_vm.IsWide) && !_drawerAnimating;
        _androidCode.Margin = visible ? default : new Thickness(0, -100000, 0, 0);
        _androidCode.InvalidateMeasure();
        _androidCode.InvalidateArrange();
        // 变可见时重推一次内容，兜住控件创建前丢掉的推送
        if (visible) UpdateAndroidContent();
        if (_androidVisible != visible)
        {
            _androidVisible = visible;
            Android.Util.Log.Info("GlanceNative", $"visibility -> {visible}");
        }
    }

    private void OnDrawerAnimTick(object? sender, EventArgs e)
    {
        _drawerAnimating = false;
        _drawerAnimTimer.Stop();
        UpdateAndroidVisibility();
    }
#endif
}
