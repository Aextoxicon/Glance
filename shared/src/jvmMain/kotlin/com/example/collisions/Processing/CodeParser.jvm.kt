package com.example.glance.Processing

import uniffi.uniffi_code_parser.parseCode as uniffiParseCode
import uniffi.uniffi_code_parser.CodeParseResult as UniffiCodeParseResult

actual fun parseCode(source: String, extension: String): CodeParseResult {
    val language = extension.trimStart('.')
    return try {
        val result: UniffiCodeParseResult = uniffiParseCode(source, extension)
        CodeParseResult.Code(
            language = language,
            content = source,
            highlightsByLine = result.highlightsByLine.map { line ->
                line.map { token ->
                    HighlightToken(
                        startByte = token.startByte.toLong(),
                        endByte = token.endByte.toLong(),
                        kind = token.kind,
                    )
                }
            },
            outline = result.outline.map { it.toKt() },
        )
    } catch (e: UnsatisfiedLinkError) {
        // 原生库不可用时，返回空解析结果
        CodeParseResult.Code(
            language = language,
            content = source,
            highlightsByLine = emptyList(),
            outline = emptyList(),
        )
    }
}

private fun uniffi.uniffi_code_parser.OutlineNode.toKt(): com.example.glance.Processing.OutlineNode =
    com.example.glance.Processing.OutlineNode(
        kind = kind,
        name = name,
        detail = detail,
        startByte = startByte.toLong(),
        endByte = endByte.toLong(),
        children = children.map { it.toKt() },
    )
