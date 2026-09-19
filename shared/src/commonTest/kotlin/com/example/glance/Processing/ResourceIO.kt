package com.example.glance.Processing

/** 跨平台资源加载（commonTest声明，各平台test source set实现actual） */
expect fun loadResource(path: String): String?

/** 将golden内容保存到build目录（供首次运行时手动复制回resources） */
expect fun saveResource(path: String, content: String)
