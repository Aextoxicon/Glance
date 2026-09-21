package com.example.glance.Utils

import kotlin.test.Test
import kotlin.test.assertEquals

class PathUtilTest {

    @Test
    fun `windows path returns last segment`() {
        // Kotlin里写反斜杠转义写作
        assertEquals("Glance", PathUtil.fileName("C:\\Users\\Lwh20\\Documents\\GitHub\\Glance"))
    }

    @Test
    fun `unix path returns last segment`() {
        assertEquals("Glance", PathUtil.fileName("/Users/alice/dev/Glance"))
    }

    @Test
    fun `trailing separator is trimmed`() {
        assertEquals("src", PathUtil.fileName("/root/src/"))
        assertEquals("app", PathUtil.fileName("C:\\root\\app\\"))
    }

    @Test
    fun `root path is empty`() {
        assertEquals("", PathUtil.fileName("/"))
    }

    @Test
    fun `empty path is empty`() {
        assertEquals("", PathUtil.fileName(""))
    }

    @Test
    fun `single segment returns itself`() {
        assertEquals("data.txt", PathUtil.fileName("data.txt"))
        assertEquals("C:", PathUtil.fileName("C:"))
    }
}
