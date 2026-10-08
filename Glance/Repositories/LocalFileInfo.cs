namespace Glance.Repositories;

public interface IFileDetector
{
    bool IsTextFile(string path);
}

public record LocalFileInfo(
    string Path,
    string Name,
    string ParentPath,
    bool IsDir,
    long Size,
    long LastMod,
    string Extension
);
