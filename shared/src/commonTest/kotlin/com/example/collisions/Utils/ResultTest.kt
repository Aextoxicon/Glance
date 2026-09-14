package com.example.glance.Utils

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlin.test.fail

class ResultTest {
    @Test
    fun `success should hold value`() {
        val result = Result.success(42)
        assertTrue(result.isSuccess)
        assertFalse(result.isFailure)
        assertEquals(42, result.getOrNull())
    }

    @Test
    fun `failure should hold error`() {
        val error = Exception("boom")
        val result = Result.failure<Int>(error)
        assertTrue(result.isFailure)
        assertFalse(result.isSuccess)
        assertNull(result.getOrNull())
    }

    @Test
    fun `match should call onSuccess for success`() {
        val result = Result.success("hello")
        val output = result.match(
            onSuccess = { it.uppercase() },
            onFailure = { "error: ${it.message}" },
        )
        assertEquals("HELLO", output)
    }

    @Test
    fun `match should call onFailure for failure`() {
        val result = Result.failure<String>(Exception("boom"))
        val output = result.match(
            onSuccess = { it },
            onFailure = { "error: ${it.message}" },
        )
        assertEquals("error: boom", output)
    }

    @Test
    fun `getOrNull should return null on failure`() {
        val result = Result.failure<Int>(Exception("boom"))
        assertNull(result.getOrNull())
    }

    @Test
    fun `sealed class should be exhaustive in when`() {
        val result = Result.success(1)
        val output = when (result) {
            is Result.Success -> "success"
            is Result.Failure -> "failure"
        }
        assertEquals("success", output)
    }
}