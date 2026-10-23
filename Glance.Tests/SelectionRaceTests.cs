using System;
using System.Threading.Tasks;
using Xunit;

namespace Glance.Tests;

// 连续选文件/切工作区时旧任务不得回写
public class SelectionRaceTests
{
    [Fact]
    public Task 连续点两个文件_只保留最后一次() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var slowPath = VmHarness.P("slow.txt");
        var fastPath = VmHarness.P("fast.txt");
        repo.Text(fastPath, "fast content");
        var gate = repo.GateRead(slowPath);

        var slow = vm.SelectItem(VmHarness.Item(repo, slowPath));
        await VmHarness.WaitUntil(() => repo.ReadCalls.ContainsKey(slowPath), "slow读取应已开始");

        await vm.SelectItem(VmHarness.Item(repo, fastPath));

        Assert.Equal("fast.txt", vm.SelectedArtifact!.Name);
        Assert.Equal("fast content", vm.SelectedContent);

        // 迟到的慢读取不得覆盖当前选择
        gate.SetResult("late content");
        await slow;

        Assert.Equal("fast.txt", vm.SelectedArtifact!.Name);
        Assert.Equal("fast content", vm.SelectedContent);
    });

    [Fact]
    public Task 重复选同一文件复用解析缓存() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("same.cs");
        repo.Text(path, "content");
        var item = VmHarness.Item(repo, path);

        await vm.SelectItem(item);
        var first = vm.SelectedParseResult;
        await vm.SelectItem(item);

        Assert.Equal(1, repo.ReadCalls[path]);
        if (VmHarness.NativeParserAvailable())
        {
            Assert.NotNull(first);
            Assert.Same(first, vm.SelectedParseResult);
        }
    });

    [Fact]
    public Task 切工作区后解析缓存被清空() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("same.cs");
        repo.Listing(VmHarness.Root, new[] { Artifacts.File(path) }, new[] { Artifacts.File(path) });
        repo.Text(path, "content");

        await vm.LoadCore(VmHarness.Root);
        await vm.SelectItem(VmHarness.Item(repo, path));
        Assert.Equal(1, repo.ReadCalls[path]);

        vm.CloseWorkspace();
        await vm.LoadCore(VmHarness.Root);
        await vm.SelectItem(VmHarness.Item(repo, path));

        Assert.Equal(2, repo.ReadCalls[path]);
    });

    [Fact]
    public Task 关闭工作区后迟到的尺寸计算不得回写() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var sub = VmHarness.P("sub");
        repo.Listing(VmHarness.Root, new[] { Artifacts.Dir(sub) });
        var gate = repo.GateList(sub);
        repo.Listing(sub, Array.Empty<Glance.Models.IArtifact>());

        var load = vm.LoadCore(VmHarness.Root);
        await VmHarness.WaitUntil(() => vm.IsComputingSize, "应进入计算大小阶段");

        vm.CloseWorkspace();
        Assert.Equal(0, vm.TotalSize);
        Assert.False(vm.IsComputingSize);

        gate.SetResult();
        await load;

        Assert.Equal(0, vm.TotalSize);
        Assert.False(vm.IsComputingSize);
    });

    [Fact]
    public Task 切换文件夹只保留最新文件夹的大小() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var rootA = @"D:\a";
        var rootB = @"D:\b";
        var subA = System.IO.Path.Combine(rootA, "sub");

        repo.Listing(rootA, new[] { Artifacts.Dir(subA) });
        var gateA = repo.GateList(subA);
        repo.Listing(subA, Array.Empty<Glance.Models.IArtifact>());

        var fileB = Artifacts.File(System.IO.Path.Combine(rootB, "b.txt"), 100);
        repo.Listing(rootB, new[] { fileB });

        var loadA = vm.LoadCore(rootA);
        await VmHarness.WaitUntil(() => vm.IsComputingSize, "A应进入计算大小阶段");

        await vm.LoadCore(rootB);

        Assert.Equal(rootB, vm.CurrentPath);
        Assert.Equal(100, vm.TotalSize);
        Assert.False(vm.IsComputingSize);

        gateA.SetResult();
        await loadA;

        Assert.Equal(rootB, vm.CurrentPath);
        Assert.Equal(100, vm.TotalSize);
    });
}
