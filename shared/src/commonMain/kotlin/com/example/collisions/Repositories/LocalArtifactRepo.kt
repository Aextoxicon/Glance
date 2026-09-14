package com.example.glance.Repositories

import com.example.glance.Models.ArtifactKindExt
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Utils.Result

class LocalArtifactRepo(
    private val fs: LocalFileSystem,
) : IArtifactRepo {
    override suspend fun listAsync(path: String): Result<List<IArtifact>> {
        return try {
            val artifacts = fs.listFiles(path)
                .map { it.toArtifact() }
                .toList<IArtifact>()
            Result.success(artifacts)
        } catch (e: Exception) {
            Result.failure(Exception("无法列出文件: ${e.message}"))
        }
    }

    override suspend fun getAsync(id: String): Result<IArtifact> {
        return try {
            val info = fs.fileInfo(id)
            Result.success(info.toArtifact())
        } catch (e: Exception) {
            Result.failure(Exception("无法获取文件信息: ${e.message}"))
        }
    }

    override suspend fun searchAsync(query: String): Result<List<IArtifact>> {
        return Result.success(emptyList())
    }

    override suspend fun deleteAsync(id: String): Result<Boolean> {
        return try {
            val ok = fs.delete(id)
            Result.success(ok)
        } catch (e: Exception) {
            Result.failure(Exception("无法删除文件: ${e.message}"))
        }
    }

    override suspend fun getContUriAsync(id: String): Result<String> {
        return try {
            val uri = fs.toUri(id)
            Result.success(uri)
        } catch (e: Exception) {
            Result.failure(Exception("无法获取文件URI: ${e.message}"))
        }
    }

    override suspend fun tryReadTextAsync(id: String): Result<String> {
        val content = fs.tryReadText(id)
        return if (content != null) {
            Result.success(content)
        } else {
            Result.failure(Exception("无法读取文件内容或文件不是文本"))
        }
    }
}

internal fun LocalFileInfo.toArtifact(): LocalArtifact {
    return LocalArtifact(
        id = path,
        name = name,
        size = size,
        lastMod = lastMod,
        extension = extension,
        kind = ArtifactKindExt.fromExtension(extension),
        local = LocalPayload(
            absolutePath = path,
            parentPath = parentPath,
            isDir = isDir,
        ),
    )
}