package com.example.glance.Processing

import com.example.glance.Models.ArtifactKind
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Repositories.IArtifactRepo
import com.example.glance.Utils.Result

/**
 * Mock工作区，实现[IArtifactRepo]以驱动[MainViewModel]的文件浏览和解析流程
 */
class TemplateWorkspaceRepo(
    private val workspacePath: String,
    private val templates: Map<String, String>,
) : IArtifactRepo {

    override suspend fun listAsync(path: String): Result<List<IArtifact>> {
        if (path != workspacePath) return Result.success(emptyList())

        val artifacts = templates.keys.map { name ->
            val content = templates[name] ?: ""
            val ext = name.substringAfterLast('.', "")
            LocalArtifact(
                id = "$workspacePath/$name",
                name = name,
                size = content.length.toLong(),
                lastMod = 0L,
                extension = ext,
                kind = ArtifactKind.Text,
                local = LocalPayload(
                    absolutePath = "$workspacePath/$name",
                    parentPath = workspacePath,
                    isDir = false,
                ),
            )
        }
        return Result.success(artifacts)
    }

    override suspend fun tryReadTextAsync(id: String): Result<String> {
        val name = id.substringAfterLast('/')
        val content = templates[name]
            ?: return Result.failure(IllegalArgumentException("not found: $id"))
        return Result.success(content)
    }

    override suspend fun searchAsync(query: String): Result<List<IArtifact>> =
        Result.success(emptyList())

    override suspend fun getAsync(id: String): Result<IArtifact> =
        Result.failure(IllegalArgumentException("unexpected getAsync($id)"))

    override suspend fun deleteAsync(id: String): Result<Boolean> =
        Result.success(true)

    override suspend fun getContUriAsync(id: String): Result<String> =
        Result.failure(IllegalArgumentException("unexpected getContUriAsync($id)"))
}
