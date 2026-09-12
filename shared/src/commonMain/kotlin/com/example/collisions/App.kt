package com.example.collisions

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import com.example.collisions.Repositories.LocalArtifactRepo
import com.example.collisions.Repositories.LocalFileSystem
import com.example.collisions.ViewModels.MainViewModel
import com.example.collisions.Views.MainView

@Composable
fun App(
    pickFolderAction: (suspend () -> String?)? = null,
) {
    val viewModel = remember {
        val fs = LocalFileSystem()
        val repo = LocalArtifactRepo(fs)
        val vm = MainViewModel(fs, repo)
        vm.pickFolderAction = pickFolderAction
        vm
    }
    SideEffect {
        viewModel.pickFolderAction = pickFolderAction
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
