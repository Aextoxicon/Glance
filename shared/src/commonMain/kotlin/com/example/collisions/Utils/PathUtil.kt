package com.example.collisions.Utils

object PathUtil {
    fun fileName(path: String): String {
        val trimmed = path.trimEnd('/', '\\')
        if (trimmed.isEmpty()) return ""
        val idx = maxOf(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\'))
        return if (idx < 0) trimmed else trimmed.substring(idx + 1)
    }
}