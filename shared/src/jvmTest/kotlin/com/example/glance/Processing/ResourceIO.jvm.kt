package com.example.glance.Processing

import java.io.File

actual fun loadResource(path: String): String? {
    val stream = Thread.currentThread().contextClassLoader.getResourceAsStream(path)
    return stream?.use { it.reader().readText() }
}

actual fun saveResource(path: String, content: String) {
    val fileName = path.substringAfterLast('/')
    val dir = File(System.getProperty("user.dir"), "build/generated/resources/golden")
    dir.mkdirs()
    val file = File(dir, fileName)
    file.writeText(content)
    println("Golden saved: ${file.absolutePath}")
}

fun saveGoldenToSourceTree(path: String, content: String) {
    val file = resolveSourceTreeResource(path)
    file.parentFile.mkdirs()
    file.writeText(content)
    println("=== Golden updated: ${file.absolutePath} ===")
}

private fun resolveSourceTreeResource(path: String): File {
    var dir = File(System.getProperty("user.dir")).absoluteFile
    repeat(4) {
        val resources = File(dir, "src/commonTest/resources")
        if (resources.isDirectory) return resources.resolve(path)
        dir = dir.parentFile ?: return resources.resolve(path)
    }
    return File(dir, "src/commonTest/resources").resolve(path)
}
