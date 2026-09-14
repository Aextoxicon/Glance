package com.example.collisions.Processing

/**
 * 将 [CodeParseResult] 序列化为纯文本快照，用于 golden 对比。
 * PlainText 类型仅记录 language 和 contentLen。
 */
fun serializeParseResult(result: CodeParseResult): String {
    return when (result) {
        is CodeParseResult.Code -> buildString {
            append("language=${result.language}\n")
            append("contentLen=${result.content.length}\n")
            append("highlightLineCount=${result.highlightsByLine.size}\n")
            val totalTokens = result.highlightsByLine.sumOf { it.size }
            append("totalTokens=$totalTokens\n")

            result.highlightsByLine.forEachIndexed { lineIdx, tokens ->
                if (tokens.isEmpty()) return@forEachIndexed
                append("line_$lineIdx=[")
                tokens.forEachIndexed { idx, token ->
                    if (idx > 0) append(",")
                    append("${token.startByte}-${token.endByte}:${token.kind}")
                }
                append("]\n")
            }

            append("outline=\n")
            result.outline.forEach { node ->
                appendOutlineNode(this, node, indent = 0)
            }
        }
        is CodeParseResult.PlainText -> buildString {
            append("language=${result.language}\n")
            append("contentLen=${result.content.length}\n")
            append("type=PlainText\n")
        }
    }
}

private fun appendOutlineNode(sb: StringBuilder, node: OutlineNode, indent: Int) {
    val prefix = "  ".repeat(indent)
    val detailSuffix = if (node.detail.isBlank()) "" else " ${node.detail}"
    sb.append("${prefix}${node.kind}:${node.name}(${node.startByte}-${node.endByte})$detailSuffix\n")
    node.children.forEach { child ->
        appendOutlineNode(sb, child, indent + 1)
    }
}
