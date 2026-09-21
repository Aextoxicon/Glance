package com.example.glance.Views

import androidx.compose.animation.*
import androidx.compose.animation.core.FastOutLinearInEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.InsertDriveFile
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.glance.Processing.CodeParseResult
import com.example.glance.Processing.OutlineNode
import com.example.glance.ViewModels.MainViewModel
import com.example.glance.ViewModels.TreeItemViewModel
import com.example.glance.Utils.HighlightColor
import kotlin.math.abs
import kotlinx.coroutines.launch

private val OUTLINE_DRAWER_WIDTH = 300.dp
private const val OUTLINE_SCRIM_FADE_MS = 500

@Composable
fun MainView(viewModel: MainViewModel) {
    var windowWidth by remember { mutableStateOf(0f) }
    LaunchedEffect(windowWidth) {
        if (windowWidth > 0f) {
            viewModel.onWindowResized(windowWidth.toDouble())
        }
    }
    Box(
        modifier = Modifier
            .fillMaxSize()
            .onSizeChanged { size ->
                val newWidth = size.width.toFloat()
                if (abs(newWidth - windowWidth) >= 1f) {
                    windowWidth = newWidth
                }
            },
    ) {
        if (!viewModel.hasWorkspace) {
            WelcomeScreen(onOpenFolder = { viewModel.pickFolder() })
        } else if (viewModel.isWide) {
            WideLayout(viewModel)
        } else {
            NarrowLayout(viewModel)
        }
        AnimatedVisibility(
            visible = viewModel.outlineOpen,
            enter = fadeIn(
                animationSpec = tween(durationMillis = OUTLINE_SCRIM_FADE_MS, easing = LinearOutSlowInEasing),
                initialAlpha = 0f,
            ),
            exit = fadeOut(
                animationSpec = tween(durationMillis = OUTLINE_SCRIM_FADE_MS, easing = FastOutLinearInEasing),
                targetAlpha = 0f,
            ),
        ) {
            OutlineScrim(onDismiss = { viewModel.closeOutline() })
        }

        val panelWidthPx = with(LocalDensity.current) { OUTLINE_DRAWER_WIDTH.roundToPx() }
        AnimatedVisibility(
            visible = viewModel.outlineOpen,
            enter = fadeIn() + slideInHorizontally { panelWidthPx },
            exit = fadeOut() + slideOutHorizontally { panelWidthPx },
            modifier = Modifier.align(Alignment.CenterEnd),
        ) {
            OutlineDrawer(
                outline = viewModel.currentOutline.orEmpty(),
                onDismiss = { viewModel.closeOutline() },
                onSelectNode = { node ->
                    val content = viewModel.selectedContent
                    if (content != null) {
                        viewModel.requestScrollToLine(lineIndexAt(content, node.startByte))
                    }
                },
            )
        }
    }
}

@Composable
private fun WelcomeScreen(onOpenFolder: () -> Unit) {
    Box(
        modifier = Modifier.fillMaxSize(),
        contentAlignment = Alignment.Center,
    ) {
        FilledTonalButton(onClick = onOpenFolder) {
            Icon(Icons.Filled.FolderOpen, contentDescription = null, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(8.dp))
            Text("打开文件夹")
        }
    }
}

@Composable
private fun WideLayout(viewModel: MainViewModel) {
    Row(modifier = Modifier.fillMaxSize()) {
        Surface(
            modifier = Modifier.width(300.dp).fillMaxHeight(),
            color = MaterialTheme.colorScheme.surfaceVariant,
            tonalElevation = 1.dp,
        ) {
            Column {
                WorkspaceHeader(viewModel)
                FileTreePanel(viewModel, Modifier.weight(1f))
            }
        }
        VerticalDivider(modifier = Modifier.fillMaxHeight(), color = MaterialTheme.colorScheme.outlineVariant)
        CodePreviewPanel(viewModel, Modifier.weight(1f))
    }
}

