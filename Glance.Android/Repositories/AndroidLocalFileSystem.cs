using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using AndroidX.DocumentFile.Provider;
using Glance.Repositories;

// 顶层操作刻意吞掉具体异常、转成null/空，与LocalFileSystem.cs同意图
#pragma warning disable CA1031

namespace Glance.Android.Repositories;

/// <summary>
/// AndroidSAF(Storage Access Framework)实现：路径统一为content://的tree/document Uri串，
/// 对上层MainViewModel/文件树当作不透明标识使用，缓存key、ParentPath拼接均兼容
/// https://developer.android.com/training/data-storage/shared/documents-files
/// </summary>
public sealed class AndroidLocalFileSystem : ILocalFileSystem
{
    private const int SniffBytes = 16384;

    public IReadOnlyList<LocalFileInfo> ListFiles(string path)
    {
        var uri = global::Android.Net.Uri.Parse(path)
            ?? throw new InvalidOperationException($"路径不是合法URI: {path}");
        var docFile = ResolveDirectory(uri)
            ?? throw new InvalidOperationException($"无法打开目录: {path}");

        var children = docFile.ListFiles()
            ?? throw new InvalidOperationException($"目录列举返回空: {path}");

        var parentUri = uri.ToString()!;
        var result = new List<LocalFileInfo>(children.Length);
        foreach (var item in children)
        {
            var info = MapDocumentFile(item, parentUri);
            if (info is not null) result.Add(info);
        }
        return result;
    }

    // content Uri有两种形态:tree和document
    private static DocumentFile? ResolveDirectory(global::Android.Net.Uri uri)
    {
        var direct = DocumentFile.FromTreeUri(Application.Context, uri);
        if (direct is { IsDirectory: true }) return direct;

        var text = uri.ToString();
        if (text is null) return direct;
        var docIndex = text.IndexOf("/document/", StringComparison.Ordinal);
        if (docIndex < 0) return direct;

        var treeUri = global::Android.Net.Uri.Parse(text.Substring(0, docIndex));
        if (treeUri is null) return direct;
        var treeId = global::Android.Net.Uri.Decode(treeUri.LastPathSegment);
        var docId = global::Android.Net.Uri.Decode(text.Substring(docIndex + "/document/".Length));
        if (treeId is null || docId is null || !docId.StartsWith(treeId, StringComparison.Ordinal))
            return direct;

        var node = DocumentFile.FromTreeUri(Application.Context, treeUri);
        var relative = docId.Substring(treeId.Length).TrimStart('/');
        if (relative.Length == 0) return node;
        foreach (var segment in relative.Split('/'))
        {
            node = node?.FindFile(segment);
            if (node is null) return null;
        }
        return node;
    }

    public LocalFileInfo FileInfo(string path)
    {
        var uri = global::Android.Net.Uri.Parse(path);
        if (uri is null) throw new FileNotFoundException($"文件或目录不存在: {path}");
        var docFile = ResolveDirectory(uri)
            ?? DocumentFile.FromSingleUri(Application.Context, uri)
            ?? throw new FileNotFoundException($"文件或目录不存在: {path}");
        var info = MapDocumentFile(docFile, docFile.ParentFile?.Uri?.ToString() ?? "")
            ?? throw new FileNotFoundException($"无法解析文件信息: {path}");
        return info;
    }

    public string? TryReadText(string path)
    {
        try
        {
            if (!IsTextFile(path)) return null;
            var uri = global::Android.Net.Uri.Parse(path);
            if (uri is null) return null;
            var stream = Application.Context.ContentResolver?.OpenInputStream(uri);
            if (stream is null) return null;
            using (stream)
            using (var reader = new StreamReader(stream))
            {
                var text = reader.ReadToEnd();
                return text.Contains('\0') ? null : text;
            }
        }
        catch (Exception) { return null; }
    }

    public byte[]? TryReadBytes(string path)
    {
        try
        {
            var uri = global::Android.Net.Uri.Parse(path);
            if (uri is null) return null;
            var stream = Application.Context.ContentResolver?.OpenInputStream(uri);
            if (stream is null) return null;
            using (stream)
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch (Exception) { return null; }
    }

    public bool Delete(string path)
    {
        try
        {
            var uri = global::Android.Net.Uri.Parse(path);
            if (uri is null) return false;
            var docFile = DocumentFile.FromSingleUri(Application.Context, uri);
            return docFile?.Delete() ?? false;
        }
        catch (Exception) { return false; }
    }

    public string ToUri(string path) => path;

    public bool IsTextFile(string path)
    {
        var uri = global::Android.Net.Uri.Parse(path);
        if (uri is null) return false;
        try
        {
            var stream = Application.Context.ContentResolver?.OpenInputStream(uri);
            if (stream is null) return false;
            using (stream)
            {
                var buffer = new byte[SniffBytes];
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                for (var i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0) return false;
                }
                return true;
            }
        }
        catch (Exception) { return false; }
    }

    private static LocalFileInfo? MapDocumentFile(DocumentFile docFile, string parentUri)
    {
        try
        {
            var name = docFile.Name;
            if (name is null) return null;
            var uri = docFile.Uri;
            if (uri is null) return null;
            var isDir = docFile.IsDirectory;
            return new LocalFileInfo(
                Path: uri.ToString()!,
                Name: name,
                ParentPath: parentUri,
                IsDir: isDir,
                Size: isDir ? 0L : docFile.Length(),
                LastMod: docFile.LastModified(), // Java毫秒，与桌面端LocalFileSystem的单位一致
                Extension: isDir ? "" : Path.GetExtension(name).ToLowerInvariant());
        }
        catch (Exception) { return null; }
    }
}
