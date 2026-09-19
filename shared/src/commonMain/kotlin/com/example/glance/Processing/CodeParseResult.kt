package com.example.glance.Processing

sealed class CodeParseResult {
    data class Code(
        val language: String,
        val content: String,
        val highlightsByLine: List<List<HighlightToken>>,
        val outline: List<OutlineNode>,
    ) : CodeParseResult()

    data class PlainText(
        val language: String,
        val content: String,
    ) : CodeParseResult()
}