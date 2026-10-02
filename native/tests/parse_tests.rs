use insta::assert_debug_snapshot;
use uniffi_code_parser::{parse_code, CodeParseResult, OutlineNode};

fn u32_at(src: &[u8], index: usize) -> u32 {
    let o = index * 4;
    u32::from_le_bytes([src[o], src[o + 1], src[o + 2], src[o + 3]])
}

/// 把扁平打包解回按行的 (start, end, kind) 三元组
fn unpack_lines(result: &CodeParseResult) -> Vec<Vec<(i32, i32, String)>> {
    let line_count = (result.line_index.len() / 4).saturating_sub(1);
    let mut out = Vec::with_capacity(line_count);
    for line in 0..line_count {
        let from = u32_at(&result.line_index, line) as usize;
        let to = u32_at(&result.line_index, line + 1) as usize;
        let mut row = Vec::with_capacity(to.saturating_sub(from));
        for i in from..to {
            row.push((
                u32_at(&result.highlight_data, i * 3) as i32,
                u32_at(&result.highlight_data, i * 3 + 1) as i32,
                result.kinds[u32_at(&result.highlight_data, i * 3 + 2) as usize].clone(),
            ));
        }
        out.push(row);
    }
    out
}

/// 高亮/大纲
#[derive(Debug)]
#[allow(dead_code)]
struct Snapshot<'a> {
    highlights: Vec<Vec<(i32, i32, String)>>,
    outline: &'a [OutlineNode],
}

fn snapshot_of(result: &CodeParseResult) -> Snapshot<'_> {
    Snapshot {
        highlights: unpack_lines(result),
        outline: &result.outline,
    }
}

macro_rules! lang_snapshot_test {
    ($name:ident, $ext:literal, $source:literal) => {
        #[test]
        fn $name() {
            let result: CodeParseResult = parse_code($source.to_string(), $ext.to_string());
            let total_tokens: usize = unpack_lines(&result).iter().map(|t| t.len()).sum();
            assert!(
                total_tokens > 0,
                "{}: expected at least 1 highlight token, got 0",
                $ext
            );
            assert_debug_snapshot!(snapshot_of(&result));
        }
    };
}

// 快照测试

lang_snapshot_test!(test_parse_c, ".c", "#include <stdio.h>\nint main() { /* c comment */ int x = 1; return 0; }\n");
lang_snapshot_test!(test_parse_h, ".h", "#ifndef H\n#define H\nint add(int a, int b); // header comment\n#endif\n");
lang_snapshot_test!(test_parse_cpp, ".cpp", "int main() { // cpp comment\n  auto x = 1; /* block */ return x;\n}\n");
lang_snapshot_test!(test_parse_hpp, ".hpp", "class Foo { public: int bar(); }; // hpp comment\n");
lang_snapshot_test!(test_parse_go, ".go", "package main\n\n// go comment\nfunc main() {\n\tfmt.Println(\"hi\")\n}\n");
lang_snapshot_test!(test_parse_python, ".py", "# python comment\n\ndef main():  # inline\n    return 1\n");
lang_snapshot_test!(test_parse_js, ".js", "// js comment\nfunction foo() { /* block */ return 1; }\n");
lang_snapshot_test!(test_parse_mjs, ".mjs", "// mjs comment\nexport const x = 1;\n");
lang_snapshot_test!(test_parse_cjs, ".cjs", "// cjs comment\nmodule.exports = 1;\n");
lang_snapshot_test!(test_parse_ts, ".ts", "// ts comment\nfunction add(a: number): number { return a; }\n");
lang_snapshot_test!(test_parse_tsx, ".tsx", "// tsx comment\nconst el = <div>hi</div>;\n");
lang_snapshot_test!(test_parse_sh, ".sh", "#!/bin/bash\n# shell comment\necho hello\n");
lang_snapshot_test!(test_parse_bash, ".bash", "# bash comment\necho hi\n");
lang_snapshot_test!(test_parse_zsh, ".zsh", "# zsh comment\necho hi\n");
lang_snapshot_test!(test_parse_cs, ".cs", "// cs comment\nclass C { int M() { return 1; } }\n");
lang_snapshot_test!(test_parse_java, ".java", "// java comment\npublic class Main { public static void main(String[] a) { /* block */ } }\n");
lang_snapshot_test!(test_parse_json, ".json", "{\n  \"key\": \"value\"  // jsonc not std, just test\n}\n");
lang_snapshot_test!(test_parse_css, ".css", "/* css comment */\nbody { color: red; }\n");
lang_snapshot_test!(test_parse_rs, ".rs", "// rust comment\nfn main() { /* block */ let x = 1; }\n");
lang_snapshot_test!(test_parse_toml, ".toml", "# toml comment\n[package]\nname = \"test\"\nversion = \"1.0.0\"\n");
lang_snapshot_test!(test_parse_yaml, ".yaml", "# yaml comment\nkey: value\nlist:\n  - a\n  - b\n");
lang_snapshot_test!(test_parse_yml, ".yml", "# yml comment\nkey: value\n");
lang_snapshot_test!(test_parse_ini, ".ini", "; ini comment\n[section]\nkey = value\n");
lang_snapshot_test!(test_parse_mk, ".mk", "# make comment\nall:\n\techo hello\n");
lang_snapshot_test!(test_parse_kt, ".kt", "// kotlin comment\nfun main() {\n    println(\"hi\")  /* block */\n}\n");
lang_snapshot_test!(test_parse_kts, ".kts", "// kts comment\nprintln(\"hi\")\n");
lang_snapshot_test!(test_parse_swift, ".swift", "// swift comment\nfunc main() { /* block */ print(\"hi\") }\n");
lang_snapshot_test!(test_parse_html, ".html", "<!-- html comment -->\n<div class=\"x\">hi</div>\n");
lang_snapshot_test!(test_parse_htm, ".htm", "<!-- htm comment -->\n<p>hello</p>\n");
lang_snapshot_test!(test_parse_rb, ".rb", "# ruby comment\nrequire \"singleton\"\nmodule M\n  class C\n    def m(x)\n      x > 1 ? 2 : 0\n    end\n  end\nend\n");
lang_snapshot_test!(test_parse_php, ".php", "<?php\n// php comment\nclass C {\n  public function m(int $x): int { return $x + 1; }\n}\n");
lang_snapshot_test!(test_parse_xml, ".xml", "<?xml version=\"1.0\"?>\n<!-- xml comment -->\n<root id=\"a\"><child>hi</child><empty/></root>\n");

