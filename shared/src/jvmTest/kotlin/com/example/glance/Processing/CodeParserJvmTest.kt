package com.example.glance.Processing

import kotlin.test.Test
import kotlin.test.assertTrue
import kotlin.test.assertFalse
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull

class CodeParserJvmTest {

    @Test
    fun `parse Python code returns highlights`() {
        val source = """
            def greet(name: str) -> str:
                x = 42
                return f"Hello, {name}"
        """.trimIndent()

        // Rust端parse_code需要带点号的扩展名
        val result = parseCode(source, ".py")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights")
            val tokens = result.highlightsByLine.flatten()
            assertTrue(tokens.isNotEmpty(), "Expected highlight tokens")

            val kinds = tokens.map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds")

            assertFalse(kinds.any { it.contains("_") && it == it.uppercase() }, "Found unmapped kinds: $kinds")

            assertTrue("function" in kinds || "def" in kinds, "Expected function/def token, got: $kinds")
            assertTrue("keyword" in kinds || "return" in kinds, "Expected keyword/return token, got: $kinds")
            assertTrue("string" in kinds, "Expected string token, got: $kinds")
            assertTrue("number" in kinds, "Expected number token, got: $kinds")

            assertTrue(tokens.size >= 5, "Expected at least 5 tokens, got: ${tokens.size}")
        }
    }

    @Test
    fun `parse Python code returns outline`() {
        val source = """
            class Foo:
                def bar(self) -> None:
                    pass
                def baz(self) -> None:
                    pass
        """.trimIndent()

        val result = parseCode(source, ".py")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.outline.isNotEmpty(), "Expected outline nodes")

            fun findByName(nodes: List<OutlineNode>, name: String): Boolean =
                nodes.any { it.name == name || findByName(it.children, name) }

            fun findKindByName(nodes: List<OutlineNode>, kind: String, name: String): Boolean =
                nodes.any { it.kind == kind && it.name == name } ||
                    nodes.any { findKindByName(it.children, kind, name) }

            assertTrue(findKindByName(result.outline, "class_definition", "Foo"), "Expected class_definition Foo in outline: ${result.outline}")

            assertTrue(findKindByName(result.outline, "function_definition", "bar"), "Expected function_definition bar in outline: ${result.outline}")
            assertTrue(findKindByName(result.outline, "function_definition", "baz"), "Expected function_definition baz in outline: ${result.outline}")

            val fooClass = result.outline.find { it.kind == "class_definition" && it.name == "Foo" }
            assertNotNull(fooClass, "Expected Foo class_definition node")
            assertTrue(fooClass.children.isNotEmpty(), "Expected Foo class_definition to have child methods")
            assertTrue(fooClass.children.size >= 2, "Expected at least 2 methods, got: ${fooClass.children.size}")
        }
    }

    @Test
    fun `unknown extension returns Code with empty highlights`() {
        val result = parseCode("just some text", ".unknown")
        assertTrue(result is CodeParseResult.Code, "Expected Code, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.all { it.isEmpty() }, "Expected empty highlights for unknown extension")
        }
    }

    @Test
    fun `parse Go code returns highlights`() {
        val source = """
            package main

            import "fmt"

            func main() {
                fmt.Println("hello world")
            }
        """.trimIndent()

        val result = parseCode(source, ".go")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for Go")
            val tokens = result.highlightsByLine.flatten()

            val kinds = tokens.map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds for Go")
            assertTrue("function" in kinds || "func" in kinds, "Expected function/func token, got: $kinds")
            assertTrue("keyword" in kinds || "var" in kinds, "Expected keyword/var token, got: $kinds")
            assertTrue("string" in kinds, "Expected string token, got: $kinds")
            assertTrue("keyword" in kinds || "import" in kinds, "Expected keyword/import token, got: $kinds")
        }
    }

    @Test
    fun `parse JavaScript code returns highlights`() {
        val source = """
            function greet(name) {
                const x = 42;
                return `Hello, ${'$'}name`;
            }
        """.trimIndent()

        val result = parseCode(source, ".js")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for JavaScript")
            val kinds = result.highlightsByLine.flatten().map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds for JavaScript")
            assertFalse(kinds.any { it.contains("_") && it == it.uppercase() }, "Found unmapped kinds: $kinds")
        }
    }

    @Test
    fun `parse Rust code returns highlights`() {
        val source = """
            fn main() {
                let x = 42;
                println!("hello world");
            }
        """.trimIndent()

        val result = parseCode(source, ".rs")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for Rust")
            val kinds = result.highlightsByLine.flatten().map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds for Rust")
            assertFalse(kinds.any { it.contains("_") && it == it.uppercase() }, "Found unmapped kinds: $kinds")
        }
    }

    @Test
    fun `parse C code returns highlights`() {
        val source = """
            #include <stdio.h>

            int main() {
                printf("hello world");
                return 0;
            }
        """.trimIndent()

        val result = parseCode(source, ".c")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for C")
            val kinds = result.highlightsByLine.flatten().map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds for C")
            assertFalse(kinds.any { it.contains("_") && it == it.uppercase() }, "Found unmapped kinds: $kinds")
        }
    }

    @Test
    fun `parse Java code returns highlights`() {
        val source = """
            class Hello {
                public static void main(String[] args) {
                    System.out.println("hello");
                }
            }
        """.trimIndent()

        val result = parseCode(source, ".java")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for Java")
            val kinds = result.highlightsByLine.flatten().map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds for Java")
            assertFalse(kinds.any { it.contains("_") && it == it.uppercase() }, "Found unmapped kinds: $kinds")
        }
    }

    @Test
    fun `parse TypeScript code returns highlights`() {
        val source = """
            function greet(name: string): void {
                const x: number = 42;
            }
        """.trimIndent()

        val result = parseCode(source, ".ts")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for TypeScript")
            val kinds = result.highlightsByLine.flatten().map { it.kind }.toSet()
            assertTrue(kinds.isNotEmpty(), "Expected kinds for TypeScript")
            assertFalse(kinds.any { it.contains("_") && it == it.uppercase() }, "Found unmapped kinds: $kinds")
        }
    }

    @Test
    fun `unsupported extension returns empty highlights`() {
        val source = "some unknown code"
        val result = parseCode(source, ".unknown")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.all { it.isEmpty() }, "Expected empty highlights for unsupported extension")
        }
    }

    @Test
    fun `parse C code with line and block comments returns Comment tokens`() {
        val source = """
            int main() {
                // line comment
                /* block comment */
                return 0;
            }
        """.trimIndent()

        val result = parseCode(source, ".c")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            val kinds = result.highlightsByLine.flatten().map { it.kind }.toSet()
            assertTrue(
                "comment" in kinds,
                "Expected comment tokens for // and /* */, got kinds: $kinds"
            )
        }
    }

    @Test
    fun `empty source returns Code with empty highlights`() {
        val result = parseCode("", ".py")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.content.isEmpty(), "Expected empty content")
            assertTrue(result.highlightsByLine.isEmpty(), "Expected empty highlights for empty source")
            assertTrue(result.outline.isEmpty(), "Expected empty outline for empty source")
        }
    }

    @Test
    fun `whitespace only source returns Code with empty highlights`() {
        val result = parseCode("   \n\t\n  \n", ".py")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.all { it.isEmpty() }, "Expected empty highlights for whitespace only")
            assertTrue(result.outline.isEmpty(), "Expected empty outline for whitespace only")
        }
    }

    @Test
    fun `single line Python returns highlights`() {
        val result = parseCode("x = 1", ".py")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for single line")
            val tokens = result.highlightsByLine.flatten()
            assertTrue(tokens.isNotEmpty(), "Expected tokens for single line")
        }
    }

    @Test
    fun `source with special characters returns Code`() {
        val source = """
            def test():
                s = "引号 & 特殊<字符>"
                emoji = "🎉🚀"
                chinese = "你好，世界！"
        """.trimIndent()

        val result = parseCode(source, ".py")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.isNotEmpty(), "Expected highlights for special chars")
            assertTrue(result.content.contains("引号"), "Content should contain special characters")
        }
    }

    @Test
    fun `FileProcessor routes txt extension to PlainText`() {
        val content = "This is a plain text file."
        val result = FileProcessor.process(content, "txt")

        assertTrue(result is CodeParseResult.PlainText, "Expected PlainText for .txt, got $result")
        if (result is CodeParseResult.PlainText) {
            assertEquals("txt", result.language)
            assertEquals(content, result.content)
        }
    }

    @Test
    fun `FileProcessor routes md extension to PlainText`() {
        val content = "# Markdown Title\n\nSome content."
        val result = FileProcessor.process(content, "md")

        assertTrue(result is CodeParseResult.PlainText, "Expected PlainText for .md, got $result")
        if (result is CodeParseResult.PlainText) {
            assertEquals("md", result.language)
            assertEquals(content, result.content)
        }
    }

    @Test
    fun `FileProcessor routes markdown extension to PlainText`() {
        val content = "# Markdown Title"
        val result = FileProcessor.process(content, "markdown")

        assertTrue(result is CodeParseResult.PlainText, "Expected PlainText for .markdown, got $result")
        if (result is CodeParseResult.PlainText) {
            assertEquals("markdown", result.language)
            assertEquals(content, result.content)
        }
    }

    @Test
    fun `FileProcessor routes txt extension with uppercase to PlainText`() {
        val content = "Uppercase extension test."
        val result = FileProcessor.process(content, "TXT")

        assertTrue(result is CodeParseResult.PlainText, "Expected PlainText for .TXT, got $result")
        if (result is CodeParseResult.PlainText) {
            assertEquals("txt", result.language, "Extension should be lowercased")
            assertEquals(content, result.content)
        }
    }

    @Test
    fun `FileProcessor routes code extension to Code with parse`() {
        val content = "def hello():\n    pass"
        val result = FileProcessor.process(content, "py")

        assertTrue(result is CodeParseResult.Code, "Expected Code for .py, got $result")
        if (result is CodeParseResult.Code) {
            assertEquals("py", result.language)
            assertEquals(content, result.content)
        }
    }

    @Test
    fun `FileProcessor handles empty extension with filename Dockerfile`() {
        val content = "FROM ubuntu:22.04\nRUN echo hello"
        val result = FileProcessor.process(content, "", "Dockerfile")

        assertTrue(result is CodeParseResult.Code, "Expected Code for Dockerfile, got $result")
        if (result is CodeParseResult.Code) {
            assertEquals("dockerfile", result.language)
        }
    }

    @Test
    fun `FileProcessor handles empty extension with filename Makefile`() {
        val content = ".PHONY: all\nclean:\n\trm -rf build"
        val result = FileProcessor.process(content, "", "Makefile")

        assertTrue(result is CodeParseResult.Code, "Expected Code for Makefile, got $result")
        if (result is CodeParseResult.Code) {
            assertEquals("makefile", result.language)
        }
    }

    @Test
    fun `parseCode with dotless extension returns Code`() {
        val result = parseCode("some code", "unknown")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            assertTrue(result.highlightsByLine.all { it.isEmpty() }, "Expected empty highlights for unknown extension")
        }
    }

    @Test
    fun `parseCode with uppercase extension returns empty highlights`() {
        val result = parseCode("some code", ".PY")

        assertTrue(result is CodeParseResult.Code, "Expected Code result, got $result")
        if (result is CodeParseResult.Code) {
            // Rust端不识别大写扩展名，返回空highlights
            assertTrue(result.highlightsByLine.all { it.isEmpty() }, "Expected empty highlights for uppercase .PY extension")
        }
    }
}
