package com.example.glance.Processing

import uniffi.uniffi_code_parser.parseCode as uniffiParseCode
import uniffi.uniffi_code_parser.CodeParseResult as UniffiCodeParseResult

@Volatile
private var nativeUnavailable = false

actual fun parseCode(source: String, extension: String): CodeParseResult {
    val language = extension.trimStart('.')
    return try {
        if (nativeUnavailable) {
            // 直接降级
            return fallbackResult(language, source)
        }
        val result: UniffiCodeParseResult = uniffiParseCode(source, extension)
        CodeParseResult.Code(
            language = language,
            content = source,
            highlights = HighlightIndex(result.highlightData, result.lineIndex, result.kinds),
            outline = result.outline.map { it.toKt() },
        )
    } catch (e: UnsatisfiedLinkError) {
        // .so未加载，永久降级
        nativeUnavailable = true
        println("parseCode native unavailable: $e")
        fallbackResult(language, source)
    } catch (e: Exception) {
        // 解析异常不永久降级
        println("parseCode native error for $extension: $e")
        fallbackResult(language, source)
    }
}

private fun fallbackResult(language: String, source: String): CodeParseResult.Code =
    CodeParseResult.Code(
        language = language,
        content = source,
        highlights = HighlightIndex.empty(),
        outline = emptyList(),
    )

private fun uniffi.uniffi_code_parser.OutlineNode.toKt(): com.example.glance.Processing.OutlineNode =
    com.example.glance.Processing.OutlineNode(
        kind = kind,
        name = name,
        detail = detail,
        startByte = startByte.toLong(),
        endByte = endByte.toLong(),
        children = children.map { it.toKt() },
    )
