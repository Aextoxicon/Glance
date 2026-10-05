package com.example.glance.ViewModels

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class GitIgnoreFilterTest {

    @Test
    fun dotEntriesHiddenFilesAndDirs() {
        val items = listOf(
            dirArtifact("/", ".git"),
            fileArtifact("/", ".gitignore"),
            fileArtifact("/", ".env"),
            fileArtifact("/", "main.kt"),
            dirArtifact("/", "src"),
        )
        val kept = filterIgnoredDirs(items, GitIgnoreDirs(emptySet(), emptySet()))
        assertEquals(listOf("main.kt", "src"), kept.map { it.name })
    }

    @Test
    fun trailingSlashOnlyHidesDirNotFile() {
        // foo/ -> dirOnly：目录隐藏，同名文件保留
        val items = listOf(
            dirArtifact("/", "foo"),
            fileArtifact("/", "foo"),
        )
        val kept = filterIgnoredDirs(items, GitIgnoreDirs(dirOnly = setOf("foo"), dirOrFile = emptySet()))
        assertEquals(listOf("foo"), kept.map { it.name })
    }

    @Test
    fun bareNameHidesDirAndFile() {
        // foo -> dirOrFile：目录+同名文件都隐藏
        val items = listOf(
            dirArtifact("/", "dist"),
            fileArtifact("/", "dist"),
        )
        val kept = filterIgnoredDirs(items, GitIgnoreDirs(dirOnly = emptySet(), dirOrFile = setOf("dist")))
        assertEquals(emptyList(), kept)
    }

    @Test
    fun parseSeparatesTrailingSlashFromBareNames() {
        val content = """
            # comment
            node_modules/
            dist
            *.log
            !keep.txt
            /root-only
            a/b/c/
        """.trimIndent()
        val ignored = parseGitIgnoreNames(content)
        assertEquals(setOf("node_modules"), ignored.dirOnly)
        assertEquals(setOf("dist"), ignored.dirOrFile)
    }

    @Test
    fun parseHandlesCrlfAndEmptyLines() {
        val content = "node_modules/\r\ndist\r\n\r\n# x\r\ncoverage/\r\n"
        val ignored = parseGitIgnoreNames(content)
        assertEquals(setOf("node_modules", "coverage"), ignored.dirOnly)
        assertEquals(setOf("dist"), ignored.dirOrFile)
    }

    @Test
    fun dirOnlyAndDirOrFileInteract() {
        // coverage/在dirOnly，node_modules无斜杠在dirOrFile
        val items = listOf(
            dirArtifact("/", "coverage"),
            fileArtifact("/", "coverage"),
            dirArtifact("/", "node_modules"),
            fileArtifact("/", "node_modules"),
            fileArtifact("/", "main.kt"),
        )
        val kept = filterIgnoredDirs(
            items,
            GitIgnoreDirs(dirOnly = setOf("coverage"), dirOrFile = setOf("node_modules")),
        )
        assertEquals(listOf("coverage", "main.kt"), kept.map { it.name })
    }

    @Test
    fun gitIgnoreNameMatchIsExactName() {
        val ignored = parseGitIgnoreNames("node_modules/\nnm2/\n")
        assertTrue("node_modules" in ignored.dirOnly)
        assertTrue("nm2" in ignored.dirOnly)
        assertEquals(2, ignored.dirOnly.size)
    }
}
