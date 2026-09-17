package com.example.glance.ViewModels

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.runtime.toMutableStateList
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
    private val childrenCache: MutableMap<String, List<IArtifact>>? = null,
    // 状态线程：所有 Compose 状态写入统一在这里
    private val uiDispatcher: CoroutineDispatcher? = null,
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.Default,
    owner: Job? = null,
) {
    // owner 非空时作为 SupervisorJob 的 parent：父 Job 取消时级联中止所有子目录加载，
    // 避免 closeWorkspace 清空 childrenCache 后仍有任务回写造成泄漏。
    // SupervisorJob 保证兄弟任务之间互不影响。
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

    init {
        if (isDir) {
            children.add(TreeItemViewModel()) // 占位符
        }
    }

    private constructor() : this(
        artifact = PlaceholderArtifact(),
        repo = null,
        childrenCache = null,
    ) {
        isPlaceholder = true
    }

    val sizeDisplay: String
        get() {
            if (isDir) {
                // 目录项数在展开时由 children 决定；未展开不显示，避免 listFiles 时预扫子目录
                if (children.isEmpty() || children[0].isPlaceholder) return ""
                return "${children.count { !it.isPlaceholder }} 项"
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
        val cache = childrenCache ?: return
        isLoading = true
        try {
            val payload = artifact.payload as LocalPayload
            // 放 io 线程，children 的写入留在 ui 线程
            val items = cache.getOrPut(payload.absolutePath) {
                val listResult = withContext(ioDispatcher) { r.listAsync(payload.absolutePath) }
                listResult.getOrNull() ?: emptyList()
            }
            val sorted = items.sortedWith(compareBy({ !((it.payload as? LocalPayload)?.isDir ?: false) }, { it.name.lowercase() }))
            children.clear()
            for (child in sorted) {
                children.add(TreeItemViewModel(child, r, cache, uiDispatcher, ioDispatcher, owner = scope.coroutineContext.get(Job)))
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

    fun expandAllRecursive() {
        if (!isDir) return
        isExpanded = true
        if (children.isNotEmpty() && children[0].isPlaceholder) {
            scope.launch {
                loadChildren()
                for (child in children) {
                    if (!child.isPlaceholder) {
                        child.expandAllRecursive()
                    }
                }
            }
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
