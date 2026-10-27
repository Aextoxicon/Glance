using System.Threading.Tasks;
using Xunit;

namespace Glance.Tests;

// dxf虽是可读文本但必须先走CAD分支，dwg是二进制同理
public class CadPreviewTests
{
    [Theory]
    [InlineData("plan.dwg")]
    [InlineData("plan.dxf")]
    public Task 选中CAD文件走几何预览(string name) => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P(name);
        repo.Text(path, "not empty");

        await vm.SelectItem(VmHarness.Item(repo, path));

        Assert.True(vm.IsCadPreview);
        Assert.NotNull(vm.SelectedCad);
        Assert.Null(vm.SelectedParseResult);
        Assert.Null(vm.MessageText);
    });

    [Fact]
    public Task 选中普通文件不进CAD预览() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("note.txt");
        repo.Text(path, "hello");

        await vm.SelectItem(VmHarness.Item(repo, path));

        Assert.False(vm.IsCadPreview);
        Assert.Null(vm.SelectedCad);
    });

    [Fact]
    public Task 关闭预览清空CAD状态() => VmHarness.RunAsync(async (vm, repo) =>
    {
        var path = VmHarness.P("plan.dwg");
        repo.Text(path, "not empty");
        await vm.SelectItem(VmHarness.Item(repo, path));
        Assert.True(vm.IsCadPreview);

        vm.ClearSelection();

        Assert.False(vm.IsCadPreview);
        Assert.Null(vm.SelectedCad);
    });
}
