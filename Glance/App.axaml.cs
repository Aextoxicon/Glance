using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Glance.ViewModels;
using Glance.Views;

namespace Glance;

public partial class App : Application
{
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
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            vm.TryRestoreLastDirectory();
            vm.PickFolderAction = async () =>
            {
                var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
                if (folders.Count == 0) return null;
                return folders[0].Path?.LocalPath;
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = new MainViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView { DataContext = new MainViewModel() };
        }
#else
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView { DataContext = new MainViewModel() };
        }
#endif
        base.OnFrameworkInitializationCompleted();
    }
}
