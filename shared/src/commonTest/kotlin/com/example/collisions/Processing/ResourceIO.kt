package com.example.collisions.Processing

/** 跨平台资源加载（commonTest 声明，各平台 test source set 实现 actual） */
expect fun loadResource(path: String): String?

/** 将 golden 内容保存到 build 目录（供首次运行时手动复制回 resources） */
expect fun saveResource(path: String, content: String)
