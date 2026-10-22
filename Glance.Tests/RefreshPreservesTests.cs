using System;
using System.Linq;
using System.Threading.Tasks;
using Glance.Models;
using Xunit;

namespace Glance.Tests;

// 刷新只做就地diff，展开态与选中必须留住
public class RefreshPreservesTests
{
    [Fact]
    public Task 刷新重读根目录并保留已有节点实例() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var a = Artifacts.File(VmHarness.P("a.txt"));
        var dirX = Artifacts.Dir(VmHarness.P("dirX"));
        var newFile = Artifacts.File(VmHarness.P("newFile.txt"));

        repo.Listing(VmHarness.Root,
            new IArtifact[] { a, dirX },
            new IArtifact[] { a, dirX },
            new IArtifact[] { a, dirX, newFile });
        repo.Listing(VmHarness.P("dirX"), Array.Empty<IArtifact>());

        await vm.LoadCore(VmHarness.Root);
        Assert.Equal(2, vm.TreeItems.Count);
        var firstA = vm.TreeItems.First(t => t.Name == "a.txt");

        await vm.RefreshTree();

        Assert.Equal(3, vm.TreeItems.Count);
        Assert.Contains(vm.TreeItems, t => t.Name == "newFile.txt");
        Assert.Contains(vm.TreeItems, t => t.Name == "a.txt");
        // 就地diff：同标识复用同一实例，容器身份不丢
        Assert.Same(firstA, vm.TreeItems.First(t => t.Name == "a.txt"));
    });

    [Fact]
    public Task 刷新保留展开态并重读已展开子目录() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var sub = VmHarness.P("sub");
        var one = Artifacts.File(System.IO.Path.Combine(sub, "1.txt"));
        var two = Artifacts.File(System.IO.Path.Combine(sub, "2.txt"));

        repo.Listing(VmHarness.Root,
            new IArtifact[] { Artifacts.Dir(sub) },
            new IArtifact[] { Artifacts.Dir(sub) },
            new IArtifact[] { Artifacts.Dir(sub) });
        repo.Listing(sub,
            new IArtifact[] { one },
            new IArtifact[] { one },
            new IArtifact[] { one, two });

        await vm.LoadCore(VmHarness.Root);
        var oldSub = vm.TreeItems.First();
        await vm.SelectItem(oldSub);
        await VmHarness.WaitUntil(
            () => oldSub.Children.Count > 0 && !oldSub.Children[0].IsPlaceholder,
            "子目录首次加载");
        Assert.Equal(new[] { "1.txt" }, oldSub.Children.Select(c => c.Name));

        await vm.RefreshTree();

        var newSub = vm.TreeItems.First();
        Assert.Same(oldSub, newSub);
        Assert.True(newSub.IsExpanded);
        Assert.Equal(new[] { "1.txt", "2.txt" }, newSub.Children.Select(c => c.Name));
    });

    [Fact]
    public Task 刷新后选中仍然挂在对应节点() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var sub = VmHarness.P("sub");
        var one = Artifacts.File(System.IO.Path.Combine(sub, "1.txt"));

        repo.Listing(VmHarness.Root,
            new IArtifact[] { Artifacts.Dir(sub) },
            new IArtifact[] { Artifacts.Dir(sub) },
            new IArtifact[] { Artifacts.Dir(sub) });
        repo.Listing(sub,
            new IArtifact[] { one },
            new IArtifact[] { one },
            new IArtifact[] { one });
        repo.Text(one.Id, "content");

        await vm.LoadCore(VmHarness.Root);
        var oldSub = vm.TreeItems.First();
        await vm.SelectItem(oldSub);
        await VmHarness.WaitUntil(
            () => oldSub.Children.Count > 0 && !oldSub.Children[0].IsPlaceholder,
            "子目录首次加载");

        var file = oldSub.Children.First(c => c.Name == "1.txt");
        await vm.SelectItem(file);
        Assert.Equal(one.Id, vm.SelectedArtifact!.Id);

        await vm.RefreshTree();

        var newFile = vm.TreeItems.First().Children.First(c => c.Name == "1.txt");
        Assert.True(newFile.IsSelected);
        Assert.True(vm.TreeItems.First().IsExpanded);
        Assert.Equal(one.Id, vm.SelectedArtifact!.Id);
    });

    [Fact]
    public Task 已展开目录路径只含展开目录() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var sub = VmHarness.P("sub");
        var other = VmHarness.P("other");

        repo.Listing(VmHarness.Root,
            new IArtifact[] { Artifacts.Dir(sub), Artifacts.Dir(other) },
            new IArtifact[] { Artifacts.Dir(sub), Artifacts.Dir(other) });
        repo.Listing(sub, Array.Empty<IArtifact>());
        repo.Listing(other, Array.Empty<IArtifact>());

        await vm.LoadCore(VmHarness.Root);
        Assert.Empty(vm.CurrentExpandedDirPaths());

        var subNode = vm.TreeItems.First(t => t.Name == "sub");
        await vm.SelectItem(subNode);

        // CurrentExpandedDirPaths只看IsExpanded，不依赖子节点是否加载完
        Assert.True(subNode.IsExpanded);
        Assert.Equal(new[] { sub }, vm.CurrentExpandedDirPaths());
    });
}
