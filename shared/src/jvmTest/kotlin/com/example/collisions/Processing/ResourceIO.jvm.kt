package com.example.collisions.Processing

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
