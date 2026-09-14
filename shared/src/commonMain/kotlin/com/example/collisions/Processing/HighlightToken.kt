package com.example.glance.Processing

data class HighlightToken(
    val startByte: Long,
    val endByte: Long,
    val kind: String,
)