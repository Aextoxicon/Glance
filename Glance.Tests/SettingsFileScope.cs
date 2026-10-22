using System;
using System.IO;
using Xunit;

#pragma warning disable CA1031

namespace Glance.Tests;

// LoadCore会PersistSettings()写真实%LOCALAPPDATA%\Glance\settings.json
// 测试里备份/还原，避免把用户的重开目录、主题、字号冲掉
public sealed class SettingsFileScope : IDisposable
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance", "settings.json");

    private readonly string? _backup;

    public SettingsFileScope()
    {
        try
        {
            if (File.Exists(FilePath)) _backup = File.ReadAllText(FilePath);
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            if (_backup is null)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, _backup);
            }
        }
        catch { }
    }
}
