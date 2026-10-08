using System.Collections.Generic;

namespace Glance.Processing;

public static class FileProcessor
{
    private static readonly Dictionary<string, string> FilenameToExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = "dockerfile",
        ["containerfile"] = "dockerfile",
        ["makefile"] = "makefile",
        ["gnumakefile"] = "makefile",
    };

    public static ParsedCode Process(string content, string extension, string? filename, ICodeParser parser)
    {
        var cleanExt = extension.ToLowerInvariant().TrimStart('.');
        var resolvedExt = string.IsNullOrEmpty(cleanExt) && filename != null
            ? (FilenameToExt.TryGetValue(filename.ToLowerInvariant().TrimStart('.'), out var mapped) ? mapped : cleanExt)
            : cleanExt;

        return parser.ParseCode(content, "." + resolvedExt);
    }
}
