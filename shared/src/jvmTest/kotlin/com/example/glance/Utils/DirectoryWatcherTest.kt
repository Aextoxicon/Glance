package com.example.glance.Utils

import kotlin.test.Test
import kotlin.test.assertTrue
import java.nio.file.Files
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class DirectoryWatcherTest {

    @Test
    fun `create event in watched root triggers debounced callback`() = runBlocking {
        val dir = Files.createTempDirectory("glance-watch-create-")
        val invocations = AtomicInteger(0)
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        val watcher = DirectoryWatcher(scope, debounceMs = 50L) { invocations.incrementAndGet() }
        try {
            watcher.syncRoot(dir.toString(), emptyList())

            Files.writeString(dir.resolve("a.txt"), "hello")

            withTimeout(5000L) {
                while (invocations.get() == 0) {
                    delay(10)
                }
            }
            assertTrue(invocations.get() >= 1, "新建文件应触发一次去抖回调")
        } finally {
            watcher.close()
            scope.cancel()
            dir.toFile().deleteRecursively()
        }
    }

    @Test
    fun `modify event in expanded subdir triggers callback`() = runBlocking {
        val root = Files.createTempDirectory("glance-watch-sub-")
        val sub = root.resolve("sub")
        Files.createDirectories(sub)
        val invocations = AtomicInteger(0)
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        val watcher = DirectoryWatcher(scope, debounceMs = 50L) { invocations.incrementAndGet() }
        try {
            watcher.syncRoot(root.toString(), listOf(sub.toString()))

            Files.writeString(sub.resolve("b.txt"), "v1")

            withTimeout(5000L) {
                while (invocations.get() == 0) {
                    delay(10)
                }
            }
            assertTrue(invocations.get() >= 1, "已注册子目录内的修改应触发回调")
        } finally {
            watcher.close()
            scope.cancel()
            root.toFile().deleteRecursively()
        }
    }
}