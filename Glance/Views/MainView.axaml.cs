using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Glance.Native;
using Glance.Processing;
using Glance.ViewModels;

namespace Glance.Views;

public partial class MainView : UserControl
{
    private MainViewModel? _vm;
    private TreeItemViewModel? _lastSelected;
    private HighlightTransformer? _transformer;
    private bool _treeDrawerOpen;

    public MainView()
    {
        InitializeComponent();
        FileTree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded);
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        if (DataContext is MainViewModel vm) AttachVm(vm);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) AttachVm(vm);
    }

    private void AttachVm(MainViewModel vm)
    {
        if (_vm == vm) return;
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateDocument(_vm.SelectedContent);
        UpdateHighlighter(_vm.SelectedParseResult);
        PreviewEditor.FontSize = _vm.CodeFontSize;
        ApplyLayout();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.PropertyName == nameof(MainViewModel.SelectedContent))
            UpdateDocument(_vm.SelectedContent);
        else if (e.PropertyName == nameof(MainViewModel.SelectedParseResult))
            UpdateHighlighter(_vm.SelectedParseResult);
        else if (e.PropertyName == nameof(MainViewModel.CodeFontSize))
            PreviewEditor.FontSize = _vm.CodeFontSize;
        else if (e.PropertyName == nameof(MainViewModel.IsDark) && _transformer is not null)
        {
            _transformer.IsDark = _vm.IsDark;
            PreviewEditor.TextArea.TextView.Redraw();
        }
        else if (e.PropertyName == nameof(MainViewModel.OutlineScrollTargetLine) && _vm.OutlineScrollTargetLine is { } line)
        {
            PreviewEditor.ScrollToLine(line);
            _vm.ConsumeScrollTargetLine();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsWide) || e.PropertyName == nameof(MainViewModel.OutlineOpen))
            ApplyLayout();
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

    private void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem tvi && tvi.DataContext is TreeItemViewModel n)
            _ = n.LoadChildrenAsync(CancellationToken.None);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        var node = e.AddedItems.OfType<TreeItemViewModel>().FirstOrDefault();
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
            ? new ColumnDefinitions("260,*,220")
            : new ColumnDefinitions("*,0,0");

        // 文件树：宽屏进左列，窄屏进左抽屉
        if (wide)
        {
            if (FileTree.Parent != TreeColumnHost)
            {
                TreeDrawerPanel.Child = null;
                TreeColumnHost.Child = FileTree;
            }
            TreeColumnHost.IsVisible = true;
            TreeDrawer.IsVisible = false;
        }
        else
        {
            if (FileTree.Parent != TreeDrawerPanel)
            {
                TreeColumnHost.Child = null;
                TreeDrawerPanel.Child = FileTree;
            }
            TreeColumnHost.IsVisible = false;
            TreeDrawer.IsVisible = _treeDrawerOpen;
        }

        // 大纲：宽屏进右列，窄屏进右抽屉
        if (wide)
        {
            if (OutlineContent.Parent != OutlineColumnHost)
            {
                OutlineDrawerPanel.Child = null;
                OutlineColumnHost.Child = OutlineContent;
            }
            OutlineColumnHost.IsVisible = _vm.OutlineOpen;
            OutlineDrawer.IsVisible = false;
        }
        else
        {
            if (OutlineContent.Parent != OutlineDrawerPanel)
            {
                OutlineColumnHost.Child = null;
                OutlineDrawerPanel.Child = OutlineContent;
            }
            OutlineColumnHost.IsVisible = false;
            OutlineDrawer.IsVisible = _vm.OutlineOpen;
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

    private void OnOpenFolder(object? sender, RoutedEventArgs e) => _ = _vm?.PickFolder() ?? Task.CompletedTask;
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
}
