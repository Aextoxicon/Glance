package com.example.glance.Views

import androidx.compose.foundation.*
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.InsertDriveFile
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Archive
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Code
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.FolderOpen
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.PictureAsPdf
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Terminal
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.glance.ViewModels.MainViewModel
import com.example.glance.ViewModels.TreeItemViewModel
import com.example.glance.Utils.HighlightColor
import com.example.glance.Processing.CodeParseResult

@Composable
fun MainView(viewModel: MainViewModel) {
    var windowWidth by remember { mutableStateOf(0f) }
    LaunchedEffect(windowWidth) {
        if (windowWidth > 0f) {
            viewModel.onWindowResized(windowWidth.toDouble())
        }
    }

    BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
        windowWidth = maxWidth.value

        if (!viewModel.hasWorkspace) {
            WelcomeScreen(onOpenFolder = { viewModel.pickFolder() })
        } else if (viewModel.isWide) {
            WideLayout(viewModel)
        } else {
            NarrowLayout(viewModel)
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
    val drawerState = rememberDrawerState(initialValue = DrawerValue.Closed)

    LaunchedEffect(viewModel.isDrawerOpen) {
        if (viewModel.isDrawerOpen) drawerState.open() else drawerState.close()
    }
    LaunchedEffect(drawerState.isOpen) {
        if (drawerState.isOpen != viewModel.isDrawerOpen) viewModel.toggleDrawer()
    }

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
                        TextButton(onClick = { viewModel.toggleDrawer() }) {
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
            TextButton(onClick = { viewModel.collapseAll() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) { Text("折叠", fontSize = 12.sp) }
            TextButton(onClick = { viewModel.closeWorkspace() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) {
                Icon(Icons.Filled.Close, contentDescription = "关闭", modifier = Modifier.size(14.dp))
            }
        }
    }
    HorizontalDivider()
}

private fun flattenTree(items: List<TreeItemViewModel>, depth: Int = 0): List<Pair<Int, TreeItemViewModel>> {
    val snapshot = items.toList() // 快照，避免 ConcurrentModificationException
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

    LazyColumn(modifier = modifier.fillMaxWidth(), state = listState) {
        items(items = flatItems.value, key = { (_, item) -> item.artifact.id }) { (depth, item) ->
            TreeItemRow(
                depth = depth,
                item = item,
                isSelected = viewModel.selectedArtifact?.id == item.artifact.id,
                onClick = { viewModel.selectItem(item) },
            )
        }
    }
}

@Composable
private fun TreeItemRow(depth: Int, item: TreeItemViewModel, isSelected: Boolean, onClick: () -> Unit) {
    val indent = (depth * 20).dp
    val bgColor = if (isSelected) MaterialTheme.colorScheme.primaryContainer else Color.Transparent

    Surface(modifier = Modifier.fillMaxWidth().clickable(onClick = onClick), color = bgColor) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.padding(start = 8.dp + indent, end = 8.dp, top = 2.dp, bottom = 2.dp).heightIn(min = 28.dp),
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
                if (item.isDir) (if (item.isExpanded) Icons.Filled.FolderOpen else Icons.Filled.Folder) else getFileIcon(item.artifact.extension),
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
}

private fun getFileIcon(extension: String): ImageVector {
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
                viewModel.selectedContent != null -> CodeContentView(content = viewModel.selectedContent ?: "", parseResult = viewModel.selectedParseResult)
                else -> Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
            }
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
        TextButton(onClick = { viewModel.clearSelection() }, modifier = Modifier.height(28.dp), contentPadding = PaddingValues(horizontal = 4.dp)) {
            Icon(Icons.Filled.Close, contentDescription = "关闭预览", modifier = Modifier.size(14.dp))
        }
    }
}

@Composable
private fun CodeContentView(content: String, parseResult: CodeParseResult?) {
    val scrollState = rememberScrollState()
    val annotatedString = remember(parseResult, content) {
        if (parseResult != null) HighlightColor.toAnnotatedString(parseResult) else androidx.compose.ui.text.AnnotatedString(content)
    }

    Box(
        modifier = Modifier.fillMaxSize()
            .background(MaterialTheme.colorScheme.surface)
            .horizontalScroll(scrollState)
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
    ) {
        SelectionContainer {
            Text(text = annotatedString, fontFamily = FontFamily.Monospace, fontSize = 13.sp, lineHeight = 20.sp)
        }
    }
}

@Composable
private fun MessageView(message: String) {
    Box(modifier = Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface).padding(32.dp), contentAlignment = Alignment.Center) {
        Text(message, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}