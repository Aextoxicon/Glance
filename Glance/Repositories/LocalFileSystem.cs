using System;
using System.Collections.Generic;
using System.IO;

namespace Glance.Repositories;

public interface ILocalFileSystem : IFileDetector
{
    IReadOnlyList<LocalFileInfo> ListFiles(string path);
    LocalFileInfo FileInfo(string path);
    string? TryReadText(string path);
    bool Delete(string path);
    string ToUri(string path);
}

public sealed class LocalFileSystem : ILocalFileSystem
{
    // 文本/二进制闸门改为嗅探优先：读前 16KB 查 NUL。
    // 不再维护扩展名/文件名白名单而丢给Rust（resolve_grammar 按文件名+扩展名判定），
    // TODO: Android 需改用 SAF 的 head 读取替换 SniffBinary 的 System.IO 实现。
    private const int SniffBytes = 16384;

    public IReadOnlyList<LocalFileInfo> ListFiles(string path) => throw new System.NotImplementedException("TODO: 平台实现（Desktop=System.IO；Android=SAF）");
    public LocalFileInfo FileInfo(string path) => throw new System.NotImplementedException("TODO: 平台实现");
    public string? TryReadText(string path) => throw new System.NotImplementedException("TODO: 平台实现");
    public bool Delete(string path) => throw new System.NotImplementedException("TODO: 平台实现");
    public string ToUri(string path) => throw new System.NotImplementedException("TODO: 平台实现");

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
