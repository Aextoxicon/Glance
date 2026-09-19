package com.example.glance.ViewModels

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.InsertDriveFile
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Code
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Terminal
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.runtime.toMutableStateList
import androidx.compose.ui.graphics.vector.ImageVector
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Repositories.IArtifactRepo
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class TreeItemViewModel(
    val artifact: IArtifact,
    private val repo: IArtifactRepo? = null,
    // 状态线程：所有Compose状态写入统一在这里
    private val uiDispatcher: CoroutineDispatcher? = null,
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.IO,
    owner: Job? = null,
) {
    // owner非空时作为SupervisorJob的parent：父Job取消时级联中止所有子目录加载，
    // 避免closeWorkspace清空树后仍有任务回写造成泄漏。
    // SupervisorJob保证兄弟任务之间互不影响。
    private val scope = CoroutineScope(
        SupervisorJob(owner) + (uiDispatcher ?: Dispatchers.Default)
    )

    val isDir: Boolean = artifact.payload is LocalPayload && (artifact.payload as LocalPayload).isDir

    var isExpanded by mutableStateOf(false)
        private set

    var isLoading by mutableStateOf(false)
        private set

    val children: SnapshotStateList<TreeItemViewModel> = mutableStateListOf()

    // 占位符标记：目录节点初始包含一个占位符，保证展开箭头显示
    var isPlaceholder: Boolean = false
        private set

    var isSelected by mutableStateOf(false)
    val icon: ImageVector = fileIconFor(artifact.extension)

    init {
        if (isDir) {
            children.add(TreeItemViewModel()) // 占位符
        }
    }

    private constructor() : this(
        artifact = PlaceholderArtifact(),
        repo = null,
    ) {
        isPlaceholder = true
    }

    // 目录项数：loadChildren完成后一次性写入，避免sizeDisplay读children把每行订阅到children的每次变更
    var childCount by mutableStateOf(0)
        private set

    val sizeDisplay: String
        get() {
            if (isDir) {
                if (childCount == 0) return ""
                return "$childCount 项"
            }
            return com.example.glance.Utils.FormatSize.readable(artifact.size)
        }

    fun toggleExpanded() {
        if (!isDir) return
        if (!isExpanded) {
            expand()
        } else {
            collapse()
        }
    }

    fun expand() {
        if (!isDir) return
        isExpanded = true
        // 仅在展开时 + 尚未加载子节点时加载
        if (children.isNotEmpty() && children[0].isPlaceholder) {
            scope.launch {
                loadChildren()
            }
        }
    }

    fun collapse() {
        isExpanded = false
    }

    suspend fun loadChildren() {
        if (!isDir) return
        val r = repo ?: return
        isLoading = true
        try {
            val payload = artifact.payload as LocalPayload
            // 放io线程，children的写入留在ui线程；不再缓存目录列表（与TreeItemViewModel持有的artifact重复，且单工作区内永不失效会堆涨）
            val items = withContext(ioDispatcher) {
                val listResult = r.listAsync(payload.absolutePath)
                listResult.getOrNull() ?: emptyList()
            }
            val sorted = filterIgnoredDirs(items)
                .sortedWith(compareBy({ !((it.payload as? LocalPayload)?.isDir ?: false) }, { it.name.lowercase() }))
            childCount = sorted.count()
            children.clear()
            for (child in sorted) {
                children.add(TreeItemViewModel(child, r, uiDispatcher, ioDispatcher, owner = scope.coroutineContext.get(Job)))
            }
            // 空目录：保留占位符，保证箭头始终显示
            if (children.isEmpty()) {
                children.add(TreeItemViewModel())
            }
        } finally {
            isLoading = false
        }
    }

    fun collapseRecursive() {
        isExpanded = false
        for (child in children) {
            child.collapseRecursive()
        }
    }

    // 展开并等待子节点加载完成。由MainViewModel.expandAll分批调用，以便用固定批次上限并发，而不是每个目录各起一个协程。
    suspend fun ensureLoaded() {
        if (!isDir) return
        isExpanded = true
        if (children.isNotEmpty() && children[0].isPlaceholder) {
            loadChildren()
        }
    }
}

private class PlaceholderArtifact : IArtifact {
    override val id: String = ""
    override val name: String = ""
    override val size: Long = 0
    override val lastMod: Long = 0
    override val extension: String = ""
    override val kind: com.example.glance.Models.ArtifactKind = com.example.glance.Models.ArtifactKind.Text
    override val source: com.example.glance.Models.ArtifactSource = com.example.glance.Models.ArtifactSource.Local
    override val status: com.example.glance.Models.ArtifactStatus = com.example.glance.Models.ArtifactStatus.Available
    override val metadata: com.example.glance.Models.ArtifactMetadata? = null
    override val payload: com.example.glance.Models.IArtifactPayload = LocalPayload("", "", false)
}

/**
 * 常见噪声目录：依赖缓存、VCS、IDE与构建产物。浏览树上不显示，
 * 避免node_modules级别的仓库把扁平化列表和LazyColumn撑爆。
 * 注意：computeTotalSize不做此过滤，工作区总大小仍反映真实磁盘占用。
 */
internal val IGNORED_DIR_NAMES: Set<String> = setOf(
    ".git",
    "node_modules",
    ".gradle",
    ".idea",
    ".dart_tool",
    "__pycache__",
    ".venv",
    "venv",
    ".next",
    ".cache",
    ".pytest_cache",
    ".mypy_cache",
    "coverage",
    "Pods",
    "DerivedData",
)

/** 过滤掉 [IGNORED_DIR_NAMES]中的目录项，文件不受影响。 */
internal fun filterIgnoredDirs(items: List<IArtifact>): List<IArtifact> {
    if (IGNORED_DIR_NAMES.isEmpty()) return items
    return items.filterNot { item ->
        val payload = item.payload as? LocalPayload
        payload != null && payload.isDir && item.name in IGNORED_DIR_NAMES
    }
}

// 文件类型图标：extension不变，结果在TreeItemViewModel构造时算一次缓存
internal fun fileIconFor(extension: String): ImageVector {
    return when (extension.lowercase().trimStart('.')) {
        "kt", "kts", "java", "py", "js", "ts", "jsx", "tsx", "rs", "go", "swift", "c", "cpp", "h", "hpp", "cs" -> Icons.Filled.Code
        "md", "markdown", "txt" -> Icons.Filled.Description
        "json", "xml", "yaml", "yml", "toml" -> Icons.Filled.Settings
        "png", "jpg", "jpeg", "gif", "svg", "ico" -> Icons.Filled.Image
        "pdf" -> Icons.Filled.PictureAsPdf
        "zip", "tar", "gz", "rar" -> Icons.Filled.Archive
        "sh", "bash", "zsh" -> Icons.Filled.Terminal
        "gradle", "gradle.kts" -> Icons.Filled.Settings
        else -> Icons.AutoMirrored.Filled.InsertDriveFile
    }
}

