using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using Android.Provider;
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

    // 列顺序与ChildProjection一致
    private const int ColumnDocumentId = 0;
    private const int ColumnDisplayName = 1;
    private const int ColumnMimeType = 2;
    private const int ColumnSize = 3;
    private const int ColumnLastModified = 4;

    private static readonly string[] ChildProjection =
    {
        "document_id", "_display_name", "mime_type", "_size", "last_modified",
    };

    public IReadOnlyList<LocalFileInfo> ListFiles(string path)
    {
        var uri = global::Android.Net.Uri.Parse(path)
            ?? throw new InvalidOperationException($"路径不是合法URI: {path}");
        var resolver = Application.Context.ContentResolver
            ?? throw new InvalidOperationException($"ContentResolver不可用: {path}");

        // DocumentFile.listFiles会静默吞错回空数组
        var errors = new List<string>();
        var anyQueried = false;
        foreach (var (treeUri, documentId) in RecoverTree(uri))
        {
            try
            {
                TryPersistPermission(resolver, treeUri);
                var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId);
                if (childrenUri is null)
                {
                    errors.Add($"无法构造子项uri({treeUri},{documentId})");
                    continue;
                }
                using var cursor = resolver.Query(childrenUri, ChildProjection, null, null, null);
                if (cursor is null)
                {
                    errors.Add($"查询返回null({treeUri})");
                    continue;
                }
                anyQueried = true;
                var children = ReadChildren(cursor, treeUri, path);
                if (children.Count > 0) return children;
                // 全部形态都0行时按真空目录返回
                errors.Add($"0行({treeUri}|{documentId})");
            }
            catch (Exception ex)
            {
                errors.Add($"{treeUri}|{documentId}|{ex.Message}");
            }
        }
        // KMP同款DocumentFile路径兜底
        try
        {
            TryPersistPermission(resolver, uri);
            var fallback = DocumentFile.FromTreeUri(Application.Context, uri);
            var list = fallback?.ListFiles();
            if (list is { Length: > 0 })
            {
                var mapped = new List<LocalFileInfo>(list.Length);
                string? mapError = null;
                foreach (var item in list)
                {
                    var info = MapDocumentFile(item, path, ref mapError);
                    if (info is not null) mapped.Add(info);
                }
                if (mapped.Count > 0) return mapped;
                errors.Add($"DocumentFile映射失败{list.Length}项 {mapError}");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"DocumentFile回退异常 {ex.Message}");
        }
        // 至少一次查询成功且都返回0行
        if (anyQueried) return Array.Empty<LocalFileInfo>();
        global::Android.Util.Log.Error("GlanceFS", $"ListFiles全失败: {string.Join(" ; ", errors)}");
        throw new InvalidOperationException($"列举失败 {path} 尝试{errors.Count}种: {string.Join(" ; ", errors)}");
    }

    // 只有纯tree uri才能持久化授权,document uri继承tree授权无需尝试
    private static bool TryPersistPermission(ContentResolver resolver, global::Android.Net.Uri treeUri)
    {
        var text = treeUri.ToString();
        if (text is null || text.Contains("/document/", StringComparison.Ordinal)) return false;
        try
        {
            resolver.TakePersistableUriPermission(treeUri, ActivityFlags.GrantReadUriPermission);
            return true;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Info("GlanceFS", $"persist授权失败 {treeUri} {ex.Message}");
            return false;
        }
    }

    private static IReadOnlyList<LocalFileInfo> ReadChildren(global::Android.Database.ICursor cursor,
        global::Android.Net.Uri treeUri, string parentPath)
    {
        var result = new List<LocalFileInfo>(cursor.Count);
        while (cursor.MoveToNext())
        {
            var documentId = cursor.GetString(ColumnDocumentId);
            var name = cursor.GetString(ColumnDisplayName);
            if (documentId is null || name is null) continue;
            var isDir = cursor.GetString(ColumnMimeType) == DocumentsContract.Document.MimeTypeDir;
            var documentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId);
            result.Add(new LocalFileInfo(
                Path: documentUri?.ToString() ?? documentId,
                Name: name,
                ParentPath: parentPath,
                IsDir: isDir,
                Size: isDir || cursor.IsNull(ColumnSize) ? 0L : cursor.GetLong(ColumnSize),
                LastMod: cursor.IsNull(ColumnLastModified) ? 0L : cursor.GetLong(ColumnLastModified),
                Extension: isDir ? "" : System.IO.Path.GetExtension(name).ToLowerInvariant()));
        }
        return result;
    }

    // tree Uri直接用;document Uri从中拆出tree段;只剩documentId时把它当tree根再试
    private static IEnumerable<(global::Android.Net.Uri TreeUri, string DocumentId)> RecoverTree(global::Android.Net.Uri uri)
    {
        var text = uri.ToString();
        if (text is null) yield break;

        var docIndex = text.IndexOf("/document/", StringComparison.Ordinal);

        if (DocumentsContract.IsTreeUri(uri))
        {
            var treeId = DocumentsContract.GetTreeDocumentId(uri);
            if (treeId is not null)
            {
                // document uri必须用自身documentId列举,用treeId会查到tree根的子项
                // 纯tree uri没有document段,GetDocumentId会抛Invalid URI
                if (docIndex > 0)
                {
                    var docId = global::Android.Net.Uri.Decode(text.Substring(docIndex + "/document/".Length));
                    if (docId is not null) yield return (uri, docId);
                }
                else
                {
                    yield return (uri, treeId);
                }
            }
        }

        if (docIndex <= 0) yield break;

        var docId2 = global::Android.Net.Uri.Decode(text.Substring(docIndex + "/document/".Length));
        if (docId2 is null) yield break;

        var treeUri = global::Android.Net.Uri.Parse(text.Substring(0, docIndex));
        if (treeUri is not null)
        {
            yield return (treeUri, docId2);
            var treeId = global::Android.Net.Uri.Decode(treeUri.LastPathSegment);
            if (treeId == docId2 && DocumentsContract.GetTreeDocumentId(treeUri) is { } root)
                yield return (treeUri, root);
        }

        if (uri.Authority is { } authority)
        {
            var asTree = DocumentsContract.BuildTreeDocumentUri(authority, docId2);
            if (asTree is not null) yield return (asTree, docId2);
        }
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
        string? error = null;
        var info = MapDocumentFile(docFile, docFile.ParentFile?.Uri?.ToString() ?? "", ref error)
            ?? throw new FileNotFoundException($"无法解析文件信息: {path} ({error})");
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

    private static LocalFileInfo? MapDocumentFile(DocumentFile docFile, string parentUri, ref string? firstError)
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
        catch (Exception ex)
        {
            firstError ??= ex.Message;
            return null;
        }
    }
}
