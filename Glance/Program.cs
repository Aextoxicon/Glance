using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Glance;

public static class Program
{
    // Native Avalonia desktop entry point. The uniffi cdylib (uniffi_code_parser)
    // is copied next to this assembly by the CopyNativeLib target, so the first
    // P/Invoke into Glance.Native loads and checksum-verifies it automatically.
    [STAThread]
    public static int Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
