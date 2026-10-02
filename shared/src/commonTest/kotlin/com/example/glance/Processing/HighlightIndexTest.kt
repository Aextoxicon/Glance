package com.example.glance.Processing

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * 覆盖 [HighlightIndex] 的打包布局解读，这里只测 Kotlin 侧，
 * 与 Rust 打包端的对齐由 golden 快照（LanguageGoldenTest）保证
 */
class HighlightIndexTest {

    @Test
    fun `按行解出 token`() {
        val index = buildIndex(
            rows = listOf(
                listOf(), // 第 0 行是空行
                listOf(triple(0, 3, 0), triple(4, 9, 1)),
                listOf(triple(0, 1, 2)),
            ),
            kinds = listOf("keyword", "function", "string"),
        )

        assertEquals(3, index.lineCount)
        assertEquals(3, index.totalTokenCount)

        assertTrue(index.tokensOf(0).isEmpty(), "空行应返回空表")
        assertEquals(
            listOf(HighlightToken(0, 3, "keyword"), HighlightToken(4, 9, "function")),
            index.tokensOf(1),
        )
        assertEquals(listOf(HighlightToken(0, 1, "string")), index.tokensOf(2))
    }

    @Test
    fun `行区间由相邻索引界定，最后一行以总数收尾`() {
        val index = buildIndex(
            rows = listOf(listOf(triple(0, 1, 0)), listOf(triple(0, 2, 0))),
            kinds = listOf("keyword"),
        )
        assertEquals(1, index.tokenCountOf(0))
        assertEquals(1, index.tokenCountOf(1))
        assertEquals(2, index.allTokens().size)
    }

    @Test
    fun `越界行不抛异常，返回空表`() {
        val index = buildIndex(rows = listOf(listOf(triple(0, 1, 0))), kinds = listOf("keyword"))
        assertTrue(index.tokensOf(-1).isEmpty())
        assertTrue(index.tokensOf(1).isEmpty())
        assertTrue(index.tokensOf(Int.MAX_VALUE).isEmpty())
        assertEquals(0, index.tokenCountOf(-1))
        assertEquals(0, index.tokenCountOf(99))
    }

    @Test
    fun `kind 下标越界降级为空串而不是崩溃`() {
        val index = buildIndex(rows = listOf(listOf(triple(0, 1, 7))), kinds = listOf("keyword"))
        assertEquals("", index.tokensOf(0).single().kind)
    }

    @Test
    fun `空索引退化为零行零 token`() {
        val index = HighlightIndex.empty()
        assertEquals(0, index.lineCount)
        assertEquals(0, index.totalTokenCount)
        assertTrue(index.allTokens().isEmpty())
        assertTrue(index.tokensOf(0).isEmpty())
    }

    @Test
    fun `保留 start 大于 end 的原始数据，不做纠正`() {
        // 纠正属于打包端职责，读取端应保持原样，否则会掩盖上游问题
        val index = buildIndex(rows = listOf(listOf(triple(5, 2, 0))), kinds = listOf("keyword"))
        assertEquals(HighlightToken(5, 2, "keyword"), index.tokensOf(0).single())
    }

    private fun triple(start: Int, end: Int, kind: Int) = Triple(start, end, kind)

    private fun buildIndex(rows: List<List<Triple<Int, Int, Int>>>, kinds: List<String>): HighlightIndex {
        val bytesPerToken = 12
        val total = rows.sumOf { it.size }
        val data = ByteArray(total * bytesPerToken)
        val lineIndex = ByteArray((rows.size + 1) * 4)
        var p = 0
        var seq = 0
        rows.forEachIndexed { rowIdx, row ->
            writeInt(lineIndex, rowIdx * 4, seq)
            for ((start, end, kind) in row) {
                writeInt(data, p, start)
                writeInt(data, p + 4, end)
                writeInt(data, p + 8, kind)
                p += bytesPerToken
                seq++
            }
        }
        writeInt(lineIndex, rows.size * 4, seq)
        return HighlightIndex(data, lineIndex, kinds)
    }

    private fun writeInt(dst: ByteArray, off: Int, value: Int) {
        dst[off] = (value and 0xFF).toByte()
        dst[off + 1] = ((value shr 8) and 0xFF).toByte()
        dst[off + 2] = ((value shr 16) and 0xFF).toByte()
        dst[off + 3] = ((value shr 24) and 0xFF).toByte()
    }
}
