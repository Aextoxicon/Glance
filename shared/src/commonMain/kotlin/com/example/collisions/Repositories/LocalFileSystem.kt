package com.example.collisions.Repositories

data class LocalFileInfo(
    val path: String,
    val name: String,
    val parentPath: String,
    val isDir: Boolean,
    val size: Long,
    val lastMod: Long,
    val extension: String,
)

interface TextFileDetector {
    fun isTextFile(path: String): Boolean
}

expect class LocalFileSystem() : TextFileDetector {
    fun listFiles(path: String): List<LocalFileInfo>
    fun fileInfo(path: String): LocalFileInfo
    override fun isTextFile(path: String): Boolean
    fun tryReadText(path: String): String?
    fun delete(path: String): Boolean
    fun toUri(path: String): String
}