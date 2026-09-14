package com.example.glance

import android.annotation.SuppressLint
import android.content.Context

/**
 * Android 全局 ApplicationContext 持有者。
 * 在 Application.onCreate() 或 MainActivity.onCreate() 中初始化。
 */
object AndroidContext {
    @SuppressLint("StaticFieldLeak")
    private var _context: Context? = null

    val context: Context
        get() = _context ?: throw IllegalStateException("AndroidContext 未初始化，请在 Application 或 Activity 中调用 init()")

    fun init(context: Context) {
        _context = context.applicationContext
    }
}