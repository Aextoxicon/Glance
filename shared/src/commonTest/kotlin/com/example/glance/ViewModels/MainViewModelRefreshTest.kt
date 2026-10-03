package com.example.glance.ViewModels

import com.example.glance.Utils.Result
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

@OptIn(ExperimentalCoroutinesApi::class)
class MainViewModelRefreshTest {

    // loadCore树构建-> size扫描-> 测试展开/刷新各自排队
    private fun TestScope.vmWith(): Pair<FakeRepo, MainViewModel> {
        val repo = FakeRepo()
        val vm = MainViewModel(
            AlwaysTextDetector(),
            repo,
            StandardTestDispatcher(testScheduler),
            StandardTestDispatcher(testScheduler),
        )
        return repo to vm
    }

    @Test
    fun `refresh reloads root listing and keeps existing items`() = runTest {
        val (repo, vm) = vmWith()
        repo.listGates["/root"] = queueGates(
            Result.success(listOf(fileArtifact("/root", "a.txt"), dirArtifact("/root", "dirX"))),
            Result.success(listOf(fileArtifact("/root", "a.txt"), dirArtifact("/root", "dirX"))),
            Result.success(
                listOf(
                    fileArtifact("/root", "a.txt"),
                    dirArtifact("/root", "dirX"),
                    fileArtifact("/root", "newFile.txt"),
                ),
            ),
        )
        repo.listGates["/root/dirX"] = queueGates(Result.success(emptyList()))

        vm.loadCore("/root")
        runCurrent()
        assertEquals(2, vm.treeItems.size)

        vm.refreshTree()
        runCurrent()

        assertEquals(3, vm.treeItems.size, "刷新后应出现新增文件")
        assertTrue(vm.treeItems.any { it.artifact.name == "newFile.txt" })
        assertTrue(vm.treeItems.any { it.artifact.name == "a.txt" })
    }

    @Test
    fun `refresh preserves expansion and reloads expanded subdir content`() = runTest {
        val (repo, vm) = vmWith()
        val sub = dirArtifact("/root", "sub")
        repo.listGates["/root"] = queueGates(
            Result.success(listOf(sub)),
            Result.success(listOf(sub)),
            Result.success(listOf(sub)),
        )
        repo.listGates["/root/sub"] = queueGates(
            Result.success(listOf(fileArtifact("/root/sub", "1.txt"))),
            Result.success(listOf(fileArtifact("/root/sub", "1.txt"))),
            Result.success(listOf(fileArtifact("/root/sub", "1.txt"), fileArtifact("/root/sub", "2.txt"))),
        )

        vm.loadCore("/root")
        runCurrent()

        val oldSub = vm.treeItems.first { it.artifact.name == "sub" }
        vm.selectItem(oldSub)
        runCurrent()
        assertEquals(listOf("1.txt"), vm.treeItems.first().children.map { it.artifact.name })

        vm.refreshTree()
        runCurrent()

        val newSub = vm.treeItems.first { it.artifact.name == "sub" }
        assertTrue(newSub.isExpanded, "刷新后展开状态应保留")
        assertEquals(
            listOf("1.txt", "2.txt"),
            newSub.children.map { it.artifact.name },
            "已展开子目录应被重读并见到新增文件",
        )
    }

    @Test
    fun `refresh reattaches selection to the new node instance`() = runTest {
        val (repo, vm) = vmWith()
        val sub = dirArtifact("/root", "sub")
        repo.listGates["/root"] = queueGates(
            Result.success(listOf(sub)),
            Result.success(listOf(sub)),
            Result.success(listOf(sub)),
        )
        repo.listGates["/root/sub"] = queueGates(
            Result.success(listOf(fileArtifact("/root/sub", "1.txt"))),
            Result.success(listOf(fileArtifact("/root/sub", "1.txt"))),
            Result.success(listOf(fileArtifact("/root/sub", "1.txt"))),
        )

        vm.loadCore("/root")
        runCurrent()
        val oldSub = vm.treeItems.first { it.artifact.name == "sub" }
        vm.selectItem(oldSub)
        runCurrent()
        val oldFile = oldSub.children.first { it.artifact.name == "1.txt" }
        vm.selectItem(oldFile)
        runCurrent()
        assertEquals("1.txt", vm.selectedArtifact?.name)

        vm.refreshTree()
        runCurrent()

        val newFile = vm.treeItems.first()
            .children.first { it.artifact.name == "1.txt" }
        assertTrue(newFile.isSelected, "刷新重建后选中高亮应挂到新节点")
        assertTrue(vm.treeItems.first().isExpanded)
        assertEquals("/root/sub/1.txt", vm.selectedArtifact?.id)
    }

    @Test
    fun `currentExpandedDirPaths collects expanded dirs only`() = runTest {
        val (repo, vm) = vmWith()
        val sub = dirArtifact("/root", "sub")
        val other = dirArtifact("/root", "other")
        repo.listGates["/root"] = queueGates(
            Result.success(listOf(sub, other)),
            Result.success(listOf(sub, other)),
        )
        repo.listGates["/root/sub"] = queueGates(Result.success(emptyList()))
        repo.listGates["/root/other"] = queueGates(Result.success(emptyList()))

        vm.loadCore("/root")
        runCurrent()
        assertTrue(vm.currentExpandedDirPaths().isEmpty(), "未展开时不应监听任何目录")

        vm.selectItem(vm.treeItems.first { it.artifact.name == "sub" })
        runCurrent()
        assertEquals(listOf("/root/sub"), vm.currentExpandedDirPaths(), "只应包含已展开目录")
    }
}