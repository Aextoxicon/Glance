package com.example.glance.Utils

import kotlin.time.TimeSource

object Trace {
    var enabled = false

    private val baseValue = TimeSource.Monotonic.markNow()
    private var lastValue = TimeSource.Monotonic.markNow()

    fun mark(label: String, extra: String = "") {
        if (!enabled) return
        val now = TimeSource.Monotonic.markNow()
        val totalMs = (now - baseValue).inWholeMilliseconds
        val deltaMs = (now - lastValue).inWholeMilliseconds
        lastValue = now
        println("TRACE total_ms=$totalMs delta_ms=$deltaMs $label $extra".trim())
    }
}
