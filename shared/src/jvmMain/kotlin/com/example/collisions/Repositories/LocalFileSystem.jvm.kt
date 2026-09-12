package com.example.collisions.Repositories

import java.io.File
import java.io.FileInputStream
import java.io.FileNotFoundException

actual class LocalFileSystem : TextFileDetector {
    actual fun listFiles(path: String): List<LocalFileInfo> {
        val dir = File(path)
        if (!dir.exists() || !dir.isDirectory) return emptyList()
        return dir.listFiles()?.mapNotNull { item ->
            try {
                LocalFileInfo(
                    path = item.absolutePath,
                    name = item.name,
                    parentPath = dir.absolutePath,
                    isDir = item.isDirectory,
                    size = if (item.isDirectory) countDirChildren(item.absolutePath) else item.length(),
                    lastMod = item.lastModified() / 1000L,
                    extension = if (item.isDirectory) "" else item.extension.lowercase(),
                )
            } catch (_: SecurityException) {
                null
            }
        }?.toList() ?: emptyList()
    }

    actual fun fileInfo(path: String): LocalFileInfo {
        val file = File(path)
        if (!file.exists()) {
            val dir = File(path)
            if (dir.exists() && dir.isDirectory) {
                return LocalFileInfo(
                    path = dir.absolutePath,
                    name = dir.name,
                    parentPath = dir.parent ?: "",
                    isDir = true,
                    size = countDirChildren(path),
                    lastMod = dir.lastModified() / 1000L,
                    extension = "",
                )
            }
            throw FileNotFoundException("文件或目录不存在: $path")
        }
        return LocalFileInfo(
            path = file.absolutePath,
            name = file.name,
            parentPath = file.parent ?: "",
            isDir = false,
            size = file.length(),
            lastMod = file.lastModified() / 1000L,
            extension = file.extension.lowercase(),
        )
    }

    actual override fun isTextFile(path: String): Boolean {
        val ext = File(path).extension.lowercase()
        val name = File(path).name.lowercase()

        if (ext in textExt) return true
        if (name in textFileNames) return true

        // 空字节检测：读取前 16KB，含 \0 则视为二进制
        return try {
            FileInputStream(path).use { fis ->
                val buffer = ByteArray(16384)
                val bytesRead = fis.read(buffer)
                for (i in 0 until bytesRead) {
                    if (buffer[i] == 0.toByte()) return false
                }
                true
            }
        } catch (_: Exception) {
            false
        }
    }

    actual fun tryReadText(path: String): String? {
        return try {
            val file = File(path)
            if (!file.exists() || !file.isFile) return null
            if (!isTextFile(path)) return null
            val content = file.readText()
            if (content.contains('\u0000')) null else content
        } catch (_: Exception) {
            null
        }
    }

    actual fun delete(path: String): Boolean {
        return try {
            val file = File(path)
            if (file.isDirectory) {
                file.deleteRecursively()
            } else {
                file.delete()
            }
        } catch (_: SecurityException) {
            false
        }
    }

    actual fun toUri(path: String): String {
        return File(path).toURI().toString()
    }

    private fun countDirChildren(dirPath: String): Long {
        return try {
            File(dirPath).list()?.size?.toLong() ?: 0L
        } catch (_: Exception) {
            0L
        }
    }

    companion object {
        private val textExt = setOf(
            "txt", "md", "markdown", "json", "xml", "yaml", "yml",
            "cs", "js", "ts", "jsx", "tsx", "py", "java", "kt",
            "kts", "swift", "c", "cpp", "h", "hpp", "css", "scss",
            "less", "html", "htm", "sh", "bash", "zsh", "ps1",
            "bat", "cmd", "sql", "r", "go", "rs", "toml", "ini",
            "cfg", "conf", "env", "gitignore", "gradle", "sln",
            "csproj", "props", "targets", "razor",
            "fs", "fsx", "dart", "lua", "pl", "pm", "rb", "php",
            "scala", "clj", "cljs", "edn", "coffee", "vue", "svelte",
            "astro", "svg", "graphql", "proto", "cmake", "m", "mm",
        )

        private val textFileNames = setOf(
            "dockerfile", "makefile", "gnumakefile", "cmakelists",
            "readme", "license", "changelog", "contributing",
            "authors", "todo", "notes", "help",
        )
    }
}