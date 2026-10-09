namespace Glance.Repositories;

public record LocalFileInfo(
    string Path,
    string Name,
    string ParentPath,
    bool IsDir,
    long Size,
    long LastMod,
    string Extension
);
