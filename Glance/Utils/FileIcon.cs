using System;
using System.Collections.Generic;
using Material.Icons;

namespace Glance.Utils;

// 文件类型→MaterialIconKind映射
public static class FileIcon
{
    private static readonly HashSet<string> CodeExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "kt", "kts", "java", "py", "js", "ts", "jsx", "tsx",
        "rs", "go", "swift", "c", "cpp", "h", "hpp", "cs",
    };

    private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "markdown", "txt",
    };

    private static readonly HashSet<string> ConfigExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "xml", "yaml", "yml", "toml", "gradle", "gradle.kts",
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "svg", "ico",
    };

    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "tar", "gz", "rar",
    };

    private static readonly HashSet<string> ShellExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh", "bash", "zsh",
    };

    public static MaterialIconKind For(string extension, bool isDir)
    {
        if (isDir) return MaterialIconKind.FolderOutline;
        var clean = extension.TrimStart('.');
        if (string.IsNullOrEmpty(clean)) return MaterialIconKind.FileOutline;
        if (CodeExts.Contains(clean)) return MaterialIconKind.Code;
        if (TextExts.Contains(clean)) return MaterialIconKind.FileDocumentOutline;
        if (ConfigExts.Contains(clean)) return MaterialIconKind.CogOutline;
        if (ImageExts.Contains(clean)) return MaterialIconKind.FileImageOutline;
        if (clean.Equals("pdf", StringComparison.OrdinalIgnoreCase)) return MaterialIconKind.FilePdfBox;
        if (ArchiveExts.Contains(clean)) return MaterialIconKind.CompressedFileOutline;
        if (ShellExts.Contains(clean)) return MaterialIconKind.Terminal;
        return MaterialIconKind.FileOutline;
    }
}
