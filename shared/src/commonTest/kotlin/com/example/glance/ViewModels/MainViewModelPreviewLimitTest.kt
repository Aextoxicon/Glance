package com.example.glance.ViewModels

import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private const val MB = 1024L * 1024L
private const val HARD_LIMIT = 10L * MB
private const val PLAIN_LIMIT = 2L * MB

@OptIn(ExperimentalCoroutinesApi::class)
class MainViewModelPreviewLimitTest {

    private fun TestScope.selected(): Pair<FakeRepo, MainViewModel> {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )
        return repo to vm
    }

    private fun TestScope.selectOnce(vm: MainViewModel, size: Long, name: String = "big.txt"): TreeItemViewModel {
        val item = TreeItemViewModel(fileArtifact("/root", name, size = size))
        vm.selectItem(item)
        runCurrent()
        return item
    }

    @Test
    fun `file above the hard limit is never read`() = runTest {
        val (repo, vm) = selected()

        selectOnce(vm, HARD_LIMIT + 1)

        assertNotNull(vm.selectedArtifact, "选中状态仍应更新")
        assertNull(vm.selectedContent, "超限文件不得载入内容")
        assertNull(vm.selectedParseResult)
        assertTrue(vm.messageText?.contains("已跳过预览") == true, "应给出跳过预览提示: ${vm.messageText}")
        assertEquals(0, repo.readCalls["/root/big.txt"] ?: 0, "超限文件不应触发读取")
    }

    @Test
    fun `file exactly at the hard limit is previewed`() = runTest {
        val (repo, vm) = selected()

        selectOnce(vm, HARD_LIMIT)

        assertNull(vm.messageText, "恰好等于阈值不应触发跳过")
        assertNotNull(vm.selectedContent)
        assertEquals(1, repo.readCalls["/root/big.txt"] ?: 0)
    }

    @Test
    fun `file at the plain limit is highlighted and cached`() = runTest {
        val (repo, vm) = selected()

        selectOnce(vm, PLAIN_LIMIT)
        selectOnce(vm, PLAIN_LIMIT)

        assertNull(vm.previewNotice, "恰好等于阈值不应降级")
        assertNotNull(vm.selectedParseResult, "未降级的文件应有解析结果")
        assertEquals(1, repo.readCalls["/root/big.txt"] ?: 0, "恰好等于阈值也应进缓存")
    }

    @Test
    fun `file just above the plain limit drops highlighting but keeps content`() = runTest {
        val (_, vm) = selected()

        selectOnce(vm, PLAIN_LIMIT + 1)

        assertNotNull(vm.selectedContent, "降级只丢高亮，内容仍可读")
        assertNull(vm.selectedParseResult, "超过纯文本阈值不应解析")
        assertTrue(vm.previewNotice?.contains("已跳过语法高亮") == true, "应给出降级提示: ${vm.previewNotice}")
    }

    @Test
    fun `file inside the highlighted band reuses the cache on reselect`() = runTest {
        val (repo, vm) = selected()

        selectOnce(vm, 100L, name = "small.txt")
        selectOnce(vm, 100L, name = "small.txt")

        assertEquals(1, repo.readCalls["/root/small.txt"] ?: 0, "命中缓存时不得重复读取")
    }

    @Test
    fun `file above the plain limit is not cached so reselect re-reads it`() = runTest {
        val (repo, vm) = selected()

        selectOnce(vm, 3 * MB, name = "mid.txt")
        selectOnce(vm, 3 * MB, name = "mid.txt")

        assertEquals(2, repo.readCalls["/root/mid.txt"] ?: 0, "2MB-10MB 区间不进缓存，重选应重新读取")
    }

    @Test
    fun `parse cache evicts the least recently used entry`() = runTest {
        val (repo, vm) = selected()

        repeat(33) { i ->
            selectOnce(vm, 10L, name = "f$i.txt")
        }

        selectOnce(vm, 10L, name = "f0.txt")

        assertEquals(2, repo.readCalls["/root/f0.txt"] ?: 0, "最久未访问的首个文件应已被逐出")
        assertEquals(1, repo.readCalls["/root/f32.txt"] ?: 0, "最近访问的文件应仍在缓存中")
    }
}
