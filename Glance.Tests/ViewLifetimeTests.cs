using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Glance.ViewModels;
using Glance.Views;
using Xunit;

namespace Glance.Tests;

// 视图离树后必须与VM断开，否则VM被Application拴住时会连带整个视图
public class ViewLifetimeTests
{
    private static MainViewModel NewVm() =>
        new(new FakeFileSystem(), new FakeArtifactRepo(), new FakeParser());

    private static Delegate? Subscribers(INotifyPropertyChanged target) =>
        typeof(ObservableObject)
            .GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(target) as Delegate;

    private static bool ViewSubscribed(MainViewModel vm, MainView view) =>
        Subscribers(vm)?.GetInvocationList().Any(d => ReferenceEquals(d.Target, view)) ?? false;

    [Fact]
    public Task 视图离树后与VM双向断开() => AvaloniaTest.Run(() =>
    {
        var vm = NewVm();
        var view = new MainView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Assert.True(ViewSubscribed(vm, view));
            Assert.NotNull(vm.PickFolderAction);

            window.Content = null;
            window.UpdateLayout();

            Assert.False(ViewSubscribed(vm, view));
            Assert.Null(vm.PickFolderAction);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    });
}
