package com.example.glance.Repositories

import kotlinx.cinterop.ExperimentalForeignApi
import kotlinx.cinterop.addressOf
import kotlinx.cinterop.usePinned
import platform.Foundation.*
import platform.posix.*

@OptIn(ExperimentalForeignApi::class)
actual class LocalFileSystem {
    actual fun listFiles(path: String): List<LocalFileInfo> {
        val fm = NSFileManager.defaultManager
        val contents = fm.contentsOfDirectoryAtPath(path, null) ?: return emptyList()
        return contents.mapNotNull { item ->
            val name = item as? String ?: return@mapNotNull null
            val fullPath = "$path/$name"
            val attrs = fm.attributesOfItemAtPath(fullPath, null) ?: return@mapNotNull null
            val isDir = attrs[NSFileType] as? String == NSFileTypeDirectory
            val size = (attrs[NSFileSize] as? NSNumber)?.longValue ?: 0L
            val modDate = attrs[NSFileModificationDate] as? NSDate
            val lastMod = (modDate?.timeIntervalSince1970 ?: 0.0).toLong()

            LocalFileInfo(
                path = fullPath,
                name = name,
                parentPath = path,
                isDir = isDir,
                size = if (isDir) size else (attrs[NSFileSize] as? NSNumber)?.longValue ?: 0L,
                lastMod = lastMod,
                extension = if (isDir) "" else name.substringAfterLast('.', "").lowercase(),
            )
        }
    }

    actual fun fileInfo(path: String): LocalFileInfo {
        val fm = NSFileManager.defaultManager
        val exists = fm.fileExistsAtPath(path)

        if (!exists) {
            throw Exception("文件或目录不存在: $path")
        }

        val attrs = fm.attributesOfItemAtPath(path, null) ?: throw Exception("无法获取文件属性: $path")
        val type = attrs[NSFileType] as? String
        val isDirectory = type == NSFileTypeDirectory
        val name = path.substringAfterLast('/')
        val parentPath = path.substringBeforeLast('/')

        return LocalFileInfo(
            path = path,
            name = name,
            parentPath = parentPath,
            isDir = isDirectory,
            size = (attrs[NSFileSize] as? NSNumber)?.longValue ?: 0L,
            lastMod = ((attrs[NSFileModificationDate] as? NSDate)?.timeIntervalSince1970 ?: 0.0).toLong(),
            extension = if (isDirectory) "" else name.substringAfterLast('.', "").lowercase(),
        )
    }

    actual fun isTextFile(path: String): Boolean {
        val ext = path.substringAfterLast('.', "").lowercase()
        val name = path.substringAfterLast('/').lowercase()

        if (ext in textExt) return true
        if (name in textFileNames) return true

        // 空字节检测：读取前 16KB，含 \0 则视为二进制
        return try {
            val data = NSData.dataWithContentsOfFile(path) ?: return false
            if (data.length == 0UL) return true
            val buffer = ByteArray(minOf(data.length.toInt(), 16384))
            buffer.usePinned { pinned ->
                data.getBytes(pinned.addressOf(0), buffer.size.toULong())
            }
            for (b in buffer) {
                if (b == 0.toByte()) return false
            }
            true
        } catch (_: Exception) {
            false
        }
    }

    actual fun tryReadText(path: String): String? {
        return try {
            val fm = NSFileManager.defaultManager
            if (!fm.fileExistsAtPath(path)) return null
            if (!isTextFile(path)) return null
            val content = NSString.stringWithContentsOfFile(path, NSUTF8StringEncoding, null) ?: return null
            if (content.contains('\u0000')) null else content
        } catch (_: Exception) {
            null
        }
    }

    actual fun delete(path: String): Boolean {
        return try {
            NSFileManager.defaultManager.removeItemAtPath(path, null)
        } catch (_: Exception) {
            false
        }
    }

    actual fun toUri(path: String): String {
        return NSURL.fileURLWithPath(path).absoluteString ?: path
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

@OptIn(ExperimentalForeignApi::class)
private fun ByteArray.refTo(index: Int): kotlinx.cinterop.CValuesRef<kotlinx.cinterop.ByteVar> {
    return this.usePinned { it.addressOf(index) }
}