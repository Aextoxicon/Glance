package com.example.glance.Utils

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.AnnotatedString
import com.example.glance.Processing.HighlightToken
import kotlin.test.Test
import kotlin.test.assertEquals

// 渲染层单测：golden只钉token列表，钉不住「token怎么拼成一行带色文本」
class HighlightColorTest {

    private fun AnnotatedString.colorPerChar(): List<Color> {
        val len = text.length
        val out = MutableList(len) { Color.Unspecified }
        for (range in spanStyles) {
            for (i in range.start until range.end.coerceAtMost(len)) {
                out[i] = range.item.color
            }
        }
        return out
    }

    private fun render(line: String, vararg tokens: HighlightToken): List<Color> =
        HighlightColor.buildLineAnnotatedString(line, tokens.toList()).colorPerChar()

    private fun text(line: String, vararg tokens: HighlightToken): String =
        HighlightColor.buildLineAnnotatedString(line, tokens.toList()).text

    private fun tok(start: Int, end: Int, kind: String) = HighlightToken(start, end, kind)

    private val plain = HighlightColor.plainText

    @Test
    fun `no token leaves the whole line in default color`() {
        assertEquals(listOf(plain, plain, plain), render("abc"))
    }

    @Test
    fun `single token covering the whole line colors every char`() {
        val kw = HighlightColor.keyword
        assertEquals(listOf(kw, kw, kw), render("abc", tok(0, 3, "keyword")))
    }

    @Test
    fun `gaps before and after token keep default color`() {
        val kw = HighlightColor.keyword
        assertEquals(
            listOf(plain, kw, kw, plain, plain),
            render("abcde", tok(1, 3, "keyword")),
        )
    }

    @Test
    fun `container token followed by inner tokens must not duplicate text`() {
        // native曾产出 (0-5:property) 与 (0-1:type) 嵌套，旧渲染把a = 1又画一遍 → "aa = 1"
        assertEquals(5, text("a = 1", tok(0, 5, "property"), tok(0, 1, "type")).length)
        assertEquals("a = 1", text("a = 1", tok(0, 5, "property"), tok(0, 1, "type")))
    }

    @Test
    fun `already split tokens render each segment exactly once`() {
        val line = "a = 1"
        val result = HighlightColor.buildLineAnnotatedString(
            line,
            listOf(
                tok(0, 1, "type"),
                tok(1, 2, "property"),
                tok(2, 3, "operator"),
                tok(3, 4, "property"),
                tok(4, 5, "number"),
            ),
        )
        assertEquals(line, result.text)
        assertEquals(
            listOf(
                HighlightColor.type,
                HighlightColor.property,
                HighlightColor.operator,
                HighlightColor.property,
                HighlightColor.number,
            ),
            result.colorPerChar(),
        )
    }

    @Test
    fun `token starting before rendered position is clipped`() {
        val kw = HighlightColor.keyword
        val str = HighlightColor.string
        assertEquals(
            listOf(kw, kw, kw, str, str),
            render("abcde", tok(0, 3, "keyword"), tok(1, 5, "string")),
        )
    }

    @Test
    fun `token fully inside rendered range is skipped`() {
        val kw = HighlightColor.keyword
        assertEquals(
            listOf(kw, kw, kw),
            render("abc", tok(0, 3, "keyword"), tok(1, 2, "string")),
        )
    }

    @Test
    fun `token past end of line is clamped`() {
        val kw = HighlightColor.keyword
        assertEquals(listOf(kw, kw), render("ab", tok(0, 10, "keyword")))
        assertEquals("ab", text("ab", tok(0, 10, "keyword")))
    }

    @Test
    fun `zero width token colors nothing`() {
        assertEquals(listOf(plain, plain, plain), render("abc", tok(2, 2, "keyword")))
    }

    @Test
    fun `unknown kind falls back to default color`() {
        assertEquals(listOf(plain, plain), render("ab", tok(0, 2, "totally.unknown.kind")))
    }

    @Test
    fun `dotted kind resolves by known prefix`() {
        assertEquals(
            listOf(HighlightColor.functionBuiltin, HighlightColor.functionBuiltin),
            render("ab", tok(0, 2, "function.builtin")),
        )
        assertEquals(
            listOf(HighlightColor.variable, HighlightColor.variable),
            render("ab", tok(0, 2, "variable.parameter")),
        )
    }

    @Test
    fun `custom default color applies to gaps`() {
        val custom = Color(0xFF00FF00)
        val result = HighlightColor.buildLineAnnotatedString(
            "ab",
            listOf(tok(1, 2, "keyword")),
            colorDefault = custom,
        )
        assertEquals(listOf(custom, HighlightColor.keyword), result.colorPerChar())
    }
}
