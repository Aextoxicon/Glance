using System.Collections.Generic;
using System.Linq;
using Glance.Repositories;

namespace Glance.Utils;

/// <summary>.gitignore 解析出的忽略名单（仅目录名维度）。平移自 KMP GitIgnoreDirs。</summary>
public sealed record GitIgnoreDirs(HashSet<string> DirOnly, HashSet<string> DirOrFile)
{
    public static readonly GitIgnoreDirs Empty = new(new HashSet<string>(), new HashSet<string>());
}

/// <summary>.gitignore 解析与目录过滤。仅整目录名忽略，忽略含通配符/路径/取反/注释的行。</summary>
public static class GitIgnore
{
    public static GitIgnoreDirs Parse(string? content)
    {
        if (string.IsNullOrEmpty(content)) return GitIgnoreDirs.Empty;

        var dirOnly = new HashSet<string>();
        var dirOrFile = new HashSet<string>();

        foreach (var rawLine in content.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("!")) continue;
            if (line.Contains('*') || line.Contains('?') || line.Contains('[') || line.Contains(']')) continue;
            if (line.StartsWith("/")) continue;
            var name = line.TrimEnd('/');
            if (name.Length == 0 || name.StartsWith(".")) continue;
            if (name.Contains('/')) continue;
            if (line.EndsWith("/")) dirOnly.Add(name);
            else dirOrFile.Add(name);
        }

        return new GitIgnoreDirs(dirOnly, dirOrFile);
    }

    public static IReadOnlyList<LocalFileInfo> Filter(IReadOnlyList<LocalFileInfo> items, GitIgnoreDirs ignored)
    {
        var dirOnly = ignored.DirOnly;
        var dirOrFile = ignored.DirOrFile;

        return items.Where(item =>
        {
            if (item.Name.StartsWith(".")) return false;
            if (dirOrFile.Contains(item.Name)) return false;
            if (item.IsDir && dirOnly.Contains(item.Name)) return false;
            return true;
        }).ToList();
    }
}
