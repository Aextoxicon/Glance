namespace Glance.Utils;

public static class FormatSize
{
    public static string Readable(long bytes)
    {
        var suffixes = new[] { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        var size = (double)bytes;

        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        if (order == 0)
        {
            return $"{bytes} B";
        }

        var formatted = size >= 100
            ? ((int)size).ToString()
            : (System.Math.Round(size * 100) / 100).ToString().TrimEnd('0').TrimEnd('.');
        return $"{formatted} {suffixes[order]}";
    }
}
