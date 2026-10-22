using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Glance.ViewModels;
using Xunit;

namespace Glance.Tests;

// 只验证计算属性会不会跟着来源字段发通知这类bug不报错、构建通过、界面看着也正常
// MainViewModel构造会读Application.Current.ActualThemeVariant，必须跑在无头UI线程上
public class BindingNotificationTests
{
    private static MainViewModel NewVm() =>
        new(new FakeFileSystem(), new FakeArtifactRepo(), new FakeParser());

    private static Task WithVm(Action<MainViewModel> body) =>
        AvaloniaTest.RunAsync(() =>
        {
            var vm = NewVm();
            try { body(vm); }
            finally { vm.Dispose(); }
            return Task.CompletedTask;
        });

    private static List<string?> Capture(INotifyPropertyChanged target, Action act)
    {
        var names = new List<string?>();
        void Handler(object? s, PropertyChangedEventArgs e) => names.Add(e.PropertyName);
        target.PropertyChanged += Handler;
        try { act(); }
        finally { target.PropertyChanged -= Handler; }
        return names;
    }

    [Fact]
    public Task 总大小变化时可读总大小发出通知() => WithVm(vm =>
    {
        var names = Capture(vm, () => vm.TotalSize = 1536);
        Assert.Contains(nameof(MainViewModel.TotalSizeReadable), names);
    });

    [Fact]
    public Task 当前路径变化时文件夹名发出通知() => WithVm(vm =>
    {
        var names = Capture(vm, () => vm.CurrentPath = @"D:\GitHub\Collisions\");
        Assert.Contains(nameof(MainViewModel.CurrentFolderName), names);
    });

    [Fact]
    public Task 选中项变化时选中大小发出通知() => WithVm(vm =>
    {
        var names = Capture(vm, () => vm.SelectedArtifact = Artifacts.File(@"D:\a\b.cs", 2048));
        Assert.Contains(nameof(MainViewModel.SelectedSizeDisplay), names);
    });

    [Fact]
    public void 子项数变化时可读大小发出通知()
    {
        var node = new TreeItemViewModel(Artifacts.Dir(@"D:\a\src"), new FakeArtifactRepo());
        var names = Capture(node, () => node.ChildCount = 7);
        Assert.Contains(nameof(TreeItemViewModel.SizeDisplay), names);
    }

    [Theory]
    [InlineData(@"D:\projects\", "projects")]
    [InlineData(@"D:\projects", "projects")]
    [InlineData(@"D:/projects/", "projects")]
    // 与KMP的差异：KMP的PathUtil会把"D:\"削成"D:"；C#的TrimEndingDirectorySeparator
    // 刻意保留根路径，所以这里显示完整"D:\"，保留更清晰的一方
    [InlineData(@"D:\", @"D:\")]
    [InlineData("", "工作区")]
    public Task 文件夹名按尾分隔符与空路径返回(string path, string expected) => WithVm(vm =>
    {
        vm.CurrentPath = path;
        Assert.Equal(expected, vm.CurrentFolderName);
    });

    [Fact]
    public Task 预览提示层四条件() => WithVm(vm =>
    {
        Assert.False(vm.HasPreviewMessage);

        vm.HasSelection = true;
        vm.MessageText = "[二进制文件] x 无法预览";
        vm.IsLoadingPreview = true;
        Assert.False(vm.HasPreviewMessage); // 加载中即使有消息也不该盖住进度条
        vm.IsLoadingPreview = false;
        Assert.True(vm.HasPreviewMessage);

        // 拿到内容后就不再需要提示层，内容本身会盖满预览区
        vm.SelectedContent = "text";
        Assert.False(vm.HasPreviewMessage);

        vm.SelectedContent = null;
        vm.MessageText = null;
        Assert.False(vm.HasPreviewMessage);
    });

    [Fact]
    public Task 状态栏可见性跟随状态文本() => WithVm(vm =>
    {
        vm.MessageText = null;
        Assert.False(vm.HasMessage);
        var names = Capture(vm, () => vm.MessageText = "日志已导出");
        Assert.Contains(nameof(MainViewModel.HasMessage), names);
        Assert.True(vm.HasMessage);
    });
}
