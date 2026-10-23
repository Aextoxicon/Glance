using System.Threading.Tasks;
using Glance.ViewModels;
using Xunit;

namespace Glance.Tests;

public class DisposeTests
{
    [Fact]
    public async Task 重复释放不抛异常() =>
        await AvaloniaTest.Run(() =>
        {
            var vm = new MainViewModel(new FakeFileSystem(), new ScriptedRepo(), new FakeParser());
            vm.Dispose();
            vm.Dispose();
        });
}
