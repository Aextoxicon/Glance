using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Glance.Tests;

// 测试预览部分的上线不然要炸
public class PreviewLimitTests
{
    private const long MB = 1024L * 1024L;
    private const long HardLimit = 10 * MB;
    private const long PlainLimit = 2 * MB;

    private static readonly bool Native = VmHarness.NativeParserAvailable();

    [Fact]
    public Task 超过硬上限不读取也不载入内容() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("big.cs");
        repo.Text(path, "should not be read");

        await vm.SelectItem(VmHarness.Item(repo, path, HardLimit + 1));

        Assert.NotNull(vm.SelectedArtifact);
        Assert.Null(vm.SelectedContent);
        Assert.Null(vm.SelectedParseResult);
        Assert.Contains("已跳过预览", vm.MessageText);
        Assert.DoesNotContain(path, repo.ReadCalls.Keys);
    });

    [Fact]
    public Task 恰好等于硬上限仍然预览() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("big.cs");
        repo.Text(path, "hello");

        await vm.SelectItem(VmHarness.Item(repo, path, HardLimit));

        Assert.Null(vm.MessageText);
        Assert.NotNull(vm.SelectedContent);
        Assert.Equal(1, repo.ReadCalls[path]);
    });

    [Fact]
    public Task 恰好等于纯文本上限仍解析并进缓存() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("big.cs");
        repo.Text(path, "hello");

        await vm.SelectItem(VmHarness.Item(repo, path, PlainLimit));
        await vm.SelectItem(VmHarness.Item(repo, path, PlainLimit));

        Assert.Null(vm.PreviewNotice);
        if (Native) Assert.NotNull(vm.SelectedParseResult);
        Assert.Equal(1, repo.ReadCalls[path]);
    });

    [Fact]
    public Task 略超纯文本上限丢高亮但保留内容() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("big.cs");
        repo.Text(path, "hello");

        await vm.SelectItem(VmHarness.Item(repo, path, PlainLimit + 1));

        Assert.NotNull(vm.SelectedContent);
        Assert.Null(vm.SelectedParseResult);
        Assert.Contains("已跳过语法高亮", vm.PreviewNotice);
    });

    [Fact]
    public Task 命中缓存时重选不重复读取() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("small.txt");
        repo.Text(path, "hello");

        await vm.SelectItem(VmHarness.Item(repo, path, 100));
        await vm.SelectItem(VmHarness.Item(repo, path, 100));

        Assert.Equal(1, repo.ReadCalls[path]);
    });

    [Fact]
    public Task 纯文本区间之上不进缓存_重选会重读() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("mid.txt");
        repo.Text(path, "hello");

        await vm.SelectItem(VmHarness.Item(repo, path, 3 * MB));
        await vm.SelectItem(VmHarness.Item(repo, path, 3 * MB));

        Assert.Equal(2, repo.ReadCalls[path]);
    });

    [Fact]
    public Task 解析缓存逐出最久未使用项() => VmHarness.RunAsync(async (vm, repo) =>
    {
        for (var i = 0; i < 33; i++)
        {
            var p = VmHarness.P($"f{i}.txt");
            repo.Text(p, "hello");
            await vm.SelectItem(VmHarness.Item(repo, p, 10));
        }

        var first = VmHarness.P("f0.txt");
        var last = VmHarness.P("f32.txt");
        Assert.Equal(1, repo.ReadCalls[first]);

        await vm.SelectItem(VmHarness.Item(repo, first, 10));

        Assert.Equal(2, repo.ReadCalls[first]);
        Assert.Equal(1, repo.ReadCalls[last]);
    });
}
