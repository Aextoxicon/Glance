package com.example.glance

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.ui.tooling.preview.Preview
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

class MainActivity : ComponentActivity() {
    private var openDirectoryCallback: ((Uri?) -> Unit)? = null

    private val openDirectoryLauncher = registerForActivityResult(
        ActivityResultContracts.OpenDocumentTree()
    ) { uri: Uri? ->
        openDirectoryCallback?.invoke(uri)
        openDirectoryCallback = null
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        // 初始化全局ApplicationContext
        AndroidContext.init(this)

        setContent {
            App(
                pickFolderAction = {
                    withContext(Dispatchers.Main) {
                        suspendCancellableCoroutine<String?> { continuation ->
                            openDirectoryCallback = { uri ->
                                if (uri != null) {
                                    // 持久化权限，重启后仍可访问此目录
                                    contentResolver.takePersistableUriPermission(
                                        uri, Intent.FLAG_GRANT_READ_URI_PERMISSION
                                    )
                                    continuation.resume(uri.toString())
                                } else {
                                    continuation.resume(null)
                                }
                            }
                            openDirectoryLauncher.launch(null)
                        }
                    }
                }
            )
        }
    }
}

@Preview
@Composable
fun AppAndroidPreview() {
    App()
}