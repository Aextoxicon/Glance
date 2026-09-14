package com.example.glance.ViewModels

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Processing.CodeParseResult
import com.example.glance.Processing.FileProcessor
import com.example.glance.Repositories.IArtifactRepo
import com.example.glance.Repositories.TextFileDetector
import com.example.glance.Utils.FormatSize
import com.example.glance.Utils.PathUtil
import kotlinx.coroutines.*

class MainViewModel(
    private val fs: TextFileDetector,
    private val repo: IArtifactRepo,
    private val dispatcher: CoroutineDispatcher = Dispatchers.Main,
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.Default,
) {
    companion object {
        private const val WIDE_MODE_THRESHOLD = 640
    }

    private val scope = CoroutineScope(SupervisorJob() + dispatcher)
    private val childrenCache = mutableMapOf<String, List<IArtifact>>()

    private var loadJob: Job? = null
    private var selectJob: Job? = null
    private var sizeJob: Job? = null

    // 文件浏览状态
    var currentPath by mutableStateOf("")
        private set

    var totalSize by mutableStateOf(0L)
        private set

    var isComputingSize by mutableStateOf(false)
        private set

    var selectedArtifact by mutableStateOf<IArtifact?>(null)
        private set

    var selectedContent by mutableStateOf<String?>(null)
        private set

    var messageText by mutableStateOf<String?>(null)
        private set

    var selectedParseResult by mutableStateOf<CodeParseResult?>(null)
        private set

    var treeItems by mutableStateOf<List<TreeItemViewModel>>(emptyList())
        private set

    var hasWorkspace by mutableStateOf(false)
        private set

    var hasSelection by mutableStateOf(false)
        private set

    var isDrawerOpen by mutableStateOf(false)
        private set

    var isWide by mutableStateOf(true)
        private set

    val totalSizeReadable: String get() = FormatSize.readable(totalSize)
    val selectedSizeDisplay: String get() = selectedArtifact?.let { FormatSize.readable(it.size) } ?: ""
    val isCodePreviewVisible: Boolean get() = hasSelection && messageText == null
    val currentFolderName: String
        get() = if (currentPath.isEmpty()) "" else PathUtil.fileName(currentPath)

    // 平台相关的文件选择器注入
    var pickFolderAction: (suspend () -> String?)? = null

    fun pickFolder() {
        scope.launch {
            val path = pickFolderAction?.invoke()
            if (path != null) {
                loadCore(path)
            }
        }
    }

    fun closeWorkspace() {
        // 取消所有在途任务，避免旧状态被异步结果改写
        loadJob?.cancel()
        selectJob?.cancel()
        sizeJob?.cancel()
        isComputingSize = false
        currentPath = ""
        hasWorkspace = false
        childrenCache.clear()
        treeItems = emptyList()
        totalSize = 0
        selectedArtifact = null
        selectedContent = null
        selectedParseResult = null
        hasSelection = false
        messageText = null
    }

    fun selectItem(item: TreeItemViewModel?) {
        if (item == null) return
        if (item.isDir) {
            item.toggleExpanded()
        } else {
            selectFile(item.artifact)
        }
    }

    fun expandAll() {
        for (root in treeItems) {
            root.expandAllRecursive()
        }
    }

    fun collapseAll() {
        for (root in treeItems) {
            root.collapseRecursive()
        }
    }

    fun clearSelection() {
        selectedArtifact = null
        hasSelection = false
        selectedContent = null
        selectedParseResult = null
        messageText = null
    }

    fun toggleDrawer() {
        isDrawerOpen = !isDrawerOpen
    }

    fun onWindowResized(width: Double) {
        isWide = width > WIDE_MODE_THRESHOLD
        if (isWide) {
            isDrawerOpen = false
        }
    }

    fun loadCore(path: String) {
        // 切换目录时取消上一轮的目录加载与大小计算
        loadJob?.cancel()
        sizeJob?.cancel()
        loadJob = scope.launch {
            currentPath = path
            hasWorkspace = true
            childrenCache.clear()
            totalSize = 0
            treeItems = emptyList()

            try {
                // 文件系统读取放到 io 线程，状态写入保持在 dispatcher（主线程）
                val listResult = withContext(ioDispatcher) { repo.listAsync(path) }
                val items = listResult.getOrNull() ?: emptyList()
                if (!isActive) return@launch
                treeItems = items.map { TreeItemViewModel(it, repo, childrenCache, dispatcher, ioDispatcher) }
            } catch (ex: Exception) {
                if (isActive) messageText = "加载失败: ${ex.message}"
            }

            // 异步计算总大小
            isComputingSize = true
            val sizePath = path
            sizeJob = scope.launch {
                val size = computeTotalSize(sizePath)
                // 只有路径未变时才写回，避免残留在途任务污染新会话
                if (isActive && currentPath == sizePath) {
                    totalSize = size
                    isComputingSize = false
                }
            }
        }
    }

    private data class LoadedFile(val parseResult: CodeParseResult?, val content: String)

    private fun selectFile(artifact: IArtifact) {
        selectJob?.cancel() // 快速连续点击时，丢弃前一次尚未完成的读取
        selectJob = scope.launch {
            messageText = null
            selectedContent = null
            selectedParseResult = null
            selectedArtifact = artifact
            hasSelection = true

            val loaded = loadFile(artifact) ?: return@launch
            // 期间若用户已切换选择或关闭工作区，丢弃本次结果
            if (!isActive) return@launch
            selectedParseResult = loaded.parseResult
            selectedContent = loaded.content
        }
    }

    private suspend fun loadFile(artifact: IArtifact): LoadedFile? {
        // 检查是否是目录
        if (artifact.payload is LocalPayload && (artifact.payload as LocalPayload).isDir) {
            return null
        }

        val path = artifact.id
        if (path.isEmpty()) {
            messageText = "无法读取文件: ${artifact.name}"
            return null
        }

        if (!fs.isTextFile(path)) {
            messageText = "[二进制文件] ${artifact.name} 无法预览"
            return null
        }

        // 读取文件内容（阻塞 I/O 放 io 线程）
        val contentResult = withContext(ioDispatcher) { repo.tryReadTextAsync(path) }
        val content = contentResult.getOrNull()
        if (content == null) {
            messageText = "无法读取文件: ${artifact.name}"
            return null
        }

        val normalizedContent = content.replace("\t", "    ")
        val ext = artifact.extension
        val filename = artifact.name

        // 解析代码
        val parseResult = try {
            FileProcessor.process(normalizedContent, ext, filename)
        } catch (ex: Exception) {
            println("Code parsing failed for ${artifact.name}: ${ex.message}")
            null
        }

        return LoadedFile(parseResult, normalizedContent)
    }

    private suspend fun computeTotalSize(path: String): Long {
        return withContext(ioDispatcher) {
            try {
                val items = repo.listAsync(path)
                val artifacts = items.getOrNull() ?: return@withContext 0L

                val deferredResults = artifacts.map { item ->
                    async {
                        if (item.payload is LocalPayload && (item.payload as LocalPayload).isDir) {
                            val payload = item.payload as LocalPayload
                            computeTotalSize(payload.absolutePath)
                        } else {
                            item.size
                        }
                    }
                }
                deferredResults.sumOf { it.await() }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                println("computeTotalSize error for path: $path, message: ${e.message}")
                0L
            }
        }
    }

    fun dispose() {
        scope.cancel()
    }
}