using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Glance.Platform;
using Glance.Processing;
using Glance.Repositories;
using Glance.ViewModels;
using Glance.Views;

namespace Glance;

public partial class App : Application
{
    private MainViewModel? _vm;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if !ANDROID
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _vm = new MainViewModel();
            var window = new MainWindow { DataContext = _vm };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => DisposeVm();
            _vm.TryRestoreLastDirectory();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => NewMainView(() => new MainViewModel());
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = NewMainView(() => new MainViewModel());
        }
#else
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var fs = PlatformServices.FileSystem ?? new LocalFileSystem();
            singleViewPlatform.MainView = NewMainView(() => new MainViewModel(fs, new UniffiCodeParser()));
        }
#endif
        base.OnFrameworkInitializationCompleted();
    }

    // 旧VM不释放会被Application的主题订阅拴住并连带整个文件树与解析缓存
    private MainView NewMainView(Func<MainViewModel> factory)
    {
        DisposeVm();
        _vm = factory();
        return new MainView { DataContext = _vm };
    }

    private void DisposeVm()
    {
        _vm?.Dispose();
        _vm = null;
    }
}
