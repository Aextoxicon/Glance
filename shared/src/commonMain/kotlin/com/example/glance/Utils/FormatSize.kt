package com.example.glance.Utils

object FormatSize {
    fun readable(bytes: Long): String {
        val suffixes = arrayOf("B", "KB", "MB", "GB", "TB")
        var order = 0
        var size = bytes.toDouble()

        while (size >= 1024 && order < suffixes.size - 1) {
            order++
            size /= 1024
        }

        return if (order == 0) {
            "$bytes B"
        } else {
            val formatted = if (size >= 100) {
                size.toInt().toString()
            } else {
                val rounded = kotlin.math.round(size * 100) / 100
                rounded.toString().trimEnd('0').trimEnd('.')
            }
            "$formatted ${suffixes[order]}"
        }
    }
}