use std::sync::LazyLock;

// 消除每个语言文件中的模板代码（LazyLock + GrammarDef构造）
// 用法：
//   grammar!(tree_sitter_go::LANGUAGE, HIGHLIGHT_QUERY)
// 多段 query 按顺序拼接（例如 CPP = C + C++ 增量）
macro_rules! grammar {
    ($lang:expr, $($seg:expr),+ $(,)?) => {
        ::std::sync::LazyLock::new(|| {
            let segments: &[&str] = &[$($seg),+];
            $crate::lang::build_grammar($lang.into(), segments)
        })
    };
}

// 编译 query（多段按换行拼接）并算出每条 pattern 的结构具体度
pub fn build_grammar(language: tree_sitter::Language, segments: &[&str]) -> GrammarDef {
    let highlight = segments.join("\n");
    let compiled_query = tree_sitter::Query::new(&language, &highlight)
        .expect("invalid highlight query");
    let pattern_specificity = compute_pattern_specificity(&highlight, &compiled_query);
    GrammarDef {
        language,
        compiled_query,
        pattern_specificity,
    }
}

// 数出 query 文本里的节点模式个数：语法里 `(` 只开节点模式，开谓词时紧跟 `#`
fn count_pattern_nodes(text: &str) -> usize {
    let bytes = text.as_bytes();
    let mut nodes = 0usize;
    let mut in_string = false;
    let mut escaped = false;
    for i in 0..bytes.len() {
        if in_string {
            match bytes[i] {
                b'\\' => escaped = !escaped,
                b'"' if !escaped => in_string = false,
                _ => {}
            }
            continue;
        }
        match bytes[i] {
            b'"' => in_string = true,
            b'(' => {
                if !text[i + 1..].trim_start().starts_with('#') {
                    nodes += 1;
                }
            }
            _ => {}
        }
    }
    nodes
}

// 每条 pattern 的结构具体度 = 节点模式数 + 谓词约束数；节点越多越具体
pub fn compute_pattern_specificity(query_text: &str, query: &tree_sitter::Query) -> Vec<u8> {
    (0..query.pattern_count())
        .map(|i| {
            let start = query.start_byte_for_pattern(i);
            let end = query.end_byte_for_pattern(i).min(query_text.len());
            let text = query_text.get(start..end).unwrap_or("");
            let predicates = query.general_predicates(i).len()
                + query.property_predicates(i).len()
                + query.property_settings(i).len();
            (count_pattern_nodes(text) + predicates).min(u8::MAX as usize) as u8
        })
        .collect()
}

mod c;
mod cpp;
mod go;
mod python;
mod javascript;
mod typescript;
mod bash;
mod csharp;
mod java;
mod json;
mod css;
mod rust;
mod toml;
mod yaml;
mod ini;
mod make;
mod containerfile;
mod kotlin;
mod swift;
mod html;

pub struct GrammarDef {
    pub language: tree_sitter::Language,
    /// 预编译的Query
    pub compiled_query: tree_sitter::Query,
    pub pattern_specificity: Vec<u8>,
}

// 按文件扩展名查找对应的grammar定义
pub fn get_grammar(ext: &str) -> Option<&'static LazyLock<GrammarDef>> {
    match ext {
        ".c" => Some(&c::GRAMMAR),
        ".h" => Some(&c::GRAMMAR),
        ".cpp" | ".cc" | ".cxx" => Some(&cpp::GRAMMAR),
        ".hpp" => Some(&cpp::GRAMMAR),
        ".go" => Some(&go::GRAMMAR),
        ".py" => Some(&python::GRAMMAR),
        ".js" | ".mjs" | ".cjs" | ".jsx" => Some(&javascript::GRAMMAR),
        ".ts" | ".mts" | ".cts" => Some(&typescript::GRAMMAR_TS),
        ".tsx" => Some(&typescript::GRAMMAR_TSX),
        ".gradle" => Some(&kotlin::GRAMMAR),
        ".sh" | ".bash" | ".zsh" => Some(&bash::GRAMMAR),
        ".cs" => Some(&csharp::GRAMMAR),
        ".java" => Some(&java::GRAMMAR),
        ".json" => Some(&json::GRAMMAR),
        ".css" => Some(&css::GRAMMAR),
        ".rs" => Some(&rust::GRAMMAR),
        ".toml" => Some(&toml::GRAMMAR),
        ".yaml" | ".yml" => Some(&yaml::GRAMMAR),
        ".ini" => Some(&ini::GRAMMAR),
        ".mk" | ".makefile" => Some(&make::GRAMMAR),
        ".kt" | ".kts" => Some(&kotlin::GRAMMAR),
        ".dockerfile" => Some(&containerfile::GRAMMAR),
        ".swift" => Some(&swift::GRAMMAR),
        ".html" | ".htm" => Some(&html::GRAMMAR),
        _ => None,
    }
}

pub fn get_grammar_by_filename(filename: &str) -> Option<&'static LazyLock<GrammarDef>> {
    match filename {
        "Makefile" | "makefile" | "GNUmakefile" => Some(&make::GRAMMAR),
        "Dockerfile" | "Containerfile" => Some(&containerfile::GRAMMAR),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const GO_CJK: &str = r#"
        (identifier) @variable
        (function_declaration
          name: (identifier) @function)
        ((identifier) @constant
          (#match? @constant "^[A-Z][A-Z0-9_]*$"))
    "#;

    #[test]
    fn nested_pattern_beats_flat_catchall() {
        let def = build_grammar(tree_sitter_go::LANGUAGE.into(), &[GO_CJK]);
        let q = &def.compiled_query;
        assert_eq!(def.pattern_specificity.len(), q.pattern_count());
        // 嵌套节点越多越具体
        assert!(def.pattern_specificity[1] > def.pattern_specificity[0]);
        // 带谓词的约束更具体
        assert!(def.pattern_specificity[2] > def.pattern_specificity[0]);
    }

    #[test]
    fn predicate_counts_as_constraint() {
        let lang = tree_sitter_c::LANGUAGE.into();
        let query = tree_sitter::Query::new(
            &lang,
            "((identifier) @constant (#match? @constant \"^[A-Z]$\"))",
        )
        .unwrap();
        let spec = compute_pattern_specificity(
            "((identifier) @constant (#match? @constant \"^[A-Z]$\"))",
            &query,
        );
        assert_eq!(spec, vec![2]);
    }
}