@Composable
private fun NarrowLayout(viewModel: MainViewModel) {
    // 抽屉状态只保留drawerState一份真源
    // 变宽时MainView切走WideLayout，NarrowLayout离开组合，rememberDrawerState随之丢弃
    val drawerState = rememberDrawerState(initialValue = DrawerValue.Closed)
    val scope = rememberCoroutineScope()

    ModalNavigationDrawer(
        drawerState = drawerState,
        drawerContent = {
            ModalDrawerSheet(modifier = Modifier.width(300.dp)) {
                WorkspaceHeader(viewModel)
                FileTreePanel(viewModel, Modifier.weight(1f))
            }
        },
    ) {
        Scaffold(
            topBar = {
                TopAppBar(
                    title = {
                        Text(
                            viewModel.selectedArtifact?.name ?: viewModel.currentFolderName.ifBlank { "Glance" },
                            maxLines = 1, overflow = TextOverflow.Ellipsis,
                        )
                    },
                    navigationIcon = {
                        TextButton(onClick = {
                            scope.launch {
                                if (drawerState.isClosed) drawerState.open() else drawerState.close()
                            }
                        }) {
                            Icon(Icons.Filled.Menu, contentDescription = "菜单", modifier = Modifier.size(20.dp))
                        }
                    },
                    actions = {
                        if (viewModel.totalSize > 0) {
                            Text(viewModel.totalSizeReadable, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            Spacer(Modifier.width(8.dp))
                        }
                    },
                )
            },
        ) { padding ->
            Box(modifier = Modifier.fillMaxSize().padding(padding)) {
                CodePreviewPanel(viewModel, Modifier.fillMaxSize())
            }
        }
    }
}

@Composable
private fun WorkspaceHeader(viewModel: MainViewModel) {
    Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
            Text(viewModel.currentFolderName.ifBlank { "工作区" }, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            if (viewModel.isComputingSize) {
                CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp)
            }
        }
        Spacer(Modifier.height(4.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(viewModel.totalSizeReadable, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.weight(1f))
            TextButton(onClick = { viewModel.expandAll() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) { Text("展开全部", fontSize = 12.sp) }
            TextButton(onClick = { viewModel.collapseAll() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) { Text("折叠", fontSize = 12.sp) }
            TextButton(onClick = { viewModel.closeWorkspace() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) {
                Icon(Icons.Filled.Close, contentDescription = "关闭", modifier = Modifier.size(14.dp))
            }
        }
    }
    HorizontalDivider()
}

private fun flattenTree(items: List<TreeItemViewModel>, depth: Int = 0): List<Pair<Int, TreeItemViewModel>> {
    val snapshot = items.toList() // 快照，避免ConcurrentModificationException
    val result = mutableListOf<Pair<Int, TreeItemViewModel>>()
    for (item in snapshot) {
        if (item.isPlaceholder) continue
        result.add(depth to item)
        if (item.isExpanded && item.isDir) {
            if (item.children.size == 1 && item.children[0].isPlaceholder) continue
            result.addAll(flattenTree(item.children, depth + 1))
        }
    }
    return result
}

@Composable
private fun FileTreePanel(viewModel: MainViewModel, modifier: Modifier = Modifier) {
    val flatItems = remember { derivedStateOf { flattenTree(viewModel.treeItems) } }
    val listState = rememberLazyListState()

    if (viewModel.treeItems.isEmpty()) {
        Box(modifier = modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            Text("空目录", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
        }
        return
    }

    Box(modifier = modifier.fillMaxWidth()) {
        LazyColumn(modifier = Modifier.fillMaxSize(), state = listState) {
            items(items = flatItems.value, key = { (_, item) -> item.artifact.id }, contentType = { (_, item) -> if (item.isDir) "dir" else "file" }) { (depth, item) ->
                TreeItemRow(
                    depth = depth,
                    item = item,
                    isSelected = item.isSelected,
                    onSelect = viewModel::selectItem,
                )
            }
        }
        PlatformVerticalScrollbar(scrollState = listState, modifier = Modifier.align(Alignment.CenterEnd))
    }
}

@Composable
private fun TreeItemRow(depth: Int, item: TreeItemViewModel, isSelected: Boolean, onSelect: (TreeItemViewModel) -> Unit) {
    val indent = (depth * 20).dp
    val bgColor = if (isSelected) MaterialTheme.colorScheme.primaryContainer else Color.Transparent

    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .background(bgColor)
            .clickable(onClick = { onSelect(item) })
            .padding(start = 8.dp + indent, end = 8.dp, top = 2.dp, bottom = 2.dp)
            .heightIn(min = 28.dp),
    ) {
        if (item.isDir) {
            Icon(
                if (item.isExpanded) Icons.Filled.KeyboardArrowDown else Icons.AutoMirrored.Filled.KeyboardArrowRight,
                contentDescription = null,
                modifier = Modifier.size(16.dp),
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            Spacer(Modifier.width(16.dp))
        }
        Spacer(Modifier.width(4.dp))

        Icon(
            if (item.isDir) (if (item.isExpanded) Icons.Filled.FolderOpen else Icons.Filled.Folder) else item.icon,
            contentDescription = null,
            modifier = Modifier.size(16.dp),
            tint = if (item.isDir) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.width(8.dp))

        Text(item.artifact.name, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        Spacer(Modifier.width(4.dp))

        Text(item.sizeDisplay, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
    }
}

// 右侧大纲抽屉

private fun flattenOutline(nodes: List<OutlineNode>, depth: Int = 0): List<Pair<Int, OutlineNode>> {
    val result = mutableListOf<Pair<Int, OutlineNode>>()
    for (node in nodes) {
        result.add(depth to node)
        if (node.children.isNotEmpty()) {
            result.addAll(flattenOutline(node.children, depth + 1))
        }
    }
    return result
}

private fun outlineKindLabel(kind: String): String = when {
    kind.startsWith("class") || kind.startsWith("record") || kind.startsWith("annotation_type") -> "类"
    kind.startsWith("interface") -> "接口"
    kind.startsWith("struct") -> "结构体"
    kind.startsWith("enum") -> "枚举"
    kind.startsWith("trait") -> "trait"
    kind.startsWith("impl") -> "impl"
    kind.startsWith("method") -> "方法"
    kind.startsWith("function") || kind.startsWith("generator_function") -> "函数"
    kind.startsWith("namespace") || kind.startsWith("mod") -> "命名空间"
    kind.startsWith("type_alias") || kind.startsWith("type_item") || kind == "type_declaration" -> "类型"
    kind.startsWith("static") || kind.startsWith("const") -> "常量"
    kind == "package_clause" -> "包"
    else -> kind.substringBefore('_')
}

//OutlineNode.startByte已经是被rust的convert_outline转成 UTF-16 偏移
private fun lineIndexAt(content: String, offset: Long): Int {
    if (offset <= 0) return 0
    val end = offset.coerceAtMost(content.length.toLong()).toInt()
    var line = 0
    for (i in 0 until end) {
        if (content[i] == '\n') line++
    }
    return line
}

@Composable
private fun OutlineDrawer(
    outline: List<OutlineNode>,
    onDismiss: () -> Unit,
    onSelectNode: (OutlineNode) -> Unit,
) {
    val flatItems = remember(outline) { flattenOutline(outline) }

    Surface(
        modifier = Modifier.fillMaxHeight().width(OUTLINE_DRAWER_WIDTH),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 2.dp,
        shadowElevation = 8.dp,
    ) {
        Column {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.fillMaxWidth().padding(start = 12.dp, end = 4.dp).heightIn(min = 44.dp),
            ) {
                Text("大纲", style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
                TextButton(
                    onClick = onDismiss,
                    modifier = Modifier.height(28.dp),
                    contentPadding = PaddingValues(horizontal = 4.dp),
                ) {
                    Icon(Icons.Filled.Close, contentDescription = "关闭大纲", modifier = Modifier.size(14.dp))
                }
            }
            HorizontalDivider()

            if (flatItems.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        "该文件没有大纲",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            } else {
                val listState = rememberLazyListState()
                Box(modifier = Modifier.fillMaxSize()) {
                    LazyColumn(state = listState, modifier = Modifier.fillMaxSize()) {
                        items(
                            items = flatItems,
                            key = { (_, node) -> "${node.startByte}-${node.endByte}-${node.kind}" },
                        ) { (depth, node) ->
                            OutlineRow(depth = depth, node = node, onClick = { onSelectNode(node) })
                        }
                    }
                    PlatformVerticalScrollbar(scrollState = listState, modifier = Modifier.align(Alignment.CenterEnd))
                }
            }
        }
    }
}

@Composable
private fun OutlineScrim(onDismiss: () -> Unit) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.32f))
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null,
                onClick = onDismiss,
            ),
    )
}

@Composable
private fun OutlineRow(depth: Int, node: OutlineNode, onClick: () -> Unit) {
    val indent = (depth * 14).dp
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(start = 12.dp + indent, end = 12.dp, top = 3.dp, bottom = 3.dp)
            .heightIn(min = 30.dp),
    ) {
        Text(
            outlineKindLabel(node.kind),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.primary,
            modifier = Modifier.width(48.dp),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Spacer(Modifier.width(6.dp))
        Text(
            node.name.ifBlank { "（无名）" },
            style = MaterialTheme.typography.bodySmall,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
    }
}

@Composable
private fun CodePreviewPanel(viewModel: MainViewModel, modifier: Modifier = Modifier) {
    Column(modifier = modifier) {
        if (!viewModel.hasSelection) {
            Box(modifier = Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(
                        Icons.AutoMirrored.Filled.InsertDriveFile,
                        contentDescription = null,
                        modifier = Modifier.size(48.dp),
                        tint = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.4f),
                    )
                    Spacer(Modifier.height(8.dp))
                    Text("选择文件以预览", color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f), style = MaterialTheme.typography.bodyMedium)
                }
            }
            return
        }

        CodePreviewToolbar(viewModel)
        HorizontalDivider()

        Box(modifier = Modifier.fillMaxSize()) {
            when {
                viewModel.messageText != null -> MessageView(viewModel.messageText ?: "")
                viewModel.selectedContent != null -> key(viewModel.selectedArtifact?.id) {
                    CodeContentView(
                        parseResult = viewModel.selectedParseResult,
                        content = viewModel.selectedContent ?: "",
                        scrollTargetLine = viewModel.outlineScrollTargetLine,
                        onScrolled = { viewModel.consumeScrollTargetLine() },
                    )
                }
                else -> Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
            }
        }

        // 大文件跳过高亮等降级提示
        viewModel.previewNotice?.let { notice ->
            HorizontalDivider()
            Text(
                notice,
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun CodePreviewToolbar(viewModel: MainViewModel) {
    val artifact = viewModel.selectedArtifact ?: return
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp).heightIn(min = 40.dp),
    ) {
        Text(artifact.name, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        Spacer(Modifier.width(8.dp))
        Text(viewModel.selectedSizeDisplay, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.width(8.dp))
        // 只有确实有大纲时才给入口
        if (viewModel.currentOutline != null) {
            TextButton(
                onClick = { viewModel.toggleOutline() },
                modifier = Modifier.height(28.dp),
                contentPadding = PaddingValues(horizontal = 4.dp),
            ) {
                Text("大纲", fontSize = 12.sp)
            }
            Spacer(Modifier.width(4.dp))
        }
        TextButton(onClick = { viewModel.clearSelection() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) {
            Icon(Icons.Filled.Close, contentDescription = "关闭预览", modifier = Modifier.size(14.dp))
        }
    }
}

private val CodeLineHeight = 20.sp
private val LineSeparator = AnnotatedString("\n")

@Composable
private fun CodeContentView(
    parseResult: CodeParseResult?,
    content: String,
    scrollTargetLine: Int?,
    onScrolled: () -> Unit,
) {
    val horizontalScrollState = rememberScrollState()
    val lazyListState = rememberLazyListState()
    val lineHeight = with(LocalDensity.current) { CodeLineHeight.toDp() }
    val lines = remember(content) { content.split("\n") }

    LaunchedEffect(scrollTargetLine) {
        val line = scrollTargetLine ?: return@LaunchedEffect
        lazyListState.scrollToItem((line - 1).coerceIn(0, (lines.size - 1).coerceAtLeast(0)))
        onScrolled()
    }

    // LazyColumn只构建可见行，每行独立TextLayout
    // 固定行高 LazyLayout直接算偏移
    // 长行撑开LazyColumn宽度，外层统一横向滚动，行列对齐
    Box(
        modifier = Modifier.fillMaxSize()
            .background(MaterialTheme.colorScheme.surface),
    ) {
        SelectionContainer {
            Box(modifier = Modifier.fillMaxSize().horizontalScroll(horizontalScrollState)) {
                LazyColumn(
                    state = lazyListState,
                    modifier = Modifier.fillMaxHeight(),
                    contentPadding = PaddingValues(16.dp),
                ) {
                    items(count = lines.size, key = { it }) { index ->
                        val text = when {
                            parseResult is CodeParseResult.Code && index < parseResult.highlightsByLine.size ->
                                HighlightColor.buildLineAnnotatedString(lines[index], parseResult.highlightsByLine[index])
                            else -> AnnotatedString(lines[index])
                        }
                        Text(
                            text = text + LineSeparator,
                            fontFamily = FontFamily.Monospace,
                            fontSize = 13.sp,
                            lineHeight = CodeLineHeight,
                            maxLines = 1,
                            softWrap = false,
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(lineHeight),
                        )
                    }
                }
            }
        }
        PlatformVerticalScrollbar(scrollState = lazyListState, modifier = Modifier.align(Alignment.CenterEnd))
    }
}

@Composable
private fun MessageView(message: String) {
    Box(modifier = Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface).padding(32.dp), contentAlignment = Alignment.Center) {
        Text(message, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}