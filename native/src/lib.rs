use streaming_iterator::StreamingIterator;
use tree_sitter::{Language, Parser, QueryCursor};
pub mod lang;
uniffi::setup_scaffolding!();

// 调试日志宏
macro_rules! debug_log {
    ($($arg:tt)*) => {
        #[cfg(debug_assertions)]
        eprintln!($($arg)*);
    };
}

// UniFFI types
#[derive(uniffi::Record, Debug)]
pub struct HighlightToken {
    pub start_byte: u64,
    pub end_byte: u64,
    pub kind: String,
}

#[derive(uniffi::Record, Debug)]
pub struct OutlineNode {
    pub kind: String,
    pub name: String,
    pub detail: String,
    pub start_byte: u64,
    pub end_byte: u64,
    pub children: Vec<OutlineNode>,
}

#[derive(uniffi::Record, Debug)]
pub struct CodeParseResult {
    // 按需返回每行的高亮token
    pub highlights_by_line: Vec<Vec<HighlightToken>>,
    pub outline: Vec<OutlineNode>,
}

struct ScanSource {
    line_boundaries: Vec<(u64, u64)>,
    /// UTF-8字节偏移转UTF-16偏移的映射表，若源文件纯ASCII则为None
    byte_to_utf16_map: Option<Vec<u32>>,
}

fn scan_source(source: &str) -> ScanSource {
    // 纯ASCII-字节偏移=UTF-16偏移
    if source.is_ascii() {
        let mut boundaries = Vec::new();
        let mut line_start: u64 = 0;
        for (i, &b) in source.as_bytes().iter().enumerate() {
            if b == b'\n' {
                let pos = (i + 1) as u64;
                boundaries.push((line_start, pos));
                line_start = pos;
            }
        }
        let len = source.len() as u64;
        if line_start < len || (line_start == len && line_start > 0) {
            boundaries.push((line_start, len));
        }
        return ScanSource {
            line_boundaries: boundaries,
            byte_to_utf16_map: None,
        };
    }

    // 非ASCII单次遍历同时构建映射表+行边界
    let n = source.len();
    let mut map = Vec::with_capacity(n + 1);
    let mut boundaries = Vec::new();
    let mut line_start: u64 = 0;
    let mut utf16_pos: u64 = 0;
    for ch in source.chars() {
        let utf16_len = ch.len_utf16() as u64;
        for _ in 0..ch.len_utf8() {
            map.push(utf16_pos as u32);
        }
        utf16_pos += utf16_len;
        if ch == '\n' {
            boundaries.push((line_start, utf16_pos));
            line_start = utf16_pos;
        }
    }
    map.push(utf16_pos as u32);
    if line_start < utf16_pos || (line_start == utf16_pos && line_start > 0) {
        boundaries.push((line_start, utf16_pos));
    }
    ScanSource {
        line_boundaries: boundaries,
        byte_to_utf16_map: Some(map),
    }
}

fn map_byte(map: &[u32], byte_pos: u64) -> u64 {
    map.get(byte_pos as usize).copied().unwrap_or(0) as u64
}

fn convert_highlights(map: Option<&[u32]>, highlights: &mut [HighlightToken]) {
    let Some(map) = map else { return };
    for h in highlights {
        h.start_byte = map_byte(map, h.start_byte);
        h.end_byte = map_byte(map, h.end_byte);
    }
}

fn convert_outline(map: Option<&[u32]>, outline: &mut [OutlineNode]) {
    let Some(map) = map else { return };
    for node in outline {
        node.start_byte = map_byte(map, node.start_byte);
        node.end_byte = map_byte(map, node.end_byte);
        convert_outline(Some(map), &mut node.children);
    }
}

fn split_highlights_by_line(
    line_boundaries: &[(u64, u64)],
    highlights: &[HighlightToken],
) -> Vec<Vec<HighlightToken>> {
    let line_count = line_boundaries.len();
    if line_count == 0 {
        return Vec::new();
    }

    // 预先分配每行的Vec
    let mut result: Vec<Vec<HighlightToken>> = (0..line_count).map(|_| Vec::new()).collect();
    let line_starts: Vec<u64> = line_boundaries.iter().map(|(s, _)| *s).collect();
    for h in highlights {
        let start_line = match line_starts.binary_search(&h.start_byte) {
            Ok(idx) => idx,
            Err(idx) => {
                if idx == 0 {
                    continue; // 在文件开头之前，不应发生
                }
                idx - 1
            }
        };
        let end_line = {
            let idx = line_starts
                .binary_search(&h.end_byte)
                .unwrap_or_else(|insertion_point| insertion_point);
            if idx == 0 {
                continue;
            }
            idx - 1
        };
        for line_idx in start_line..=end_line.min(line_count - 1) {
            let (line_start, line_end) = line_boundaries[line_idx];
            let overlap_start = h.start_byte.max(line_start);
            let overlap_end = h.end_byte.min(line_end);
            if overlap_start < overlap_end {
                let line_len = line_end.saturating_sub(line_start);
                result[line_idx].push(HighlightToken {
                    start_byte: overlap_start.saturating_sub(line_start).min(line_len),
                    end_byte: overlap_end.saturating_sub(line_start).min(line_len),
                    kind: h.kind.clone(),
                });
            }
        }
    }
    // 排序
    for tokens in &mut result {
        tokens.sort_by_key(|t| t.start_byte);
    }

    result
}

//helpers
fn extract_name(node: tree_sitter::Node, source: &[u8]) -> String {
    if let Some(name_node) = node.child_by_field_name("name") {
        name_node.utf8_text(source).unwrap_or("").to_string()
    } else {
        String::new()
    }
}

