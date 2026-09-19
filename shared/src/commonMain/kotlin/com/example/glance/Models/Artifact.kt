package com.example.glance.Models

enum class ArtifactKind {
    Image,
    Video,
    Audio,
    Pdf,
    Text,
    Archive,
}

object ArtifactKindExt {
    private val extensionMap: Map<String, ArtifactKind> = mapOf(
        "jpg" to ArtifactKind.Image,
        "jpeg" to ArtifactKind.Image,
        "png" to ArtifactKind.Image,
        "gif" to ArtifactKind.Image,
        "bmp" to ArtifactKind.Image,
        "webp" to ArtifactKind.Image,
        "svg" to ArtifactKind.Image,
        "mp4" to ArtifactKind.Video,
        "avi" to ArtifactKind.Video,
        "mkv" to ArtifactKind.Video,
        "mov" to ArtifactKind.Video,
        "mp3" to ArtifactKind.Audio,
        "wav" to ArtifactKind.Audio,
        "flac" to ArtifactKind.Audio,
        "aac" to ArtifactKind.Audio,
        "pdf" to ArtifactKind.Pdf,
        "zip" to ArtifactKind.Archive,
        "rar" to ArtifactKind.Archive,
        "7z" to ArtifactKind.Archive,
        "tar" to ArtifactKind.Archive,
        "gz" to ArtifactKind.Archive,
    )

    fun fromExtension(ext: String): ArtifactKind {
        val clean = ext.trimStart('.').lowercase()
        return extensionMap[clean] ?: ArtifactKind.Text
    }
}

enum class ArtifactSource {
    Local,
    BackendChat,
}

// Artifact状态。
enum class ArtifactStatus {
    Available,
    Loading,
    Error,
    Unavailable,
}

// Artifact载荷接口（标记接口，具体类型由实现定义）。
interface IArtifactPayload

// Artifact接口。
interface IArtifact {
    val id: String
    val name: String
    val size: Long
    val lastMod: Long
    val extension: String
    val kind: ArtifactKind
    val source: ArtifactSource
    val status: ArtifactStatus
    val metadata: ArtifactMetadata?
    val payload: IArtifactPayload
}

// Artifact元数据。
data class ArtifactMetadata(
    val checksum: String?,
    val thumbnail: String?,
    val extra: Map<String, String>,
)

// 本地文件系统的payload包含文件系统路径信息
data class LocalPayload(
    val absolutePath: String,
    val parentPath: String,
    val isDir: Boolean,
) : IArtifactPayload

data class LocalArtifact(
    override val id: String,
    override val name: String,
    override val size: Long,
    override val lastMod: Long,
    override val extension: String,
    override val kind: ArtifactKind,
    val local: LocalPayload,
    override val status: ArtifactStatus = ArtifactStatus.Available,
    override val metadata: ArtifactMetadata? = null,
) : IArtifact {
    override val source: ArtifactSource get() = ArtifactSource.Local
    override val payload: IArtifactPayload get() = local
}

// 后端协作的payload，Backend*都是插桩，no！del！
data class BackendPayload(
    val messageId: String,
    val downloadUri: String,
    val thumbnailUri: String? = null,
) : IArtifactPayload

data class BackendArtifact(
    override val id: String,
    override val name: String,
    override val size: Long,
    override val lastMod: Long,
    override val extension: String,
    override val kind: ArtifactKind,
    override val payload: BackendPayload,
    override val status: ArtifactStatus = ArtifactStatus.Available,
    override val metadata: ArtifactMetadata? = null,
) : IArtifact {
    override val source: ArtifactSource get() = ArtifactSource.BackendChat
}
