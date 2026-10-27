using System.Collections.Generic;
using System.Threading.Tasks;
using Glance.Models;
using Glance.Utils;

namespace Glance.Repositories;

public sealed class BackendArtifactRepo : IArtifactRepo
{
    private readonly BackendApiClient _apiClient;

    public BackendArtifactRepo(BackendApiClient apiClient) => _apiClient = apiClient;

    public Task<Result<IReadOnlyList<IArtifact>>> ListAsync(string path) =>
        throw new System.NotImplementedException("TODO: 从会话消息中提取文件附件列表");
    public Task<Result<IReadOnlyList<IArtifact>>> SearchAsync(string query) =>
        throw new System.NotImplementedException("TODO: 搜索消息中的文件");
    public Task<Result<IArtifact>> GetAsync(string id) =>
        throw new System.NotImplementedException("TODO: 根据fileId获取文件元信息");
    public Task<Result<bool>> DeleteAsync(string id) =>
        throw new System.NotImplementedException("TODO: 删除消息中的文件附件");
    public Task<Result<string>> GetContentUriAsync(string id) =>
        throw new System.NotImplementedException("TODO: 通过预签名URL获取文件下载地址");
    public Task<Result<string>> TryReadTextAsync(string id) =>
        throw new System.NotImplementedException("TODO: 通过预签名URL下载文件内容");
    public Task<Result<byte[]>> TryReadBytesAsync(string id) =>
        throw new System.NotImplementedException("TODO: 通过预签名URL下载文件内容");
}
