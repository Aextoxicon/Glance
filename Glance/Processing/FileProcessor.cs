using System.Collections.Generic;

namespace Glance.Processing;

public static class FileProcessor
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown",
    };

    private static readonly Dictionary<string, string> FilenameToExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = "dockerfile",
        ["containerfile"] = "dockerfile",
        ["makefile"] = "makefile",
        ["gnumakefile"] = "makefile",
    };

    public static CodeParseResult Process(string content, string extension, string? filename, ICodeParser parser)
    {
        var cleanExt = extension.ToLowerInvariant().TrimStart('.');
        if (TextExtensions.Contains(cleanExt))
        {
            return new PlainText(cleanExt, content);
        }

        var resolvedExt = string.IsNullOrEmpty(cleanExt) && filename != null
            ? (FilenameToExt.TryGetValue(filename.ToLowerInvariant().TrimStart('.'), out var mapped) ? mapped : cleanExt)
            : cleanExt;

        return parser.ParseCode(content, "." + resolvedExt);
    }
}
