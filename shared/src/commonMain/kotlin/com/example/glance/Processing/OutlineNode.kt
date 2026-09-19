package com.example.glance.Processing

data class OutlineNode(
    val kind: String,
    val name: String,
    val detail: String,
    val startByte: Long,
    val endByte: Long,
    val children: List<OutlineNode> = emptyList(),
)