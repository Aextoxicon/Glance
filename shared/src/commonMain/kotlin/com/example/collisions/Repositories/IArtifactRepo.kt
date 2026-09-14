package com.example.glance.Repositories

import com.example.glance.Models.IArtifact
import com.example.glance.Utils.Result

interface IArtifactRepo {
    suspend fun listAsync(path: String): Result<List<IArtifact>>
    suspend fun searchAsync(query: String): Result<List<IArtifact>>
    suspend fun getAsync(id: String): Result<IArtifact>
    suspend fun deleteAsync(id: String): Result<Boolean>
    suspend fun getContUriAsync(id: String): Result<String>
    suspend fun tryReadTextAsync(id: String): Result<String>
}