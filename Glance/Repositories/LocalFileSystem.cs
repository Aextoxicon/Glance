using System;
using System.Collections.Generic;

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
    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown", "json", "xml", "yaml", "yml",
        "cs", "js", "ts", "jsx", "tsx", "py", "java", "kt",
        "kts", "swift", "c", "cpp", "h", "hpp", "css", "scss",
        "less", "html", "htm", "sh", "bash", "zsh", "ps1",
        "bat", "cmd", "sql", "r", "go", "rs", "toml", "ini",
        "cfg", "conf", "env", "gitignore", "gradle", "sln",
        "csproj", "props", "targets", "razor",
        "fs", "fsx", "dart", "lua", "pl", "pm", "rb", "php",
        "scala", "clj", "cljs", "edn", "coffee", "vue", "svelte",
        "astro", "svg", "graphql", "proto", "cmake", "m", "mm",
    };

    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dockerfile", "makefile", "gnumakefile", "cmakelists",
        "readme", "license", "changelog", "contributing",
        "authors", "todo", "notes", "help",
    };

    public IReadOnlyList<LocalFileInfo> ListFiles(string path) => throw new System.NotImplementedException("TODO: 平台实现（Desktop=System.IO；Android=SAF）");
    public LocalFileInfo FileInfo(string path) => throw new System.NotImplementedException("TODO: 平台实现");
    public string? TryReadText(string path) => throw new System.NotImplementedException("TODO: 平台实现");
    public bool Delete(string path) => throw new System.NotImplementedException("TODO: 平台实现");
    public string ToUri(string path) => throw new System.NotImplementedException("TODO: 平台实现");

    public bool IsTextFile(string path)
    {
        var ext = LastSegmentAfter(path, '.');
        var name = LastSegmentAfter(path, '/', '\\');
        if (TextExt.Contains(ext)) return true;
        if (TextFileNames.Contains(name)) return true;
        // TODO: 二进制嗅探（读前 16KB 查 NUL）平台逻辑留待实现
        return false;
    }

    private static string LastSegmentAfter(string s, params char[] seps)
    {
        var idx = s.LastIndexOfAny(seps);
        return idx < 0 ? s : s.Substring(idx + 1);
    }
}
