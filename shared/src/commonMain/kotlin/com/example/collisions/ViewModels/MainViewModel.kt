package com.example.glance.ViewModels

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.text.AnnotatedString
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Processing.CodeParseResult
import com.example.glance.Processing.FileProcessor
import com.example.glance.Repositories.IArtifactRepo
import com.example.glance.Repositories.TextFileDetector
import com.example.glance.Utils.FormatSize
import com.example.glance.Utils.HighlightColor
import com.example.glance.Utils.PathUtil
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class MainViewModel(
    private val fs: TextFileDetector,
    private val repo: IArtifactRepo,
    private val dispatcher: CoroutineDispatcher = Dispatchers.Main,
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.Default,
) {
    companion object {
        private const val WIDE_MODE_THRESHOLD = 640
        private const val PARSE_CACHE_MAX = 32
    }

    private val scope = CoroutineScope(SupervisorJob() + dispatcher)
    private val childrenCache = mutableMapOf<String, List<IArtifact>>()

    // 解析结果+高亮文本缓存，key = id|lastMod|size
    private val parseCache = mutableMapOf<String, CachedPreview>()
    private val parseCacheMutex = Mutex()

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

    var selectedAnnotatedLines by mutableStateOf<List<AnnotatedString>?>(null)
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
        selectedAnnotatedLines = null
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
        selectedAnnotatedLines = null
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
            // 切换工作区时丢旧目录的解析缓存
            parseCacheMutex.withLock { parseCache.clear() }
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

    private data class CachedPreview(
        val parseResult: CodeParseResult?,
        val annotatedLines: List<AnnotatedString>,
        val content: String,
    )

    private data class LoadedFile(
        val parseResult: CodeParseResult?,
        val annotatedLines: List<AnnotatedString>,
        val content: String,
    )

    private fun cacheKey(artifact: IArtifact): String =
        "${artifact.id}|${artifact.lastMod}|${artifact.size}"

    private fun selectFile(artifact: IArtifact) {
        selectJob?.cancel() // 快速连续点击时，丢前一次尚未完成的读取
        selectJob = scope.launch {
            messageText = null
            selectedContent = null
            selectedParseResult = null
            selectedAnnotatedLines = null
            selectedArtifact = artifact
            hasSelection = true

            val loaded = loadFile(artifact) ?: return@launch
            // 期间若用户已切换选择或关闭工作区，丢弃本次结果
            if (!isActive) return@launch
            selectedParseResult = loaded.parseResult
            selectedAnnotatedLines = loaded.annotatedLines
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

        // 重活统一放io
        // io 块内不写 Compose 状态，错误信息经返回值带回主线程再写 messageText
        val loaded = withContext(ioDispatcher) {
            val outcome: Pair<LoadedFile?, String?> = if (!fs.isTextFile(path)) {
                null to "[二进制文件] ${artifact.name} 无法预览"
            } else {
                val cached = parseCacheMutex.withLock { parseCache[cacheKey(artifact)] }
                if (cached != null) {
                    LoadedFile(cached.parseResult, cached.annotatedLines, cached.content) to null
                } else {
                    val contentResult = repo.tryReadTextAsync(path)
                    val content = contentResult.getOrNull()
                    if (content == null) {
                        null to "无法读取文件: ${artifact.name}"
                    } else {
                        val normalizedContent = content.replace("\t", "    ")
                        val parseResult = try {
                            FileProcessor.process(normalizedContent, artifact.extension, artifact.name)
                        } catch (ex: Exception) {
                            println("Code parsing failed for ${artifact.name}: ${ex.message}")
                            null
                        }
                        val annotatedLines = if (parseResult is CodeParseResult.Code) {
                            HighlightColor.toAnnotatedLines(parseResult)
                        } else {
                            normalizedContent.split("\n").map { AnnotatedString(it) }
                        }
                        parseCacheMutex.withLock {
                            if (parseCache.size >= PARSE_CACHE_MAX) parseCache.clear()
                            parseCache[cacheKey(artifact)] = CachedPreview(parseResult, annotatedLines, normalizedContent)
                        }
                        LoadedFile(parseResult, annotatedLines, normalizedContent) to null
                    }
                }
            }
            outcome
        }

        val (file, error) = loaded
        if (error != null) {
            messageText = error
            return null
        }
        return file
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