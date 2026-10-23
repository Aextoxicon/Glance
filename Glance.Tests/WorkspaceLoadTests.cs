using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Glance.Models;
using Xunit;

namespace Glance.Tests;

// 工作区根的显示名与.gitignore定位都不该用Path.*处理平台标识符
// Android的路径是content://的SAF URI
public class WorkspaceLoadTests
{
    [Fact]
    public Task 文件夹名取自文件系统的条目名() => VmHarness.RunAsync(async (vm, repo, fs) =>
    {
        using var settings = new SettingsFileScope();
        repo.Listing(VmHarness.Root, Array.Empty<IArtifact>());
        fs.PutName(VmHarness.Root, "Collisions");

        await vm.LoadCore(VmHarness.Root);

        Assert.Equal("Collisions", vm.CurrentFolderName);
    });

    [Fact]
    public Task 取不到条目名时回落工作区() => VmHarness.RunAsync(async (vm, repo, fs) =>
    {
        using var settings = new SettingsFileScope();
        repo.Listing(VmHarness.Root, Array.Empty<IArtifact>());
        fs.PutName(VmHarness.Root, "");

        await vm.LoadCore(VmHarness.Root);

        Assert.Equal("工作区", vm.CurrentFolderName);
    });

    [Fact]
    public Task 关闭工作区清空文件夹名() => VmHarness.RunAsync(async (vm, repo, fs) =>
    {
        using var settings = new SettingsFileScope();
        repo.Listing(VmHarness.Root, Array.Empty<IArtifact>());
        fs.PutName(VmHarness.Root, "Collisions");
        await vm.LoadCore(VmHarness.Root);
        Assert.Equal("Collisions", vm.CurrentFolderName);

        vm.CloseWorkspace();

        Assert.Equal("", vm.CurrentFolderName);
    });

    [Fact]
    public Task 根目录里的gitignore被找到并生效() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        var gitignore = Artifacts.File(VmHarness.P(".gitignore"));
        repo.Listing(VmHarness.Root, new IArtifact[]
        {
            gitignore,
            Artifacts.Dir(VmHarness.P("dist")),
            Artifacts.File(VmHarness.P("ok.cs")),
        });
        repo.Text(gitignore.Id, "dist/");
        repo.Listing(VmHarness.P("dist"), Array.Empty<IArtifact>());

        await vm.LoadCore(VmHarness.Root);

        var names = vm.TreeItems.Select(t => t.Name).ToArray();
        Assert.Contains("ok.cs", names);
        Assert.DoesNotContain("dist", names);
    });

    [Fact]
    public Task 没有gitignore时不过滤() => VmHarness.RunAsync(async (vm, repo) =>
    {
        using var settings = new SettingsFileScope();
        repo.Listing(VmHarness.Root, new IArtifact[]
        {
            Artifacts.Dir(VmHarness.P("dist")),
            Artifacts.File(VmHarness.P("ok.cs")),
        });
        repo.Listing(VmHarness.P("dist"), Array.Empty<IArtifact>());

        await vm.LoadCore(VmHarness.Root);

        var names = vm.TreeItems.Select(t => t.Name).ToArray();
        Assert.Contains("dist", names);
        Assert.Contains("ok.cs", names);
    });
}
