package com.example.glance.Utils

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.IOException
import java.nio.file.ClosedWatchServiceException
import java.nio.file.FileSystems
import java.nio.file.Files
import java.nio.file.Paths
import java.nio.file.StandardWatchEventKinds
import java.nio.file.WatchKey
import java.nio.file.WatchService

/**
 * 对应官方教程《Watching a Directory for Changes》：
 */
class DirectoryWatcher(
    private val scope: CoroutineScope,
    private val debounceMs: Long = 300L,
    private val onBatchedChange: () -> Unit,
) {
    private var watcher: WatchService? = null
    private var worker: Job? = null
    private var debounceJob: Job? = null
    private val registered = mutableMapOf<String, WatchKey>()

    fun syncRoot(root: String?, expandedDirs: List<String>) {
        val normRoot = root?.let(::normalize)
        val target = if (normRoot.isNullOrEmpty()) {
            emptySet()
        } else {
            buildSet {
                add(normRoot)
                for (dir in expandedDirs) {
                    if (dir != normRoot) add(normalize(dir))
                }
            }
        }
        val current = registered.keys.toSet()
        for (path in current - target) {
            registered.remove(path)?.cancel()
        }
        if (target.isEmpty()) {
            stop()
            return
        }
        if (watcher == null) start()
        for (path in target - current) {
            register(path)
        }
    }

    fun close() {
        stop()
    }

    private fun start() {
        val newWatcher = try {
            FileSystems.getDefault().newWatchService()
        } catch (_: IOException) {
            return
        }
        watcher = newWatcher
        worker = scope.launch(Dispatchers.IO) {
            try {
                while (isActive) {
                    val key = try {
                        newWatcher.take()
                    } catch (_: ClosedWatchServiceException) {
                        return@launch
                    }
                    var dirty = false
                    for (event in key.pollEvents()) {
                        if (event.kind() == StandardWatchEventKinds.OVERFLOW) {
                            dirty = true
                            break
                        }
                        dirty = true
                    }
                    if (!key.reset()) {
                        registered.entries.removeIf { it.value == key }
                    }
                    if (dirty) scheduleDebouncedNotify()
                }
            } finally {
                if (watcher === newWatcher) watcher = null
            }
        }
    }

    private fun stop() {
        worker?.cancel()
        worker = null
        debounceJob?.cancel()
        debounceJob = null
        for (key in registered.values) {
            key.cancel()
        }
        registered.clear()
        try {
            watcher?.close()
        } catch (_: Exception) {
            
        }
        watcher = null
    }

    private fun register(pathString: String): WatchKey? {
        val ws = watcher ?: return null
        return try {
            val path = Paths.get(pathString).toAbsolutePath().normalize()
            if (!Files.isDirectory(path)) return null
            val key = path.register(
                ws,
                StandardWatchEventKinds.ENTRY_CREATE,
                StandardWatchEventKinds.ENTRY_MODIFY,
                StandardWatchEventKinds.ENTRY_DELETE,
            )
            registered[path.toString()] = key
            key
        } catch (_: Exception) {
            null
        }
    }

    private fun normalize(path: String): String = try {
        Paths.get(path).toAbsolutePath().normalize().toString()
    } catch (_: Exception) {
        path
    }

    private fun scheduleDebouncedNotify() {
        if (debounceJob?.isActive == true) return
        debounceJob = scope.launch {
            delay(debounceMs)
            debounceJob = null
            onBatchedChange()
        }
    }
}