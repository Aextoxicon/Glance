using System.Collections.Generic;
using Glance.Models;

namespace Glance.Processing;

public static class FileProcessor
{
    private static readonly HashSet<string> TextExtensions = new() { "txt", "md", "markdown" };
    private static readonly HashSet<string> CadExtensions = new() { "dwg", "dxf" };
    private static readonly Dictionary<string, string> FilenameToExt = new()
    {
        { "dockerfile", "dockerfile" },
        { "containerfile", "dockerfile" },
        { "makefile", "makefile" },
        { "gnumakefile", "makefile" },
    };

    public static bool IsPlainText(IArtifact artifact) =>
        TextExtensions.Contains(CleanExtension(artifact.Extension));

    // dxf也可能是纯文本，分支必须先于IsTextFile判断
    public static bool IsCadFile(IArtifact artifact) =>
        CadExtensions.Contains(CleanExtension(artifact.Extension));

    // 无扩展名但文件名命中已知映射时返回带点扩展名供native选grammar，否则返回文件名
    public static string ResolveParseName(IArtifact artifact)
    {
        if (CleanExtension(artifact.Extension).Length > 0) return artifact.Name;
        var name = artifact.Name.ToLowerInvariant().TrimStart('.');
        return name.Length > 0 && FilenameToExt.TryGetValue(name, out var mapped)
            ? "." + mapped
            : artifact.Name;
    }

    private static string CleanExtension(string extension) =>
        extension.TrimStart('.').ToLowerInvariant();
}
