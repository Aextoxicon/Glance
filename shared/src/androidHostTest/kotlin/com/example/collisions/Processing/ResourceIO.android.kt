package com.example.glance.Processing

import android.util.Log
import java.io.File

actual fun loadResource(path: String): String? {
    return try {
        val stream = Thread.currentThread().contextClassLoader.getResourceAsStream(path)
        stream?.use { it.reader().readText() }
    } catch (e: Exception) {
        Log.e("ResourceIO", "Failed to load $path", e)
        null
    }
}

actual fun saveResource(path: String, content: String) {
    val fileName = path.substringAfterLast('/')
    val dir = File(System.getProperty("user.dir"), "build/generated/resources/golden")
    dir.mkdirs()
    File(dir, fileName).writeText(content)
    Log.i("ResourceIO", "Golden saved: $dir/$fileName")
}