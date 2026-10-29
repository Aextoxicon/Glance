using System;
using System.IO;
#if !ANDROID
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
#endif

namespace Glance.Utils;

// trace日志落点：桌面=下载目录/Glance-trace；Android写私有目录暂存，发布时经MediaStore复制到公共Download/Glance-trace
public static class TraceDestination
{
    public static string StagingDirectory()
    {
#if ANDROID
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance-trace");
#else
        var dir = DownloadsDirectory();
#endif
        Directory.CreateDirectory(dir);
        return dir;
    }

    // 导出后把产物挪到用户可见位置
    public static string? Publish(string filePath)
    {
#if ANDROID
        return AndroidPublish(filePath);
#else
        return filePath;
#endif
    }

#if ANDROID
    private static string? AndroidPublish(string filePath)
    {
        if ((int)global::Android.OS.Build.VERSION.SdkInt < 29) return null;
        try
        {
            var resolver = global::Android.App.Application.Context?.ContentResolver;
            if (resolver is null) return null;
            var name = Path.GetFileName(filePath);
            if (!PublishOne(resolver, name, File.ReadAllBytes(filePath), "text/csv")) return null;
            var summary = Path.ChangeExtension(filePath, ".summary.json");
            var published = $"Download/Glance-trace/{name}";
            if (File.Exists(summary))
            {
                PublishOne(resolver, Path.GetFileName(summary), File.ReadAllBytes(summary), "application/json");
                File.Delete(summary);
            }
            File.Delete(filePath);
            return published;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool PublishOne(
        global::Android.Content.ContentResolver resolver, string name, byte[] bytes, string mime)
    {
        var values = new global::Android.Content.ContentValues();
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.DisplayName, name);
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.MimeType, mime);
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.RelativePath,
            global::Android.OS.Environment.DirectoryDownloads + "/Glance-trace");
        var uri = resolver.Insert(
            global::Android.Provider.MediaStore.Downloads.GetContentUri("external"), values);
        if (uri is null) return false;
        using var stream = resolver.OpenOutputStream(uri);
        if (stream is null) return false;
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        return true;
    }
#endif

#if !ANDROID
    private static string DownloadsDirectory()
    {
        var downloads = WindowsKnownDownloads()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Path.Combine(downloads, "Glance-trace");
    }

    // FOLDERID_Downloads；下载目录可被用户重定向，不能拼UserProfile猜
    [SupportedOSPlatform("windows")]
    private static string? WindowsKnownDownloads()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var folder = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(folder, 0, IntPtr.Zero, out var ptr) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);
#endif
}
