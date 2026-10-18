using System.Collections.Generic;
using System.IO;

namespace Glance.Processing;

public static class FileProcessor
{
    private static readonly HashSet<string> TextExtensions = new() { "txt", "md", "markdown" };
    private static readonly Dictionary<string, string> FilenameToExt = new()
    {
        { "dockerfile", "dockerfile" },
        { "containerfile", "dockerfile" },
        { "makefile", "makefile" },
        { "gnumakefile", "makefile" },
    };

    public static bool IsPlainText(string path) =>
        TextExtensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant());

    // 无扩展名但文件名命中已知映射时返回带点扩展名供native选grammar，否则原样返回
    public static string ResolveParseName(string path)
    {
        var cleanExt = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (cleanExt.Length > 0) return path;
        var name = Path.GetFileName(path);
        if (name.Length > 0 && FilenameToExt.TryGetValue(name.ToLowerInvariant().TrimStart('.'), out var mapped))
            return "." + mapped;
        return path;
    }
}