// Unicode快照测试

#[test]
fn test_unicode_source_utf16_offsets() {
    // 含中文/emoji的源码，验证UTF-16偏移映射不破坏高亮
    let source = r#"import os

def hello():
    name = "你好世界👋"
    print(name)  # 打印名字
    return name
"#;
    let result: CodeParseResult = parse_code(source.to_string(), ".py".to_string());
    let total_tokens: usize = unpack_lines(&result).iter().map(|t| t.len()).sum();
    assert!(total_tokens > 5, "expected more than 5 tokens with unicode, got {}", total_tokens);
    let has_string = unpack_lines(&result).iter().flatten().any(|t| t.2 == "string");
    assert!(has_string, "expected at least one string highlight with unicode source");
    assert_debug_snapshot!(snapshot_of(&result));
}

#[test]
fn test_unicode_source_rust() {
    let source = r#"// 这是一个中文注释
fn main() {
    let x = "测试 emoji 🦀";
    println!("{}", x);
}
"#;
    let result: CodeParseResult = parse_code(source.to_string(), ".rs".to_string());
    let has_comment = unpack_lines(&result).iter().flatten().any(|t| t.2 == "comment");
    assert!(has_comment, "expected at least one comment highlight for Chinese comment");
    let has_string = unpack_lines(&result).iter().flatten().any(|t| t.2 == "string");
    assert!(has_string, "expected at least one string highlight for emoji string");
    assert_debug_snapshot!(snapshot_of(&result));
}

// Query编译、Filename测试

#[test]
fn test_all_queries_compile() {
    // 验证每个grammar的highlight query都能成功编译
    let extensions = [".c", ".h", ".cpp", ".hpp", ".go", ".py", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".sh", ".bash", ".zsh", ".cs", ".java", ".json", ".css", ".rs", ".toml", ".yaml", ".yml", ".ini", ".mk", ".kt", ".kts", ".swift", ".html", ".htm", ".xml", ".rb", ".php"];
    let mut failures = Vec::new();
    for ext in extensions {
        let grammar = uniffi_code_parser::lang::get_grammar(ext)
            .unwrap_or_else(|| panic!("no grammar for {}", ext));
        let names = grammar.compiled_query.capture_names();
        eprintln!("[QUERY OK] {} ({} captures)", ext, names.len());
        if names.is_empty() {
            failures.push(format!("{}: no captures", ext));
        }
    }
    assert!(failures.is_empty(), "query failures:\n{}", failures.join("\n"));
}

#[test]
fn test_filename_based_grammars() {
    use uniffi_code_parser::lang::get_grammar_by_filename;

    for name in ["Dockerfile", "Containerfile", "Makefile", "makefile", "GNUmakefile"] {
        let grammar = get_grammar_by_filename(name)
            .unwrap_or_else(|| panic!("no filename-based grammar for {}", name));
        assert!(
            !grammar.compiled_query.capture_names().is_empty(),
            "{}: query has no captures",
            name
        );
        eprintln!("[FILENAME OK] {} ({} captures)", name, grammar.compiled_query.capture_names().len());
    }
}

// Outline测试

fn outline_contains(outline: &[OutlineNode], kind: &str) -> bool {
    for node in outline {
        if node.kind == kind {
            return true;
        }
        if outline_contains(&node.children, kind) {
            return true;
        }
    }
    false
}

