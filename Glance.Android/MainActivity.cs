using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Glance.Android.Repositories;
using Glance.Platform;

namespace Glance.Android;

[Activity(
    Label = "Glance.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // 在Avalonia启动前注册AndroidSAF文件系统实现，供共享MainViewModel经PlatformServices取用
        PlatformServices.FileSystem = new AndroidLocalFileSystem();
        base.OnCreate(savedInstanceState);
    }
}
