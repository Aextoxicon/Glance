using System;

namespace Glance.Platform;

public interface IPlatform
{
    string Name { get; }
}

internal sealed record AndroidPlatform : IPlatform
{
    public string Name => "Android";
}

internal sealed record MacPlatform : IPlatform
{
    public string Name => "macOS";
}

internal sealed record WindowsPlatform : IPlatform
{
    public string Name => "Windows";
}

internal sealed record LinuxPlatform : IPlatform
{
    public string Name => "Linux";
}

public static class Platform
{
    public static IPlatform GetPlatform() => OperatingSystem.IsAndroid() ? new AndroidPlatform()
        : OperatingSystem.IsMacOS() ? new MacPlatform()
        : OperatingSystem.IsWindows() ? new WindowsPlatform()
        : new LinuxPlatform();
}
