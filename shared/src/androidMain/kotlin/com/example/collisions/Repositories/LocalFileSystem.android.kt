package com.example.glance.Repositories

import android.content.ContentResolver
import android.net.Uri
import android.provider.DocumentsContract
import androidx.documentfile.provider.DocumentFile
import com.example.glance.AndroidContext
import java.io.FileNotFoundException

// 基于SAF
actual class LocalFileSystem : TextFileDetector {
    private val contentResolver: ContentResolver
        get() = AndroidContext.context.contentResolver

    actual fun listFiles(path: String): List<LocalFileInfo> {
        val treeUri = Uri.parse(path)
        val docFile = DocumentFile.fromTreeUri(AndroidContext.context, treeUri)
            ?: return emptyList()
        val children = docFile.listFiles()
        val parentUri = treeUri.toString()
        return children.mapNotNull { item ->
            mapDocumentFile(item, parentUri)
        }
    }

    actual fun fileInfo(path: String): LocalFileInfo {
        val uri = Uri.parse(path)
        val docFile = DocumentFile.fromSingleUri(AndroidContext.context, uri)
            ?: throw FileNotFoundException("文件或目录不存在: $path")
        return mapDocumentFile(docFile, docFile.parentFile?.uri?.toString() ?: "")
            ?: throw FileNotFoundException("无法解析文件信息: $path")
    }

    actual override fun isTextFile(path: String): Boolean {
        val uri = Uri.parse(path)
        val ext = getExtension(uri)
        val name = getName(uri)

        if (ext in textExt) return true
        if (name in textFileNames) return true

        return try {
            contentResolver.openInputStream(uri)?.use { input ->
                val buffer = ByteArray(16384)
                val bytesRead = input.read(buffer)
                for (i in 0 until bytesRead) {
                    if (buffer[i] == 0.toByte()) return false
                }
                true
            } ?: false
        } catch (_: Exception) {
            false
        }
    }

    actual fun tryReadText(path: String): String? {
        return try {
            val uri = Uri.parse(path)
            if (!isTextFile(path)) return null
            contentResolver.openInputStream(uri)?.use { input ->
                val text = input.bufferedReader().readText()
                if (text.contains('\u0000')) null else text
            }
        } catch (_: Exception) {
            null
        }
    }

    actual fun delete(path: String): Boolean {
        return try {
            val uri = Uri.parse(path)
            val docFile = DocumentFile.fromSingleUri(AndroidContext.context, uri)
            docFile?.delete() ?: false
        } catch (_: SecurityException) {
            false
        }
    }

    actual fun toUri(path: String): String {
        // 已经是content:// URI
        return path
    }

    private fun mapDocumentFile(docFile: DocumentFile, parentUri: String): LocalFileInfo? {
        return try {
            val name = docFile.name ?: return null
            val isDir = docFile.isDirectory
            LocalFileInfo(
                path = docFile.uri.toString(),
                name = name,
                parentPath = parentUri,
                isDir = isDir,
                size = if (isDir) docFile.listFiles().size.toLong() else docFile.length(),
                lastMod = docFile.lastModified() / 1000L,
                extension = if (isDir) "" else name.substringAfterLast('.', "").lowercase(),
            )
        } catch (_: SecurityException) {
            null
        }
    }

    private fun getExtension(uri: Uri): String {
        val name = getName(uri)
        return name.substringAfterLast('.', "").lowercase()
    }

    private fun getName(uri: Uri): String {
        val cursor = contentResolver.query(uri, null, null, null, null)
        cursor?.use { c ->
            if (c.moveToFirst()) {
                val nameIndex = c.getColumnIndex(DocumentsContract.Document.COLUMN_DISPLAY_NAME)
                if (nameIndex >= 0) {
                    return c.getString(nameIndex) ?: ""
                }
            }
        }
        // fallback:从URI路径中提取
        val path = uri.path ?: ""
        return path.substringAfterLast('/')
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