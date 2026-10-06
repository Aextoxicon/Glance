package com.example.glance

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import com.example.glance.Utils.DirectoryWatcher
import com.example.glance.Utils.Trace
import com.example.glance.ViewModels.MainViewModel

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.withContext
import java.awt.FileDialog
import java.io.File
import javax.swing.JFileChooser
import javax.swing.UIManager

fun main() = application {
    Trace.enabled = System.getProperty("glance.trace") == "1"
    configureNativeLib()
    val windowState = rememberWindowState(size = DpSize(1280.dp, 800.dp))
    val fsWatcher = remember { mutableStateOf<DirectoryWatcher?>(null) }
    Window(
        onCloseRequest = {
            fsWatcher.value?.close()
            exitApplication()
        },
        state = windowState,
        title = "Glance",
    ) {
        App(
            pickFolderAction = {
                withContext(Dispatchers.IO) {
                    pickFolderNative()
                }
            },
            onViewModelCreated = { vm -> fsWatcher.value = bindFileWatcher(vm) },
        )
    }
}

// 文件树实时化
private fun bindFileWatcher(vm: MainViewModel): DirectoryWatcher {
    val watcher = DirectoryWatcher(
        scope = CoroutineScope(SupervisorJob() + Dispatchers.Main),
        debounceMs = 300L,
        onBatchedChange = { vm.refreshTree() },
    )
    vm.onWorkspaceOpened = {
        watcher.syncRoot(vm.currentPath, vm.currentExpandedDirPaths())
    }
    vm.onTreeDirsChanged = {
        watcher.syncRoot(vm.currentPath, vm.currentExpandedDirPaths())
    }
    return watcher
}

private fun configureNativeLib() {
    val resourcesDir = System.getProperty("compose.application.resources.dir") ?: return
    val osName = System.getProperty("os.name").lowercase()
    val libName = when {
        osName.contains("win") -> "uniffi_code_parser.dll"
        osName.contains("mac") -> "libuniffi_code_parser.dylib"
        else -> "libuniffi_code_parser.so"
    }
    val libFile = File(resourcesDir, libName)
    if (!libFile.exists()) {
        println("Warning: native library not found in distribution resources: ${libFile.absolutePath}")
        return
    }
    System.setProperty("uniffi.component.uniffi_code_parser.libraryOverride", libFile.absolutePath)
}

/** macOS:使用AWT FileDialog，调用NSOpenPanel
    Windows/Linux: JFileChooser+系统L&F
 */
private fun pickFolderNative(): String? {
    val osName = System.getProperty("os.name").lowercase()
    return if (osName.contains("mac")) {
        pickFolderMac()
    } else {
        pickFolderFallback()
    }
}

private fun pickFolderMac(): String? {
    System.setProperty("apple.awt.fileDialogForDirectories", "true")
    val dialog = FileDialog(null as java.awt.Frame?, "选择文件夹", FileDialog.LOAD)
    dialog.isVisible = true // 弹NSOpenPanel
    return dialog.directory?.let { dir ->
        dialog.file?.let { "${dir}${it}" } ?: dir
    }
}

private fun pickFolderFallback(): String? {
    try {
        UIManager.setLookAndFeel(UIManager.getSystemLookAndFeelClassName())
    } catch (_: Exception) {}
    val chooser = JFileChooser().apply {
        fileSelectionMode = JFileChooser.DIRECTORIES_ONLY
        isAcceptAllFileFilterUsed = false
    }
    return if (chooser.showOpenDialog(null) == JFileChooser.APPROVE_OPTION) {
        chooser.selectedFile?.absolutePath
    } else null
}
