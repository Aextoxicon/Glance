using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Glance.Models;
using Glance.Utils;

namespace Glance.Repositories;

public sealed class LocalArtifactRepo : IArtifactRepo
{
    private readonly ILocalFileSystem _fs;

    public LocalArtifactRepo(ILocalFileSystem fs) => _fs = fs;

    public Task<Result<IReadOnlyList<IArtifact>>> ListAsync(string path)
    {
        try
        {
            var artifacts = _fs.ListFiles(path)
                .Select(info => info.ToArtifact())
                .ToList<IArtifact>();
            return Task.FromResult(Result<IReadOnlyList<IArtifact>>.Success(artifacts));
        }
        catch (Exception e)
        {
            return Task.FromResult(Result<IReadOnlyList<IArtifact>>.Failure(new Exception($"无法列出文件: {e.Message}")));
        }
    }

    public Task<Result<IArtifact>> GetAsync(string id)
    {
        try
        {
            var info = _fs.FileInfo(id);
            return Task.FromResult(Result<IArtifact>.Success(info.ToArtifact()));
        }
        catch (Exception e)
        {
            return Task.FromResult(Result<IArtifact>.Failure(new Exception($"无法获取文件信息: {e.Message}")));
        }
    }

    public Task<Result<IReadOnlyList<IArtifact>>> SearchAsync(string query) =>
        Task.FromResult(Result<IReadOnlyList<IArtifact>>.Success(Array.Empty<IArtifact>()));

    public Task<Result<bool>> DeleteAsync(string id)
    {
        try
        {
            var ok = _fs.Delete(id);
            return Task.FromResult(Result<bool>.Success(ok));
        }
        catch (Exception e)
        {
            return Task.FromResult(Result<bool>.Failure(new Exception($"无法删除文件: {e.Message}")));
        }
    }

    public Task<Result<string>> GetContentUriAsync(string id)
    {
        try
        {
            var uri = _fs.ToUri(id);
            return Task.FromResult(Result<string>.Success(uri));
        }
        catch (Exception e)
        {
            return Task.FromResult(Result<string>.Failure(new Exception($"无法获取文件URI: {e.Message}")));
        }
    }

    public Task<Result<string>> TryReadTextAsync(string id)
    {
        var content = _fs.TryReadText(id);
        return content != null
            ? Task.FromResult(Result<string>.Success(content))
            : Task.FromResult(Result<string>.Failure(new Exception("无法读取文件内容或文件不是文本")));
    }
}

internal static class LocalFileInfoExtensions
{
    public static LocalArtifact ToArtifact(this LocalFileInfo info) => new(
        Id: info.Path,
        Name: info.Name,
        Size: info.Size,
        LastMod: info.LastMod,
        Extension: info.Extension,
        Kind: ArtifactKindExt.FromExtension(info.Extension),
        Local: new LocalPayload(info.Path, info.ParentPath, info.IsDir)
    );
}
