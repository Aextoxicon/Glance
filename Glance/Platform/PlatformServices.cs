using Glance.Repositories;

namespace Glance.Platform;

// 共享工程与Android专有工程之间的解耦点
public static class PlatformServices
{
    public static ILocalFileSystem? FileSystem { get; set; }
}
