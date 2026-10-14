using System;
using System.IO;
using System.Text.Json;
using Glance.ViewModels;

#pragma warning disable CA1031

namespace Glance.Utils;

public sealed class AppSettings
{
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public int CodeFontSize { get; set; } = MainViewModel.DefaultCodeFontSize;
    public bool RememberLastDir { get; set; }
    public string? LastDirectory { get; set; }
}

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glance", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } s)
                return s;
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
