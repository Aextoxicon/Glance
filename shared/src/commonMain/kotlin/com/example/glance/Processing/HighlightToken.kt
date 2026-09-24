package com.example.glance.Processing

data class HighlightToken(
    val startByte: Int,
    val endByte: Int,
    val kind: String,
)