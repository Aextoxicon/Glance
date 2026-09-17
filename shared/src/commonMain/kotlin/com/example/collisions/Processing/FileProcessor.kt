package com.example.glance.Processing

object FileProcessor {
    private val textExtensions = setOf("txt", "md", "markdown")

    private val filenameToExt = mapOf(
        "dockerfile" to "dockerfile",
        "containerfile" to "dockerfile",
        "makefile" to "makefile",
        "gnumakefile" to "makefile",
    )

    fun process(content: String, extension: String, filename: String? = null): CodeParseResult {
        val cleanExt = extension.lowercase().trimStart('.')
        if (cleanExt in textExtensions) {
            return CodeParseResult.PlainText(language = cleanExt, content = content)
        }

        val resolvedExt = if (cleanExt.isEmpty() && filename != null) {
            filenameToExt[filename.lowercase().trimStart('.')] ?: cleanExt
        } else {
            cleanExt
        }

        // 非纯文本直接传给Rust解析，扩展名路由改成Rust侧维护
        return parseCode(content, ".$resolvedExt")
    }
}