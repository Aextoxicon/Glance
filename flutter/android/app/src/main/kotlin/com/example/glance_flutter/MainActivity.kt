package com.example.glance

import android.content.ContentValues
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File

class MainActivity : FlutterActivity() {
    private var intentChannel: MethodChannel? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        intentChannel = MethodChannel(
            flutterEngine.dartExecutor.binaryMessenger,
            "glance/intent"
        ).also { ch ->
            ch.setMethodCallHandler { call, result ->
                when (call.method) {
                    "getInitialUri" -> result.success(intentDataUri(intent))
                    else -> result.notImplemented()
                }
            }
        }
        MethodChannel(
            flutterEngine.dartExecutor.binaryMessenger,
            "glance/trace"
        ).setMethodCallHandler { call, result ->
            when (call.method) {
                "publish" -> {
                    val path = call.argument<String>("path")
                    val mime = call.argument<String>("mime")
                    if (path == null || mime == null) {
                        result.error("bad_args", "path/mime required", null)
                    } else if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
                        result.success(null)
                    } else {
                        result.success(publishToDownloads(File(path), mime))
                    }
                }
                else -> result.notImplemented()
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        val uri = intentDataUri(intent)
        if (uri != null) intentChannel?.invokeMethod("openUri", uri)
    }

    // VIEW取data uri, SEND(分享)取EXTRA_STREAM
    private fun intentDataUri(intent: Intent): String? {
        intent.data?.let { return it.toString() }
        val stream = if (Build.VERSION.SDK_INT >= 33) {
            intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(Intent.EXTRA_STREAM) as? Uri
        }
        return stream?.toString()
    }

    // 经MediaStore写入公共Download/Glance-trace, 返回"Download/Glance-trace/<name>"; 失败抛异常
    private fun publishToDownloads(file: File, mime: String): String {
        val resolver = contentResolver
        val name = file.name
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, name)
            put(MediaStore.MediaColumns.MIME_TYPE, mime)
            put(
                MediaStore.MediaColumns.RELATIVE_PATH,
                Environment.DIRECTORY_DOWNLOADS + "/Glance-trace"
            )
        }
        val uri = resolver.insert(
            MediaStore.Downloads.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY),
            values
        ) ?: throw IllegalStateException("MediaStore insert failed")
        resolver.openOutputStream(uri)!!.use { out ->
            out.write(file.readBytes())
            out.flush()
        }
        return "Download/Glance-trace/$name"
    }
}
