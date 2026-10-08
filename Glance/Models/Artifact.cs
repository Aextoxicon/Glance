namespace Glance.Models;

public enum ArtifactKind
{
    Image,
    Video,
    Audio,
    Pdf,
    Text,
    Archive,
}

public enum ArtifactSource
{
    Local,
    BackendChat,
}

public enum ArtifactStatus
{
    Available,
    Loading,
    Error,
    Unavailable,
}

public interface IArtifactPayload
{
}

public interface IArtifact
{
    string Id { get; }
    string Name { get; }
    long Size { get; }
    long LastMod { get; }
    string Extension { get; }
    ArtifactKind Kind { get; }
    ArtifactSource Source { get; }
    ArtifactStatus Status { get; }
    ArtifactMetadata? Metadata { get; }
    IArtifactPayload Payload { get; }
}

public record ArtifactMetadata(
    string? Checksum,
    string? Thumbnail,
    IReadOnlyDictionary<string, string> Extra
);

public record LocalPayload(
    string AbsolutePath,
    string ParentPath,
    bool IsDir
) : IArtifactPayload;

public record LocalArtifact(
    string Id,
    string Name,
    long Size,
    long LastMod,
    string Extension,
    ArtifactKind Kind,
    LocalPayload Local,
    ArtifactStatus Status = ArtifactStatus.Available,
    ArtifactMetadata? Metadata = null
) : IArtifact
{
    public ArtifactSource Source => ArtifactSource.Local;
    public IArtifactPayload Payload => Local;
}

public record BackendPayload(
    string MessageId,
    string DownloadUri,
    string? ThumbnailUri = null
) : IArtifactPayload;

public record BackendArtifact(
    string Id,
    string Name,
    long Size,
    long LastMod,
    string Extension,
    ArtifactKind Kind,
    BackendPayload Payload,
    ArtifactStatus Status = ArtifactStatus.Available,
    ArtifactMetadata? Metadata = null
) : IArtifact
{
    public ArtifactSource Source => ArtifactSource.BackendChat;
}

public static class ArtifactKindExt
{
    private static readonly IReadOnlyDictionary<string, ArtifactKind> ExtensionMap =
        new Dictionary<string, ArtifactKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["jpg"] = ArtifactKind.Image,
            ["jpeg"] = ArtifactKind.Image,
            ["png"] = ArtifactKind.Image,
            ["gif"] = ArtifactKind.Image,
            ["bmp"] = ArtifactKind.Image,
            ["webp"] = ArtifactKind.Image,
            ["svg"] = ArtifactKind.Image,
            ["mp4"] = ArtifactKind.Video,
            ["avi"] = ArtifactKind.Video,
            ["mkv"] = ArtifactKind.Video,
            ["mov"] = ArtifactKind.Video,
            ["mp3"] = ArtifactKind.Audio,
            ["wav"] = ArtifactKind.Audio,
            ["flac"] = ArtifactKind.Audio,
            ["aac"] = ArtifactKind.Audio,
            ["pdf"] = ArtifactKind.Pdf,
            ["zip"] = ArtifactKind.Archive,
            ["rar"] = ArtifactKind.Archive,
            ["7z"] = ArtifactKind.Archive,
            ["tar"] = ArtifactKind.Archive,
            ["gz"] = ArtifactKind.Archive,
        };

    public static ArtifactKind FromExtension(string ext)
    {
        var clean = ext.TrimStart('.').ToLowerInvariant();
        return ExtensionMap.TryGetValue(clean, out var kind) ? kind : ArtifactKind.Text;
    }
}
