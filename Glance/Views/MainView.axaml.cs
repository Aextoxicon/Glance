using System;
using System.Collections.Generic;
using System.Diagnostics;
using Trace = Glance.Utils.Trace;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    // 整体平移（非列宽挤压，避免PC端编辑器重排）
    private readonly DispatcherTimer _treeSlideAnim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _outlineSlideAnim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _contentSlideAnim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Dictionary<DispatcherTimer, (double from, double to, DateTime start, TranslateTransform t, int dur)> _pushState = new();

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
        var treeWidth = wide ? 260 : 280;
        var outlineWidth = wide ? 330 : 360;
        var treeShown = wide || _treeDrawerOpen;
        var outlineShown = _vm.OutlineOpen;

        AnimateTranslate((TranslateTransform)TreeColumnHost.RenderTransform, treeShown ? 0 : -treeWidth, _treeSlideAnim);
        AnimateTranslate((TranslateTransform)OutlineColumnHost.RenderTransform, outlineShown ? 0 : outlineWidth, _outlineSlideAnim);
        var contentX = (treeShown ? treeWidth : 0) - (outlineShown ? outlineWidth : 0);
        AnimateTranslate((TranslateTransform)ContentArea.RenderTransform, contentX, _contentSlideAnim);

        TreeColumnHost.IsVisible = true;
        OutlineColumnHost.IsVisible = !wide || _vm.OutlineOpen;
        TreeDrawerScrim.IsVisible = false;
        TreeDrawerScrim.IsHitTestVisible = false;
        OutlineDrawerScrim.IsVisible = false;
        OutlineDrawerScrim.IsHitTestVisible = false;
    }

    private void AnimateTranslate(TranslateTransform t, double to, DispatcherTimer timer)
    {
        var from = t.X;
        if (Math.Abs(from - to) < 0.5) { t.X = to; return; }
        _pushState[timer] = (from, to, DateTime.Now, t, 250);
        timer.Tick -= OnPushTick;
        timer.Tick += OnPushTick;
        timer.Stop();
        timer.Start();
    }

    private void OnPushTick(object? sender, EventArgs e)
    {
        var timer = (DispatcherTimer)sender!;
        if (!_pushState.TryGetValue(timer, out var s)) return;
        var elapsed = (DateTime.Now - s.start).TotalMilliseconds / s.dur;
        if (elapsed >= 1)
        {
            s.t.X = s.to;
            timer.Stop();
            _pushState.Remove(timer);
        }
        else
        {
            var k = 1 - Math.Pow(1 - elapsed, 3);
            s.t.X = s.from + (s.to - s.from) * k;
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
        // 原生控件不跟随祖先RenderTransform，用Margin按内容平移量对齐
        var x = ((TranslateTransform)ContentArea.RenderTransform).X;
        _androidCode.Margin = visible ? new Thickness(x, 0, 0, 0) : new Thickness(0, -100000, 0, 0);
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
