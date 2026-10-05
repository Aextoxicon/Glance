package com.example.glance.Processing

/// 高亮的紧凑存储：token打包成扁平ByteArray，按行建索引，惰性解出，
class HighlightIndex(
    private val data: ByteArray,
    private val lineIndex: ByteArray,
    private val kinds: List<String>,
) {
    /// 行索引的u32个数减一；行索引末元素是token总数构成的哨兵
    val lineCount: Int
        get() = (lineIndex.size / INT_BYTES - 1).coerceAtLeast(0)

    val totalTokenCount: Int
        get() = data.size / TOKEN_BYTES

    fun tokenCountOf(line: Int): Int {
        if (line < 0 || line >= lineCount) return 0
        return u32At(lineIndex, (line + 1) * INT_BYTES) - u32At(lineIndex, line * INT_BYTES)
    }

    /// 每次调用都会新建对象，勿当缓存用
    fun tokensOf(line: Int): List<HighlightToken> {
        val count = tokenCountOf(line)
        if (count == 0) return emptyList()
        var off = u32At(lineIndex, line * INT_BYTES) * TOKEN_BYTES
        val out = ArrayList<HighlightToken>(count)
        repeat(count) {
            val start = u32At(data, off)
            val end = u32At(data, off + INT_BYTES)
            val kindIndex = u32At(data, off + 2 * INT_BYTES)
            out.add(
                HighlightToken(
                    startByte = start,
                    endByte = end,
                    kind = if (kindIndex in kinds.indices) kinds[kindIndex] else "",
                )
            )
            off += TOKEN_BYTES
        }
        return out
    }

    /// 展开成一维列表。仅测试与诊断用 —— 大文件上这会重新制造出本类型要避免的对象
    fun allTokens(): List<HighlightToken> = (0 until lineCount).flatMap { tokensOf(it) }

    companion object {
        const val TOKEN_BYTES = 12
        private const val INT_BYTES = 4

        /// 无高亮时用它，避免为每个降级文件分配缓冲
        fun empty(): HighlightIndex = HighlightIndex(ByteArray(0), ByteArray(0), emptyList())

        private fun u32At(src: ByteArray, off: Int): Int =
            (src[off].toInt() and 0xFF) or
                ((src[off + 1].toInt() and 0xFF) shl 8) or
                ((src[off + 2].toInt() and 0xFF) shl 16) or
                ((src[off + 3].toInt() and 0xFF) shl 24)
    }
}