// 只有这些节点类型会生成OutlineNode
const OUTLINE_STRUCTURAL_KINDS: &[&str] = &[

    "function_definition",
    "function_declaration",
    "function_item",
    "method_definition",
    "method_declaration",
    "generator_function_declaration",

    "class_definition",
    "class_declaration",
    "class_specifier",
    "struct_declaration",
    "struct_specifier",
    "struct_item",
    "interface_declaration",
    "type_alias_declaration",
    "type_item",
    "record_declaration",
    "enum_declaration",
    "enum_specifier",
    "enum_item",
    "trait_item",
    "impl_item",
    "annotation_type_declaration",

    "namespace_definition",
    "namespace_declaration",
    "mod_item",

    "static_item",
    "const_item",
    "type_declaration",
    "package_clause",
];

/// 最大outline嵌套深度
const MAX_OUTLINE_DEPTH: usize = 16;
/// 最大outline节点总数（超出直接截断）
const MAX_OUTLINE_NODES: usize = 1000;

fn is_structural_kind(kind: &str) -> bool {
    OUTLINE_STRUCTURAL_KINDS.contains(&kind)
}

/// 结构性节点：创建OutlineNode并递归收集子节点
/// 非结构性节点：不创建节点，但继续深入子节点
fn collect_outline(
    node: tree_sitter::Node,
    source: &[u8],
    depth: usize,
    counter: &mut usize,
    out: &mut Vec<OutlineNode>,
) {
    if depth > MAX_OUTLINE_DEPTH || *counter >= MAX_OUTLINE_NODES {
        return;
    }

    if is_structural_kind(node.kind()) {
        *counter += 1;

        let mut children = Vec::new();
        collect_outline_children(node, source, depth + 1, counter, &mut children);

        out.push(OutlineNode {
            kind: node.kind().to_string(),
            name: extract_name(node, source),
            detail: String::new(),
            start_byte: node.start_byte() as u64,
            end_byte: node.end_byte() as u64,
            children,
        });
    } else {
        collect_outline_children(node, source, depth, counter, out);
    }
}

fn collect_outline_children(
    node: tree_sitter::Node,
    source: &[u8],
    depth: usize,
    counter: &mut usize,
    out: &mut Vec<OutlineNode>,
) {
    let mut cursor = node.walk();
    if cursor.goto_first_child() {
        loop {
            let child = cursor.node();
            if child.is_named() {
                collect_outline(child, source, depth, counter, out);
            }
            if !cursor.goto_next_sibling() {
                break;
            }
        }
    }
}

//exported
#[uniffi::export]
pub fn parse_code(source: String, extension: String) -> CodeParseResult {
    let source_bytes = source.as_bytes();
    // 找grammar
    let grammar = match lang::get_grammar(&extension) {
        Some(g) => g,
        None => {
            debug_log!("[RUST] unsupported extension: {}", extension);
            return CodeParseResult {
                highlights_by_line: Vec::new(),
                outline: Vec::new(),
            };
        }
    };
    let language: Language = grammar.language.clone();

    let mut parser = Parser::new();
    if parser.set_language(&language).is_err() {
        debug_log!("[RUST] failed to set language for extension: {}", extension);
        return CodeParseResult {
            highlights_by_line: Vec::new(),
            outline: Vec::new(),
        };
    }

    let tree = match parser.parse(source_bytes, None) {
        Some(t) => t,
        None => {
            debug_log!("[RUST] failed to parse source for extension: {}", extension);
            return CodeParseResult {
                highlights_by_line: Vec::new(),
                outline: Vec::new(),
            };
        }
    };

    let query = &grammar.compiled_query;

    debug_log!(
        "[RUST] parse_code called, source length={}, extension={}",
        source.len(),
        extension
    );
    debug_log!("[RUST] query capture names: {:?}", query.capture_names());

    let mut highlights = {
        let mut qc = QueryCursor::new();
        let mut results = Vec::new();
        let mut matches = qc.matches(query, tree.root_node(), source.as_bytes());
        while let Some(match_) = matches.next() {
            for capture in match_.captures {
                let node = capture.node;
                let kind_str = query.capture_names()[capture.index as usize];
                results.push(HighlightToken {
                    start_byte: node.start_byte() as u64,
                    end_byte: node.end_byte() as u64,
                    kind: kind_str.to_string(),
                });
            }
        }
        // 线性去重
        results.sort_by_key(|t| (t.start_byte, t.end_byte));
        results.dedup_by_key(|t| (t.start_byte, t.end_byte));
        debug_log!("[RUST] highlights count: {} (deduplicated)", results.len());
        for h in &results {
            debug_log!(
                "[RUST]   highlight: start={} end={} kind={:?}",
                h.start_byte, h.end_byte, h.kind
            );
        }
        results
    };

    let outline = {
        let root = tree.root_node();
        let mut top = Vec::new();
        let mut counter = 0usize;
        collect_outline_children(root, source_bytes, 0, &mut counter, &mut top);
        debug_log!(
            "[RUST] outline count: {} (max depth: {}, max nodes: {})",
            top.len(),
            MAX_OUTLINE_DEPTH,
            MAX_OUTLINE_NODES
        );
        top
    };

    let mut result = CodeParseResult {
        highlights_by_line: Vec::new(),
        outline,
    };

    // 获取UTF-16映射表和行边界
    let scan = scan_source(&source);
    convert_highlights(scan.byte_to_utf16_map.as_deref(), &mut highlights);
    convert_outline(scan.byte_to_utf16_map.as_deref(), &mut result.outline);

    result.highlights_by_line = split_highlights_by_line(&scan.line_boundaries, &highlights);

    debug_log!(
        "[RUST] returning result with {} lines of highlights",
        result.highlights_by_line.len()
    );
    result
}