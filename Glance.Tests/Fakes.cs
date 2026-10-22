using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Glance.Models;
using Glance.Processing;
using Glance.Repositories;
using Glance.Utils;

namespace Glance.Tests;

public sealed class FakeFileSystem : ILocalFileSystem
{
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);

    public void PutText(string path, string content) => _text[path] = content;

    public IReadOnlyList<LocalFileInfo> ListFiles(string path) => Array.Empty<LocalFileInfo>();
    public LocalFileInfo FileInfo(string path) => throw new FileNotFoundException(path);
    public string? TryReadText(string path) => _text.TryGetValue(path, out var t) ? t : null;
    public bool Delete(string path) => false;
    public string ToUri(string path) => path;
    public bool IsTextFile(string path) => true;
}

public sealed class FakeArtifactRepo : IArtifactRepo
{
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);

    public void PutText(string path, string content) => _text[path] = content;

    public Task<Result<IReadOnlyList<IArtifact>>> ListAsync(string path) =>
        Task.FromResult<Result<IReadOnlyList<IArtifact>>>(new Success<IReadOnlyList<IArtifact>>(Array.Empty<IArtifact>()));

    public Task<Result<IReadOnlyList<IArtifact>>> SearchAsync(string query) =>
        ListAsync(query);

    public Task<Result<IArtifact>> GetAsync(string id) =>
        Task.FromResult<Result<IArtifact>>(new Failure<IArtifact>(new IOException(id)));

    public Task<Result<bool>> DeleteAsync(string id) =>
        Task.FromResult<Result<bool>>(new Success<bool>(false));

    public Task<Result<string>> GetContentUriAsync(string id) =>
        Task.FromResult<Result<string>>(new Success<string>(id));

    public Task<Result<string>> TryReadTextAsync(string id) =>
        Task.FromResult<Result<string>>(_text.TryGetValue(id, out var t)
            ? new Success<string>(t)
            : new Failure<string>(new IOException(id)));
}

public sealed class FakeParser : ICodeParser
{
    public ParsedCode ParseCode(string source, string filename) =>
        new(filename, source, HighlightIndex.Empty(), Array.Empty<Glance.Native.OutlineNode>());
}

public static class Artifacts
{
    public static IArtifact File(string path, long size = 1024, long lastMod = 1) =>
        new LocalArtifact(
            Id: path,
            Name: Path.GetFileName(path),
            Size: size,
            LastMod: lastMod,
            Extension: Path.GetExtension(path).ToLowerInvariant(),
            Kind: ArtifactKind.Text,
            Local: new LocalPayload(path, Path.GetDirectoryName(path) ?? "", false));

    public static IArtifact Dir(string path) =>
        new LocalArtifact(
            Id: path,
            Name: Path.GetFileName(path.TrimEnd('\\', '/')),
            Size: 0,
            LastMod: 1,
            Extension: "",
            Kind: ArtifactKind.Text,
            Local: new LocalPayload(path, Path.GetDirectoryName(path) ?? "", true));
}
