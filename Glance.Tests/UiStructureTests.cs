using System.Threading.Tasks;
using Avalonia.Controls;
using Glance.ViewModels;
using Glance.Views;
using Xunit;

namespace Glance.Tests;

// 只断言控件树的语义状态（列宽/可见性）
public class UiStructureTests
{
    private static MainViewModel NewVm() =>
        new(new FakeFileSystem(), new FakeArtifactRepo(), new FakeParser());

    [Fact]
    public Task 宽屏大纲关时第三列为0_打开后为330() => AvaloniaTest.Run(() =>
    {
        var vm = NewVm();
        var view = new MainView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            vm.IsWide = true;
            var grid = view.FindControl<Grid>("RootGrid")!;

            Assert.Equal(0, grid.ColumnDefinitions[2].Width.Value);

            vm.OutlineOpen = true;
            Assert.Equal(330, grid.ColumnDefinitions[2].Width.Value);

            vm.OutlineOpen = false;
            Assert.Equal(0, grid.ColumnDefinitions[2].Width.Value);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    });

    [Fact]
    public Task 宽屏树列恒为260_大纲列受开关控制() => AvaloniaTest.Run(() =>
    {
        var vm = NewVm();
        var view = new MainView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            vm.IsWide = true;
            var grid = view.FindControl<Grid>("RootGrid")!;
            Assert.Equal(260, grid.ColumnDefinitions[0].Width.Value);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    });

    [Fact]
    public Task 窄屏时左右两列归零() => AvaloniaTest.Run(() =>
    {
        var vm = NewVm();
        var view = new MainView { DataContext = vm };
        var window = new Window { Content = view, Width = 500, Height = 800 };
        window.Show();
        try
        {
            vm.IsWide = false;
            var grid = view.FindControl<Grid>("RootGrid")!;
            Assert.Equal(0, grid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(0, grid.ColumnDefinitions[2].Width.Value);

            var drawer = view.FindControl<Grid>("TreeDrawer")!;
            Assert.True(drawer.IsVisible);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    });
}
