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
import com.example.glance.Utils.HighlightColor
import com.example.glance.Utils.PathUtil
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class MainViewModel(
    private val fs: TextFileDetector,
    private val repo: IArtifactRepo,
    private val dispatcher: CoroutineDispatcher = Dispatchers.Main,
    // 文件走IO池
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.IO,
) {
    companion object {
        private const val WIDE_MODE_THRESHOLD = 640
        private const val PARSE_CACHE_MAX = 32
        private const val PREVIEW_HARD_LIMIT_BYTES = 10L * 1024 * 1024
        private const val PREVIEW_PLAIN_LIMIT_BYTES = 1L * 1024 * 1024

        private const val SIZE_SCAN_CONCURRENCY = 8
        private const val EXPAND_CONCURRENCY = 8
    }

    private val scope = CoroutineScope(SupervisorJob() + dispatcher)

    // 解析+高亮缓存，key = id|lastMod|size；accessOrder=true满了只淘汰最久未访问的条目
    private val parseCache = LinkedHashMap<String, LoadedFile>(PARSE_CACHE_MAX, 0.75f, true)
    private val parseCacheMutex = Mutex()

    private var loadJob: Job? = null
    private var selectJob: Job? = null
    private var sizeJob: Job? = null
    // 每轮工作区的树加载句柄：closeWorkspace取消它可级联中止所有已展开的子目录加载，
    // 防止任务回写已清空的树状态
    private var treeOwnerJob: Job? = null
    // 选中行的item引用：isSelected下沉到行，切换选中只重组旧/新两行
    private var selectedTreeItem: TreeItemViewModel? = null

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

    var previewNotice by mutableStateOf<String?>(null)
        private set

    var selectedParseResult by mutableStateOf<CodeParseResult?>(null)
        private set

    var treeItems by mutableStateOf<List<TreeItemViewModel>>(emptyList())
        private set

    var hasWorkspace by mutableStateOf(false)
        private set

    var hasSelection by mutableStateOf(false)
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
        // 取消所有在途任务
        selectedTreeItem?.isSelected = false
        selectedTreeItem = null
        loadJob?.cancel()
        selectJob?.cancel()
        sizeJob?.cancel()
        treeOwnerJob?.cancel()
        isComputingSize = false
        currentPath = ""
        hasWorkspace = false
        treeItems = emptyList()
        totalSize = 0
        selectedArtifact = null
        selectedContent = null
        selectedParseResult = null
        hasSelection = false
        messageText = null
        previewNotice = null
    }

    fun selectItem(item: TreeItemViewModel?) {
        if (item == null) return
        if (item.isDir) {
            item.toggleExpanded()
        } else {
            selectedTreeItem?.isSelected = false
            item.isSelected = true
            selectedTreeItem = item
            selectFile(item.artifact)
        }
    }

    fun expandAll() {
        // 分批展开
        scope.launch {
            val queue = ArrayDeque<TreeItemViewModel>()
            for (root in treeItems) {
                if (root.isDir) queue.add(root)
            }
            while (queue.isNotEmpty()) {
                val batch = ArrayDeque<TreeItemViewModel>()
                while (batch.size < EXPAND_CONCURRENCY && queue.isNotEmpty()) {
                    batch.add(queue.removeFirst())
                }
                coroutineScope {
                    batch.map { item -> async { item.ensureLoaded() } }.awaitAll()
                    for (item in batch) {
                        for (child in item.children) {
                            if (!child.isPlaceholder) queue.add(child)
                        }
                    }
                }
            }
        }
    }

    fun collapseAll() {
        for (root in treeItems) {
            root.collapseRecursive()
        }
    }

    fun clearSelection() {
        selectedTreeItem?.isSelected = false
        selectedTreeItem = null
        selectedArtifact = null
        hasSelection = false
        selectedContent = null
        selectedParseResult = null
        messageText = null
        previewNotice = null
    }

    fun onWindowResized(width: Double) {
        // 抽屉状态归NarrowLayout的drawerState所有
        isWide = width > WIDE_MODE_THRESHOLD
    }

    fun loadCore(path: String) {
        // 切换目录时取消上一轮
        loadJob?.cancel()
        sizeJob?.cancel()
        selectJob?.cancel()
        treeOwnerJob?.cancel()
        selectedTreeItem?.isSelected = false
        selectedTreeItem = null
        treeOwnerJob = SupervisorJob()
        loadJob = scope.launch {
            currentPath = path
            hasWorkspace = true
            // 切换工作区时丢旧目录的解析缓存
            parseCacheMutex.withLock { parseCache.clear() }
            totalSize = 0
            treeItems = emptyList()

            try {
                // 文件系统读取放到io线程，状态写入保持在主线程
                val listResult = withContext(ioDispatcher) { repo.listAsync(path) }
                val items = listResult.getOrNull() ?: emptyList()
                if (!isActive) return@launch
                treeItems = filterIgnoredDirs(items).map { TreeItemViewModel(it, repo, dispatcher, ioDispatcher, owner = treeOwnerJob) }
            } catch (ex: Exception) {
                if (isActive) messageText = "加载失败: ${ex.message}"
            }

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

    private data class LoadedFile(
        val parseResult: CodeParseResult?,
        val content: String,
        // 预览被降级时给UI的提示
        val notice: String? = null,
    )

    private fun cacheKey(artifact: IArtifact): String =
        "${artifact.id}|${artifact.lastMod}|${artifact.size}"

    private fun selectFile(artifact: IArtifact) {
        selectJob?.cancel() // 快速连续点击时，丢前一次尚未完成的读取
        selectJob = scope.launch {
            messageText = null
            selectedContent = null
            selectedParseResult = null
            previewNotice = null
            selectedArtifact = artifact
            hasSelection = true

            val loaded = loadFile(artifact) ?: return@launch
            // 期间切换选择或关闭工作区，丢弃本次结果
            if (!isActive) return@launch
            selectedParseResult = loaded.parseResult
            selectedContent = loaded.content
            previewNotice = loaded.notice
        }
    }

    private suspend fun loadFile(artifact: IArtifact): LoadedFile? {
        if (artifact.payload is LocalPayload && (artifact.payload as LocalPayload).isDir) {
            return null
        }

        val path = artifact.id
        if (path.isEmpty()) {
            messageText = "无法读取文件: ${artifact.name}"
            return null
        }

        // 直接放弃预览
        if (artifact.size > PREVIEW_HARD_LIMIT_BYTES) {
            messageText = "文件过大（${FormatSize.readable(artifact.size)}），已跳过预览"
            return null
        }

        // io块内不写Compose状态，错误信息经返回值带回主线程再写messageText
        val loaded = withContext(ioDispatcher) {
            if (!fs.isTextFile(path)) {
                null to "[二进制文件] ${artifact.name} 无法预览"
            } else {
                val cacheKey = cacheKey(artifact)
                val cached = parseCacheMutex.withLock { parseCache[cacheKey] }
                if (cached != null) {
                    cached to null
                } else {
                    val contentResult = repo.tryReadTextAsync(path)
                    val content = contentResult.getOrNull()
                    if (content == null) {
                        null to "无法读取文件: ${artifact.name}"
                    } else {
                        val built = buildPreview(content.replace("\t", "    "), artifact)
                        if (artifact.size <= PREVIEW_PLAIN_LIMIT_BYTES) {
                            parseCacheMutex.withLock {
                                if (parseCache.size >= PARSE_CACHE_MAX) parseCache.remove(parseCache.keys.first())
                                parseCache[cacheKey] = built
                            }
                        }
                        built to null
                    }
                }
            }
        }

        val (file, error) = loaded
        if (error != null) {
            messageText = error
            return null
        }
        return file
    }

    private fun buildPreview(normalizedContent: String, artifact: IArtifact): LoadedFile {
        if (artifact.size > PREVIEW_PLAIN_LIMIT_BYTES) {
            return LoadedFile(
                parseResult = null,
                content = normalizedContent,
                notice = "文件较大（${FormatSize.readable(artifact.size)}），已跳过语法高亮",
            )
        }

        val parseResult = try {
            FileProcessor.process(normalizedContent, artifact.extension, artifact.name)
        } catch (ex: Exception) {
            println("Code parsing failed for ${artifact.name}: ${ex.message}")
            null
        }
        return LoadedFile(parseResult, normalizedContent)
    }

    private suspend fun computeTotalSize(path: String): Long {
        return withContext(ioDispatcher) {
            // 分批BFS，每批同时展开SIZE_SCAN_CONCURRENCY个目录，
            // 避免对几十万文件的仓库发起同数量级的并发系统调用
            var total = 0L
            val queue = ArrayDeque<String>()
            queue.add(path)
            while (queue.isNotEmpty()) {
                val batch = ArrayDeque<String>()
                while (batch.size < SIZE_SCAN_CONCURRENCY && queue.isNotEmpty()) {
                    batch.add(queue.removeFirst())
                }
                val deferreds = batch.map { dir ->
                    async<List<IArtifact>> {
                        repo.listAsync(dir).getOrNull() ?: emptyList()
                    }
                }
                for (deferred in deferreds) {
                    for (item in deferred.await()) {
                        val payload = item.payload as? LocalPayload
                        if (payload != null && payload.isDir) {
                            queue.add(payload.absolutePath)
                        } else {
                            total += item.size
                        }
                    }
                }
            }
            total
        }
    }

    fun dispose() {
        scope.cancel()
    }
}
