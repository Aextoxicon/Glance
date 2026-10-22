using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Glance.ViewModels;
using Xunit;

namespace Glance.Tests;

// 只验证'计算属性会不会跟着来源字段发通知'这类 bug 不报错、构建通过、界面看着也正常
// MainViewModel构造会读Application.Current.ActualThemeVariant必须跑在无头 UI 线程上
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
    public Task TotalSize变化时TotalSizeReadable发出通知() => WithVm(vm =>
    {
        var names = Capture(vm, () => vm.TotalSize = 1536);
        Assert.Contains(nameof(MainViewModel.TotalSizeReadable), names);
    });

    [Fact]
    public Task CurrentPath变化时CurrentFolderName发出通知() => WithVm(vm =>
    {
        var names = Capture(vm, () => vm.CurrentPath = @"D:\GitHub\Collisions\");
        Assert.Contains(nameof(MainViewModel.CurrentFolderName), names);
    });

    [Fact]
    public Task SelectedArtifact变化时SelectedSizeDisplay发出通知() => WithVm(vm =>
    {
        var names = Capture(vm, () => vm.SelectedArtifact = Artifacts.File(@"D:\a\b.cs", 2048));
        Assert.Contains(nameof(MainViewModel.SelectedSizeDisplay), names);
    });

    [Fact]
    public void ChildCount变化时SizeDisplay发出通知()
    {
        var node = new TreeItemViewModel(Artifacts.Dir(@"D:\a\src"), new FakeArtifactRepo());
        var names = Capture(node, () => node.ChildCount = 7);
        Assert.Contains(nameof(TreeItemViewModel.SizeDisplay), names);
    }

    [Theory]
    [InlineData(@"D:\projects\", "projects")]
    [InlineData(@"D:\projects", "projects")]
    [InlineData(@"D:/projects/", "projects")]
    // 与 KMP 的差异：KMP PathUtil 会把 "D:\" 削成 "D:"；C# 的 TrimEndingDirectorySeparator
    // 刻意保留根路径，所以这里显示完整 "D:\"。保留更清晰的一方。
    [InlineData(@"D:\", @"D:\")]
    [InlineData("", "工作区")]
    public Task CurrentFolderName按尾分隔符与空路径返回(string path, string expected) => WithVm(vm =>
    {
        vm.CurrentPath = path;
        Assert.Equal(expected, vm.CurrentFolderName);
    });

    [Fact]
    public Task HasPreviewMessage四条件() => WithVm(vm =>
    {
        // 未选中 → false
        Assert.False(vm.HasPreviewMessage);

        // 选中 + 无内容 + 有消息 + 非加载 → true
        vm.HasSelection = true;
        vm.MessageText = "[二进制文件] x 无法预览";
        vm.IsLoadingPreview = true;
        Assert.False(vm.HasPreviewMessage); // 仍在加载
        vm.IsLoadingPreview = false;
        Assert.True(vm.HasPreviewMessage);

        // 有内容 → false
        vm.SelectedContent = "text";
        Assert.False(vm.HasPreviewMessage);

        // 无消息 → false
        vm.SelectedContent = null;
        vm.MessageText = null;
        Assert.False(vm.HasPreviewMessage);
    });

    [Fact]
    public Task HasMessage跟随MessageText() => WithVm(vm =>
    {
        vm.MessageText = null;
        Assert.False(vm.HasMessage);
        var names = Capture(vm, () => vm.MessageText = "日志已导出");
        Assert.Contains(nameof(MainViewModel.HasMessage), names);
        Assert.True(vm.HasMessage);
    });
}
