package com.example.glance.ViewModels

import com.example.glance.Models.ArtifactKind
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Repositories.IArtifactRepo
import com.example.glance.Repositories.TextFileDetector
import com.example.glance.Utils.Result
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

private class AlwaysTextDetector : TextFileDetector {
    override fun isTextFile(path: String): Boolean = true
}

private class FakeRepo : IArtifactRepo {
    val listGates: MutableMap<String, ArrayDeque<CompletableDeferred<Result<List<IArtifact>>>>> = mutableMapOf()
    val readGates: MutableMap<String, CompletableDeferred<Result<String>>> = mutableMapOf()

    override suspend fun listAsync(path: String): Result<List<IArtifact>> {
        val gates = listGates[path]
        if (gates != null && gates.isNotEmpty()) {
            return gates.removeFirst().await()
        }
        return Result.success(emptyList())
    }

    override suspend fun searchAsync(query: String): Result<List<IArtifact>> = Result.success(emptyList())

    override suspend fun getAsync(id: String): Result<IArtifact> =
        Result.failure(Exception("unexpected getAsync($id)"))

    override suspend fun deleteAsync(id: String): Result<Boolean> = Result.success(true)

    override suspend fun getContUriAsync(id: String): Result<String> =
        Result.failure(Exception("unexpected getContUriAsync($id)"))

    override suspend fun tryReadTextAsync(id: String): Result<String> {
        val gate = readGates[id]
        if (gate != null) return gate.await()
        return Result.success("content of $id")
    }
}

private fun fileArtifact(parent: String, name: String, size: Long = 10L): LocalArtifact =
    LocalArtifact(
        id = "$parent/$name",
        name = name,
        size = size,
        lastMod = 0,
        extension = "txt",
        kind = ArtifactKind.Text,
        local = LocalPayload("$parent/$name", parent, isDir = false),
    )

private fun dirArtifact(parent: String, name: String): LocalArtifact =
    LocalArtifact(
        id = "$parent/$name",
        name = name,
        size = 0,
        lastMod = 0,
        extension = "",
        kind = ArtifactKind.Text,
        local = LocalPayload("$parent/$name", parent, isDir = true),
    )

private fun queueGates(vararg results: Result<List<IArtifact>>): ArrayDeque<CompletableDeferred<Result<List<IArtifact>>>> {
    return ArrayDeque(results.map { result ->
        CompletableDeferred<Result<List<IArtifact>>>().also { it.complete(result) }
    })
}

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class MainViewModelRaceTest {

    @Test
    fun `rapid click waits for the latest selection only`() = runTest {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )
        val slowGate = CompletableDeferred<Result<String>>()
        repo.readGates["/root/slow.txt"] = slowGate

        val slow = TreeItemViewModel(fileArtifact("/root", "slow.txt"))
        val fast = TreeItemViewModel(fileArtifact("/root", "fast.txt"))

        // 先选中slow：任务开始读取并在gate上挂起
        vm.selectItem(slow)
        runCurrent()
        assertEquals("slow.txt", vm.selectedArtifact?.name)

        // 再选中fast：slow的读取任务应被取消
        vm.selectItem(fast)
        runCurrent()

        assertEquals("fast.txt", vm.selectedArtifact?.name)
        assertEquals("content of /root/fast.txt", vm.selectedContent)

        // 迟到的读取结果不得覆盖当前选择
        slowGate.complete(Result.success("late content"))
        runCurrent()

        assertEquals("fast.txt", vm.selectedArtifact?.name)
        assertEquals("content of /root/fast.txt", vm.selectedContent)
    }

    @Test
    fun `size job does not write back after workspace is closed`() = runTest {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )

        val sub = dirArtifact("/root", "sub")
        repo.listGates["/root"] = queueGates(Result.success(listOf(sub)), Result.success(listOf(sub)))
        val subGate = CompletableDeferred<Result<List<IArtifact>>>()
        repo.listGates["/root/sub"] = ArrayDeque(listOf(subGate))

        vm.loadCore("/root")
        runCurrent()
        assertTrue(vm.isComputingSize)

        vm.closeWorkspace()
        assertEquals(0L, vm.totalSize)
        assertFalse(vm.isComputingSize)

        // 迟到的子树计算结果不得复活为totalSize
        subGate.complete(Result.success(emptyList()))
        runCurrent()
        assertEquals(0L, vm.totalSize)
        assertFalse(vm.isComputingSize)
    }

    @Test
    fun `switch folder keeps size from the latest folder only`() = runTest {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )

        val subA = dirArtifact("/a", "sub")
        repo.listGates["/a"] = queueGates(Result.success(listOf(subA)), Result.success(listOf(subA)))
        val subGateA = CompletableDeferred<Result<List<IArtifact>>>()
        repo.listGates["/a/sub"] = ArrayDeque(listOf(subGateA))

        val fileB = fileArtifact("/b", "b.txt", size = 100L)
        repo.listGates["/b"] = queueGates(Result.success(listOf(fileB)), Result.success(listOf(fileB)))

        vm.loadCore("/a")
        runCurrent()
        assertTrue(vm.isComputingSize)

        vm.loadCore("/b")
        runCurrent()

        assertEquals("/b", vm.currentPath)
        assertEquals(100L, vm.totalSize)
        assertFalse(vm.isComputingSize)

        // 旧文件夹 /a的子树计算结果不得覆盖/b的大小
        subGateA.complete(Result.success(emptyList()))
        runCurrent()
        assertEquals("/b", vm.currentPath)
        assertEquals(100L, vm.totalSize)
    }

    @Test
    fun `repeated select of the same file reuses the cached parse result`() = runTest {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )

        val file = TreeItemViewModel(fileArtifact("/root", "same.txt"))
        vm.selectItem(file)
        runCurrent()

        val firstParse = vm.selectedParseResult
        assertNotNull(firstParse)
        assertEquals("content of /root/same.txt", vm.selectedContent)

        vm.selectItem(file)
        runCurrent()

        assertEquals("content of /root/same.txt", vm.selectedContent)
        assertTrue(vm.selectedParseResult === firstParse, "same file should reuse the cached parse result")

        // 不同文件不得复用
        vm.selectItem(TreeItemViewModel(fileArtifact("/root", "other.txt")))
        runCurrent()
        assertTrue(vm.selectedParseResult !== firstParse, "a different file must not reuse the cache entry")
    }

    @Test
    fun `switching workspace clears the parse cache`() = runTest {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )

        val file = TreeItemViewModel(fileArtifact("/a", "same.txt"))
        repo.listGates["/a"] = queueGates(Result.success(listOf(file.artifact)))

        vm.loadCore("/a")
        runCurrent()
        vm.selectItem(file)
        runCurrent()
        val firstParse = vm.selectedParseResult
        assertNotNull(firstParse)

        // 关闭并重开工作区后，旧缓存应被丢弃，同文件需重新解析
        vm.closeWorkspace()
        vm.loadCore("/a")
        runCurrent()
        vm.selectItem(file)
        runCurrent()

        assertTrue(vm.selectedParseResult !== firstParse, "cache should be cleared when switching workspace")
        assertEquals("content of /a/same.txt", vm.selectedContent)
    }
}