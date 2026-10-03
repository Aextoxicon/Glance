package com.example.glance

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import com.example.glance.Repositories.LocalArtifactRepo
import com.example.glance.Repositories.LocalFileSystem
import com.example.glance.ViewModels.MainViewModel
import com.example.glance.Views.MainView

@Composable
fun App(
    pickFolderAction: (suspend () -> String?)? = null,
    // jvm绑定DirectoryWatcher用；回调仅在该ViewMode创建后执行一次
    onViewModelCreated: (MainViewModel) -> Unit = {},
) {
    val viewModel = remember {
        val fs = LocalFileSystem()
        val repo = LocalArtifactRepo(fs)
        val vm = MainViewModel(fs, repo)
        vm.pickFolderAction = pickFolderAction
        vm
    }
    LaunchedEffect(viewModel) {
        onViewModelCreated(viewModel)
    }
    MaterialTheme {
        MainView(viewModel)
    }

    DisposableEffect(viewModel) {
        onDispose {
            viewModel.dispose()
        }
    }
}