#[test]
fn test_outline_go() {
    let source = r#"package main

func main() {
    println("hello")
}
"#;
    let result: CodeParseResult = parse_code(source.to_string(), ".go".to_string());
    assert!(outline_contains(&result.outline, "package_clause"), "expected package_clause in Go outline");
    assert!(outline_contains(&result.outline, "function_declaration"), "expected function_declaration in Go outline");
    // 验证function_definition的name是"main"
    let has_main = result.outline.iter().any(|n| n.name == "main")
        || result.outline.iter().any(|n| n.children.iter().any(|c| c.name == "main"));
    assert!(has_main, "expected function named 'main' in Go outline");
    assert_debug_snapshot!(snapshot_of(&result));
}

#[test]
fn test_outline_python() {
    let source = r#"class Greeter:
    def greet(self):
        print("hello")
"#;
    let result: CodeParseResult = parse_code(source.to_string(), ".py".to_string());
    assert!(outline_contains(&result.outline, "class_definition"), "expected class_definition in Python outline");
    assert!(outline_contains(&result.outline, "function_definition"), "expected function_definition in Python outline");
    let has_greeter = result.outline.iter().any(|n| n.name == "Greeter");
    assert!(has_greeter, "expected class named 'Greeter' in Python outline");
    assert_debug_snapshot!(snapshot_of(&result));
}

#[test]
fn test_outline_java() {
    let source = r#"public class Main {
    public static void main(String[] args) {
    }
}
"#;
    let result: CodeParseResult = parse_code(source.to_string(), ".java".to_string());
    assert!(outline_contains(&result.outline, "class_declaration"), "expected class_declaration in Java outline");
    // Java中main方法可能是method_declaration或function_declaration
    let has_main = outline_contains(&result.outline, "method_declaration") || outline_contains(&result.outline, "function_declaration");
    assert!(has_main, "expected method/function declaration in Java outline");
    assert_debug_snapshot!(snapshot_of(&result));
}

#[test]
fn test_outline_rust() {
    let source = r#"use std::fmt;

mod utils {
    pub fn helper() -> i32 {
        42
    }
}

fn main() {
    let x = 1;
}
"#;
    let result: CodeParseResult = parse_code(source.to_string(), ".rs".to_string());
    assert!(outline_contains(&result.outline, "function_item"), "expected function_item in Rust outline");
    // Rust的mod项可能是mod_item
    let has_mod = outline_contains(&result.outline, "mod_item");
    assert!(has_mod, "expected mod_item in Rust outline");
    assert_debug_snapshot!(snapshot_of(&result));
}

/// 含跨行字符串（一条token覆盖多行）与一个空行
const SPANNING_SRC: &str = "fn main() {\n    let x = \"a\nb\";\n\n    println!(\"{}\", x);\n}\n";

#[test]
fn packed_layout_is_self_consistent() {
    let result: CodeParseResult = parse_code(SPANNING_SRC.to_string(), ".rs".to_string());
    assert_eq!(result.highlight_data.len() % 12, 0, "data 长度必须是 12 的倍数");
    assert_eq!(result.line_index.len() % 4, 0, "line_index 长度必须是 4 的倍数");

    let total = result.highlight_data.len() / 12;
    let line_count = (result.line_index.len() / 4).saturating_sub(1);
    assert!(total > 0, "样本应至少产生一条 token");

    assert_eq!(u32_at(&result.line_index, 0), 0, "第 0 行必须从 0 号 token 起");
    assert_eq!(
        u32_at(&result.line_index, line_count) as usize,
        total,
        "索引末元素必须等于 token 总数"
    );

    let mut prev = 0u32;
    for line in 0..=line_count {
        let cur = u32_at(&result.line_index, line);
        assert!(cur >= prev, "行索引单调性被破坏：第 {line} 行由 {prev} 退到 {cur}");
        prev = cur;
    }

    let rows = unpack_lines(&result);
    assert_eq!(rows.len(), line_count);
    for (line, row) in rows.iter().enumerate() {
        for t in row {
            assert!(t.0 <= t.1, "第 {line} 行出现 start > end：{t:?}");
        }
        for w in row.windows(2) {
            assert!(
                w[0].0 <= w[1].0,
                "第 {line} 行未按 start 升序：{:?} 出现在 {:?} 之后",
                w[1],
                w[0]
            );
        }
    }
    // 跨行的字符串必须被拆进它覆盖的每一行
    let lines_with_string = rows
        .iter()
        .filter(|r| r.iter().any(|t| t.2 == "string"))
        .count();
    assert!(
        lines_with_string >= 2,
        "跨行字符串应至少落在两行，实际落在 {lines_with_string} 行"
    );
}

#[test]
fn packed_layout_handles_degenerate_inputs() {
    // 空
    let empty = parse_code(String::new(), ".rs".to_string());
    assert!(empty.highlight_data.is_empty(), "空源码不应产出 token");
    assert!(unpack_lines(&empty).iter().all(|r| r.is_empty()));

    let unsupported = parse_code("let x = 1;".to_string(), ".nosuchlang".to_string());
    assert!(unsupported.highlight_data.is_empty());
    assert!(unsupported.kinds.is_empty());
    assert!(unsupported.outline.is_empty());
    assert!(unpack_lines(&unsupported).is_empty());
}
