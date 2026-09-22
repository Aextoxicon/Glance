package com.example.glance.Utils

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.withStyle
import com.example.glance.Processing.HighlightToken
import com.example.glance.Processing.OutlineNode

object HighlightColor {
    val keyword = Color(0xFFD73A49)
    val string = Color(0xFF09622A)
    val comment = Color(0xFF6A737D)
    val function = Color(0xFF8250DF)
    val functionBuiltin = Color(0xFF005CC5)
    val functionMethod = Color(0xFF6F42C1)
    val type = Color(0xFF6F42C1)
    val number = Color(0xFF0550AE)
    val operator = Color(0xFFD73A49)
    val identifier = Color(0xFF24292E)
    val variable = Color(0xFFE36209)
    val property = Color(0xFFEC09BE)
    val punctuation = Color(0xFF8C959F)
    val escape = Color(0xFFE36209)
    val constantBuiltin = Color(0xFF953800)
    val label = Color(0xFFE36209)
    val namespace = Color(0xFF28A745)
    val builtin = Color(0xFF6F42C1)
    val tag = Color(0xFF22863A)
    val constructor = Color(0xFF6F42C1)
    val module = Color(0xFF28A745)
    val error = Color(0xFFCF222E)
    val plainText = Color(0xFF24292E)

    private val colorMap: Map<String, Color> = mapOf(
        // 一级
        "keyword" to keyword,
        "string" to string,
        "comment" to comment,
        "function" to function,
        "type" to type,
        "number" to number,
        "operator" to operator,
        "builtin" to builtin,
        "identifier" to identifier,
        "variable" to variable,
        "parameter" to variable,
        "property" to property,
        "punctuation" to punctuation,
        "delimiter" to punctuation,
        "embedded" to punctuation,
        "escape" to escape,
        "constant" to constantBuiltin,
        "label" to label,
        "namespace" to namespace,
        "tag" to tag,
        "constructor" to constructor,
        "module" to module,
        "error" to error,
        "text" to string,
        "boolean" to keyword,
        "attribute" to keyword,
        "import" to keyword,
        "conditional" to keyword,
        "repeat" to keyword,
        "include" to keyword,
        "exception" to keyword,
        // 细分
        "function.builtin" to functionBuiltin,
        "function.method" to functionMethod,
        "string.escape" to escape,
        "string.special.key" to property,
        "constant.builtin" to constantBuiltin,
        "constant.macro" to constantBuiltin,
        "keyword.function" to functionBuiltin,
    )

    fun colorFor(kind: String): Color {
        colorMap[kind]?.let { return it }
        // 前缀降级
        var prefix = kind
        while (true) {
            val idx = prefix.lastIndexOf('.')
            if (idx < 0) break
            prefix = prefix.substring(0, idx)
            colorMap[prefix]?.let { return it }
        }
        return plainText
    }

    // 逐行构建
    fun buildLineAnnotatedString(
        line: String,
        tokens: List<HighlightToken>,
        colorDefault: Color = plainText,
    ): AnnotatedString {
        if (tokens.isEmpty()) {
            return AnnotatedString(line, spanStyle = SpanStyle(color = colorDefault))
        }
        return buildAnnotatedString {
            var pos = 0
            for (token in tokens) {
                val rawStart = token.startByte.toInt()
                val end = token.endByte.toInt().coerceAtMost(line.length)
                // 区间与已渲染部分重叠时只补未渲染的段
                if (end > pos) {
                    val start = rawStart.coerceAtLeast(pos).coerceAtMost(line.length)
                    if (start > pos) {
                        withStyle(SpanStyle(color = colorDefault)) {
                            append(line.substring(pos, start))
                        }
                    }
                    if (start < end) {
                        withStyle(SpanStyle(color = colorFor(token.kind))) {
                            append(line.substring(start, end))
                        }
                    }
                    pos = end
                }
            }
            if (pos < line.length) {
                withStyle(SpanStyle(color = colorDefault)) {
                    append(line.substring(pos))
                }
            }
        }
    }
}