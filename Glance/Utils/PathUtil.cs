namespace Glance.Utils;

public static class PathUtil
{
    public static string FileName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0) return string.Empty;
        var idx = System.Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return idx < 0 ? trimmed : trimmed.Substring(idx + 1);
    }
}
