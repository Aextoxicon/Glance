using System;
using System.Collections.Generic;
using System.IO;

namespace Glance.Repositories;

public interface ILocalFileSystem
{
    IReadOnlyList<LocalFileInfo> ListFiles(string path);
    LocalFileInfo FileInfo(string path);
    string? TryReadText(string path);
    bool Delete(string path);
    string ToUri(string path);
    bool IsTextFile(string path);
}

public sealed class LocalFileSystem : ILocalFileSystem
{
    // 文本/二进制闸门改为嗅探优先：读前 16KB 查 NUL。
    // 不再维护扩展名/文件名白名单而丢给Rust（resolve_grammar 按文件名+扩展名判定），
    // TODO: Android 需改用 SAF 的 head 读取替换 SniffBinary 的 System.IO 实现。
    private const int SniffBytes = 16384;

    public IReadOnlyList<LocalFileInfo> ListFiles(string path)
    {
        DirectoryInfo dir;
        try { dir = new DirectoryInfo(path); }
        catch (ArgumentException) { return Array.Empty<LocalFileInfo>(); }

        if (!dir.Exists) return Array.Empty<LocalFileInfo>();

        var result = new List<LocalFileInfo>(capacity: 64);
        try
        {
            foreach (var item in dir.EnumerateFileSystemInfos())
            {
                try { result.Add(ToLocalFileInfo(item, dir.FullName)); }
                catch (UnauthorizedAccessException) { } // 跳过无权限stat的条目
                catch (IOException) { }
            }
        }
        catch (DirectoryNotFoundException) { return Array.Empty<LocalFileInfo>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<LocalFileInfo>(); }

        return result;
    }

    public LocalFileInfo FileInfo(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            var dir = new DirectoryInfo(path);
            if (dir.Exists)
            {
                return new LocalFileInfo(
                    Path: dir.FullName,
                    Name: dir.Name,
                    ParentPath: dir.Parent?.FullName ?? "",
                    IsDir: true,
                    Size: 0L,
                    LastMod: ToUnixMillis(dir.LastWriteTimeUtc),
                    Extension: "");
            }
            throw new FileNotFoundException($"文件或目录不存在: {path}");
        }

        return new LocalFileInfo(
            Path: file.FullName,
            Name: file.Name,
            ParentPath: file.Directory?.FullName ?? "",
            IsDir: false,
            Size: file.Length,
            LastMod: ToUnixMillis(file.LastWriteTimeUtc),
            Extension: Path.GetExtension(file.Name).ToLowerInvariant());
    }

    public string? TryReadText(string path)
    {
        try
        {
            if (!File.Exists(path) || Directory.Exists(path)) return null;
            if (!IsTextFile(path)) return null;
            var content = File.ReadAllText(path);
            return content.Contains('\0') ? null : content;
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    public bool Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    public string ToUri(string path) => new Uri(path).ToString();

    private static LocalFileInfo ToLocalFileInfo(FileSystemInfo info, string parentPath)
    {
        var isDir = info is DirectoryInfo;
        return new LocalFileInfo(
            Path: info.FullName,
            Name: info.Name,
            ParentPath: parentPath,
            IsDir: isDir,
            Size: isDir ? 0L : ((FileInfo)info).Length,
            LastMod: ToUnixMillis(info.LastWriteTimeUtc),
            Extension: isDir ? "" : Path.GetExtension(info.Name).ToLowerInvariant());
    }

    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static long ToUnixMillis(DateTime utc) => (long)(utc - UnixEpoch).TotalMilliseconds;

    public bool IsTextFile(string path)
    {
        var head = SniffHead(path, SniffBytes);
        if (head is null) return false;
        return Array.IndexOf(head, (byte)0) < 0;
    }

    private static byte[]? SniffHead(string path, int maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = (int)Math.Min(maxBytes, fs.Length);
            if (len <= 0) return Array.Empty<byte>();
            var buf = new byte[len];
            var read = fs.Read(buf, 0, len);
            if (read == len) return buf;
            var trimmed = new byte[read];
            Array.Copy(buf, trimmed, read);
            return trimmed;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
