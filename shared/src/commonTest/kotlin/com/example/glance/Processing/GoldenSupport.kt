package com.example.glance.Processing

/**
 * 将[CodeParseResult]序列化为纯文本快照，用于golden对比
 * PlainText类型仅记录language和contentLen
 */
fun serializeParseResult(result: CodeParseResult): String {
    return when (result) {
        is CodeParseResult.Code -> buildString {
            append("language=${result.language}\n")
            append("contentLen=${result.content.length}\n")
            append("highlightLineCount=${result.highlights.lineCount}\n")
            append("totalTokens=${result.highlights.totalTokenCount}\n")

            for (lineIdx in 0 until result.highlights.lineCount) {
                val tokens = result.highlights.tokensOf(lineIdx)
                if (tokens.isEmpty()) continue
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
