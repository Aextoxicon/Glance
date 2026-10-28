using System;
using System.Collections.Generic;
using Glance.Repositories;

namespace Glance.Platform;

// 共享与Android专有之间的解耦点
public static class PlatformServices
{
    public static ILocalFileSystem? FileSystem { get; set; }
}

// Android注册时机晚于Avalonia初始化，VM持有此代理到真正调用时再解析
public sealed class DeferredFileSystem : ILocalFileSystem
{
    public ILocalFileSystem Inner =>
        PlatformServices.FileSystem ?? throw new InvalidOperationException("平台文件系统未注册");

    public IReadOnlyList<LocalFileInfo> ListFiles(string path) => Inner.ListFiles(path);
    public LocalFileInfo FileInfo(string path) => Inner.FileInfo(path);
    public string? TryReadText(string path) => Inner.TryReadText(path);
    public byte[]? TryReadBytes(string path) => Inner.TryReadBytes(path);
    public bool Delete(string path) => Inner.Delete(path);
    public string ToUri(string path) => Inner.ToUri(path);
    public bool IsTextFile(string path) => Inner.IsTextFile(path);
}
