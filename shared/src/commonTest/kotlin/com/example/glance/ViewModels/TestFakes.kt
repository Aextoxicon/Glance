package com.example.glance.ViewModels

import com.example.glance.Models.ArtifactKind
import com.example.glance.Models.IArtifact
import com.example.glance.Models.LocalArtifact
import com.example.glance.Models.LocalPayload
import com.example.glance.Repositories.IArtifactRepo
import com.example.glance.Repositories.TextFileDetector
import com.example.glance.Utils.Result
import kotlinx.coroutines.CompletableDeferred

class AlwaysTextDetector : TextFileDetector {
    override fun isTextFile(path: String): Boolean = true
}

class FakeRepo : IArtifactRepo {
    val listGates: MutableMap<String, ArrayDeque<CompletableDeferred<Result<List<IArtifact>>>>> = mutableMapOf()
    val readGates: MutableMap<String, CompletableDeferred<Result<String>>> = mutableMapOf()
    // 每个文件的tryReadTextAsync调用次数：缓存未命中时会再涨一次，用来观测是否被缓存
    val readCalls: MutableMap<String, Int> = mutableMapOf()

    override suspend fun listAsync(path: String): Result<List<IArtifact>> {
        val gates = listGates[path]
        if (gates != null && gates.isNotEmpty()) {
            return gates.removeFirst().await()
        }
        return Result.success(emptyList())
    }

    override suspend fun searchAsync(query: String): Result<List<IArtifact>> = Result.success(emptyList())

    override suspend fun getAsync(id: String): Result<IArtifact> =
        Result.failure(Exception("unexpected getAsync($id)"))

    override suspend fun deleteAsync(id: String): Result<Boolean> = Result.success(true)

    override suspend fun getContUriAsync(id: String): Result<String> =
        Result.failure(Exception("unexpected getContUriAsync($id)"))

    override suspend fun tryReadTextAsync(id: String): Result<String> {
        readCalls[id] = (readCalls[id] ?: 0) + 1
        val gate = readGates[id]
        if (gate != null) return gate.await()
        return Result.success("content of $id")
    }
}

fun fileArtifact(parent: String, name: String, size: Long = 10L): LocalArtifact =
    LocalArtifact(
        id = "$parent/$name",
        name = name,
        size = size,
        lastMod = 0,
        extension = "txt",
        kind = ArtifactKind.Text,
        local = LocalPayload("$parent/$name", parent, isDir = false),
    )

fun dirArtifact(parent: String, name: String): LocalArtifact =
    LocalArtifact(
        id = "$parent/$name",
        name = name,
        size = 0,
        lastMod = 0,
        extension = "",
        kind = ArtifactKind.Text,
        local = LocalPayload("$parent/$name", parent, isDir = true),
    )

fun queueGates(vararg results: Result<List<IArtifact>>): ArrayDeque<CompletableDeferred<Result<List<IArtifact>>>> =
    ArrayDeque(results.map { result ->
        CompletableDeferred<Result<List<IArtifact>>>().also { it.complete(result) }
    })
