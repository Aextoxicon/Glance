package com.example.glance.Processing

import kotlinx.cinterop.ExperimentalForeignApi
import platform.Foundation.NSString
import platform.Foundation.NSURL
import platform.Foundation.NSFileManager
import platform.posix.stat

actual fun loadResource(path: String): String? {
    return try {
        val bundle = io.ktor.utils.io.core.Bundle.main
        val stream = bundle.getResourceAsStream(path) ?: return null
        stream.use { it.reader().readText() }
    } catch (e: Exception) {
        null
    }
}

actual fun saveResource(path: String, content: String) {
    val fileName = path.substringAfterLast('/')
    val dirPath = NSFileManager.defaultManager.getTemporaryDirectory() + "generated/resources/golden"
    NSFileManager.defaultManager.createDirectoryAtPath(dirPath, withIntermediateDirectories = true, attributes = null, error = null)
    val filePath = dirPath + "/" + fileName
    NSString(content).writeToFile(filePath, atomically = true, encoding = NSString.UTF8StringEncoding, error = null)
    println("Golden saved: $filePath")
}