package com.example.glance.Repositories

import com.example.glance.Models.IArtifact
import com.example.glance.Utils.Result

class BackendArtifactRepo(
    private val apiClient: BackendApiClient,
) : IArtifactRepo {
    override suspend fun listAsync(path: String): Result<List<IArtifact>> =
        TODO("TODO: 从会话消息中提取文件附件列表")

    override suspend fun searchAsync(query: String): Result<List<IArtifact>> =
        TODO("TODO: 搜索消息中的文件")

    override suspend fun getAsync(id: String): Result<IArtifact> =
        TODO("TODO: 根据 fileId 获取文件元信息")

    override suspend fun deleteAsync(id: String): Result<Boolean> =
        TODO("TODO: 删除消息中的文件附件")

    override suspend fun getContUriAsync(id: String): Result<String> =
        TODO("TODO: 通过预签名 URL 获取文件下载地址")

    override suspend fun tryReadTextAsync(id: String): Result<String> =
        TODO("TODO: 通过预签名 URL 下载文件内容")
}