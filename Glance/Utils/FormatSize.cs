using System.Globalization;

namespace Glance.Utils;

public static class FormatSize
{
    private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB" };

    public static string Readable(long bytes)
    {
        var size = (double)bytes;
        var order = 0;

        while (size >= 1024 && order < Suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        if (order == 0)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        var formatted = size >= 100
            ? ((int)size).ToString(CultureInfo.InvariantCulture)
            : size.ToString("0.##", CultureInfo.InvariantCulture);
        return formatted + " " + Suffixes[order];
    }
}
