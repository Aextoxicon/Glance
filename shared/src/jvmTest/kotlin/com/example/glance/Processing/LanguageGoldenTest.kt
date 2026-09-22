package com.example.glance.Processing

import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.v2.runComposeUiTest
import com.example.glance.Repositories.TextFileDetector
import com.example.glance.ViewModels.MainViewModel
import com.example.glance.Views.MainView
import kotlin.test.Test
import kotlin.test.assertNotNull
import kotlin.test.fail
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestCoroutineScheduler

/**
 默认：只比对，绝不改动已入库的 golden（比对失败时提示更新命令）
 ./gradlew jvmTest --rerun -PupdateGoldens=true`：把新基线直接写回 commonTest/resources/golden/
 */
@OptIn(ExperimentalTestApi::class)
class LanguageGoldenTest {

    private class AlwaysTextDetector : TextFileDetector {
        override fun isTextFile(path: String): Boolean = true
    }

    companion object {
        private const val WORKSPACE_PATH = "/workspace"

        private val UPDATE_GOLDENS: Boolean =
            System.getProperty("updateGoldens")?.equals("true", ignoreCase = true) ?: false

        private val TEMPLATE_FILES = listOf(
            "hello.py",
            "main.go",
            "App.kt",
            "lib.rs",
            "index.js",
            "config.json",
            "README.md",
            "main.c",
            "main.cpp",
            "Program.cs",
            "Main.java",
            "style.css",
            "script.sh",
            "App.ts",
            "Component.tsx",
            "Dockerfile",
            "config.ini",
            "Makefile",
            "config.toml",
            "config.yaml",
            "index.html",
            "Model.swift",
        )
    }

    @Test
    fun `open workspace and parse each language template`() {
        val scheduler = TestCoroutineScheduler()

        runComposeUiTest(
            effectContext = StandardTestDispatcher(scheduler),
        ) {
            val templates = TEMPLATE_FILES.associateWith { fileName ->
                loadResource("templates/$fileName")
                    ?.replace("\r\n", "\n")
                    ?: error("Template not found in resources: templates/$fileName")
            }

            val repo = TemplateWorkspaceRepo(WORKSPACE_PATH, templates)
            val vm = MainViewModel(
                fs = AlwaysTextDetector(),
                repo = repo,
                dispatcher = StandardTestDispatcher(scheduler),
                ioDispatcher = StandardTestDispatcher(scheduler),
            )
            vm.pickFolderAction = { WORKSPACE_PATH }

            setContent {
                MaterialTheme {
                    MainView(vm)
                }
            }

            onNodeWithText("打开文件夹").assertIsDisplayed()

            onNodeWithText("打开文件夹").performClick()
            mainClock.advanceTimeBy(0, ignoreFrameDuration = true)

            waitUntil(conditionDescription = "workspace loaded", timeoutMillis = 10_000) { vm.hasWorkspace }
            waitUntil(conditionDescription = "tree items loaded", timeoutMillis = 10_000) { vm.treeItems.isNotEmpty() }

            for (fileName in TEMPLATE_FILES) {
                onAllNodesWithText(fileName).onFirst().assertIsDisplayed()
            }

            for (fileName in TEMPLATE_FILES) {
                onAllNodesWithText(fileName).onFirst().performClick()
                mainClock.advanceTimeBy(0, ignoreFrameDuration = true)

                waitUntil(conditionDescription = "parse result for $fileName", timeoutMillis = 10_000) {
                    vm.selectedParseResult != null
                }

                val result = vm.selectedParseResult
                assertNotNull(result, "Parse result should not be null for $fileName")

                val content = templates[fileName]!!
                val codeSnippet = content.lineSequence().first().take(20).trim()
                onAllNodesWithText(codeSnippet, substring = true).onFirst().assertIsDisplayed()

                val serialized = serializeParseResult(result)
                val goldenPath = "golden/$fileName.golden"
                val golden = loadResource(goldenPath)

                if (UPDATE_GOLDENS) {
                    // 只写有变化的
                    if (golden != serialized) {
                        saveGoldenToSourceTree(goldenPath, serialized)
                    }
                } else if (golden == null) {
                    saveResource(goldenPath, serialized)
                    println("=== Generated golden for $fileName ===")
                } else if (golden != serialized) {
                    fail(
                        "Golden mismatch for $fileName\n" +
                            "--- GOLDEN (first 300 chars) ---\n" +
                            golden.take(300) + "\n" +
                            "--- ACTUAL (first 300 chars) ---\n" +
                            serialized.take(300) + "\n" +
                            "--- 若上述变化是预期的，重跑：./gradlew jvmTest --rerun -PupdateGoldens=true ---\n"
                    )
                }
            }
        }
    }
}
