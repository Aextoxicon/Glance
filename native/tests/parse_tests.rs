use insta::assert_debug_snapshot;
use uniffi_code_parser::{
    parse_code,
    CodeParseResult,
    HighlightToken,
    OutlineNode,
};

macro_rules! lang_snapshot_test {
    ($name:ident, $ext:literal, $source:literal) => {
        #[test]
        fn $name() {
            let result: CodeParseResult = parse_code($source.to_string(), $ext.to_string());
            let total_tokens: usize = result
                .highlights_by_line
                .iter()
                .map(|t: &Vec<HighlightToken>| t.len())
                .sum();
            assert!(
                total_tokens > 0,
                "{}: expected at least 1 highlight token, got 0",
                $ext
            );
            assert_debug_snapshot!(result);
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
    let total_tokens: usize = result
        .highlights_by_line
        .iter()
        .map(|t: &Vec<HighlightToken>| t.len())
        .sum();
    assert!(total_tokens > 5, "expected more than 5 tokens with unicode, got {}", total_tokens);
    let has_string = result.highlights_by_line.iter().flatten().any(|t| t.kind == "string");
    assert!(has_string, "expected at least one string highlight with unicode source");
    assert_debug_snapshot!(result);
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
    let has_comment = result.highlights_by_line.iter().flatten().any(|t| t.kind == "comment");
    assert!(has_comment, "expected at least one comment highlight for Chinese comment");
    let has_string = result.highlights_by_line.iter().flatten().any(|t| t.kind == "string");
    assert!(has_string, "expected at least one string highlight for emoji string");
    assert_debug_snapshot!(result);
}

// Query编译、Filename测试

#[test]
fn test_all_queries_compile() {
    // 验证每个grammar的highlight query都能成功编译
    let extensions = [".c", ".h", ".cpp", ".hpp", ".go", ".py", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".sh", ".bash", ".zsh", ".cs", ".java", ".json", ".css", ".rs", ".toml", ".yaml", ".yml", ".ini", ".mk", ".kt", ".kts", ".swift", ".html", ".htm"];
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
    assert_debug_snapshot!(result);
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
    assert_debug_snapshot!(result);
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
    assert_debug_snapshot!(result);
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
    assert_debug_snapshot!(result);
}