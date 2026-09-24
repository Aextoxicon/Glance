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
    // 行内偏移不超过行长
    pub start_byte: i32,
    pub end_byte: i32,
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
        h.start_byte = map_byte(map, h.start_byte as u64) as i32;
        h.end_byte = map_byte(map, h.end_byte as u64) as i32;
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

    // 仅预留容量，空行不占用容器
    let mut result: Vec<Vec<HighlightToken>> = Vec::with_capacity(line_count);
    let line_starts: Vec<u64> = line_boundaries.iter().map(|(s, _)| *s).collect();
    for h in highlights {
        let start = h.start_byte as u64;w
        let end = h.end_byte as u64;
        let start_line = match line_starts.binary_search(&start) {
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
                .binary_search(&end)
                .unwrap_or_else(|insertion_point| insertion_point);
            if idx == 0 {
                continue;
            }
            idx - 1
        };
        for line_idx in start_line..=end_line.min(line_count - 1) {
            let (line_start, line_end) = line_boundaries[line_idx];
            let overlap_start = start.max(line_start);
            let overlap_end = end.min(line_end);
            if overlap_start < overlap_end {
                // 惰性补齐到目标行
                while result.len() <= line_idx {
                    result.push(Vec::new());
                }
                let line_len = line_end.saturating_sub(line_start);
                result[line_idx].push(HighlightToken {
                    start_byte: overlap_start.saturating_sub(line_start).min(line_len) as i32,
                    end_byte: overlap_end.saturating_sub(line_start).min(line_len) as i32,
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

/// 标识符类节点，跨 grammar 的名字节点类型并不统一，穷举常见的几种
const IDENTIFIER_KINDS: &[&str] = &[
    "identifier",
    "type_identifier",
    "simple_identifier",
    "package_identifier",
    "field_identifier",
    "qualified_identifier",
];

fn is_identifier_kind(kind: &str) -> bool {
    IDENTIFIER_KINDS.contains(&kind)
}

fn text_of(node: tree_sitter::Node, source: &[u8]) -> String {
    node.utf8_text(source).unwrap_or("").to_string()
}

fn find_child_of_kind<'a>(
    node: tree_sitter::Node<'a>,
    kinds: &[&str],
) -> Option<tree_sitter::Node<'a>> {
    let mut cursor = node.walk();
    let found = node
        .named_children(&mut cursor)
        .find(|child| kinds.contains(&child.kind()));
    found
}

fn first_identifier_child(node: tree_sitter::Node, source: &[u8]) -> Option<String> {
    let mut cursor = node.walk();
    let found = node
        .named_children(&mut cursor)
        .find(|child| is_identifier_kind(child.kind()))
        .map(|child| text_of(child, source));
    found
}

/// 沿 declarator 链下钻找真正的标识符
fn declarator_identifier(node: tree_sitter::Node, source: &[u8]) -> Option<String> {
    if is_identifier_kind(node.kind()) {
        return Some(text_of(node, source));
    }
    let mut cursor = node.walk();
    for child in node.named_children(&mut cursor) {
        if let Some(name) = declarator_identifier(child, source) {
            return Some(name);
        }
    }
    None
}

fn extract_name(node: tree_sitter::Node, source: &[u8]) -> String {
    if let Some(name_node) = node.child_by_field_name("name") {
        return text_of(name_node, source);
    }

    match node.kind() {
        "function_definition" | "declaration" => {
            if let Some(declarator) = node.child_by_field_name("declarator") {
                if let Some(name) = declarator_identifier(declarator, source) {
                    return name;
                }
            }
        }
        "type_declaration" => {
            if let Some(name) = find_child_of_kind(node, &["type_spec"])
                .and_then(|spec| spec.child_by_field_name("name"))
            {
                return text_of(name, source);
            }
        }
        _ => {}
    }

    first_identifier_child(node, source).unwrap_or_default()
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

struct RawCapture {
    start_byte: u64,
    end_byte: u64,
    kind: String,
    specificity: u8,
}

// 具体度打分：结构具体度高 256 倍于 capture 名点分段数，两级同时生效
fn capture_score(t: &RawCapture) -> u16 {
    (u16::from(t.specificity) << 8) | (t.kind.split('.').count() as u16)
}

// 解决高亮token的冲突：同区间按具体度择优，嵌套区间切开成互不重叠的
fn resolve_capture_conflicts(mut tokens: Vec<RawCapture>) -> Vec<HighlightToken> {
    tokens.sort_by(|a, b| {
        a.start_byte
            .cmp(&b.start_byte)
            .then_with(|| b.end_byte.cmp(&a.end_byte))
            .then_with(|| capture_score(b).cmp(&capture_score(a)))
    });
    tokens.dedup_by(|a, b| {
        a.start_byte == b.start_byte && a.end_byte == b.end_byte && a.kind == b.kind
    });

    struct Open {
        cursor: u64,
        end: u64,
        kind: String,
    }
    let mut out: Vec<HighlightToken> = Vec::with_capacity(tokens.len());
    let mut stack: Vec<Open> = Vec::new();

    fn close_top(stack: &mut Vec<Open>, out: &mut Vec<HighlightToken>) {
        if let Some(top) = stack.pop() {
            if top.cursor < top.end {
                out.push(HighlightToken {
                    start_byte: top.cursor as i32,
                    end_byte: top.end as i32,
                    kind: top.kind,
                });
            }
            // 外层跳过刚被内层占用的那一段
            if let Some(next) = stack.last_mut() {
                if next.cursor < top.end {
                    next.cursor = top.end;
                }
            }
        }
    }

    for t in tokens {
        // 关掉所有在 t 起点之前（含）就结束的外层
        while stack.last().map_or(false, |top| top.end <= t.start_byte) {
            close_top(&mut stack, &mut out);
        }

        if let Some(top) = stack.last() {
            // 区间完全相同：已按具体度降序，栈顶胜出，直接丢弃 t
            if top.cursor == t.start_byte && top.end == t.end_byte {
                continue;
            }
        }

        if let Some(top) = stack.last_mut() {
            if top.end > t.end_byte {
                // 先吐出 top 在 t 之前的那一段
                if top.cursor < t.start_byte {
                    out.push(HighlightToken {
                        start_byte: top.cursor as i32,
                        end_byte: t.start_byte as i32,
                        kind: top.kind.clone(),
                    });
                    top.cursor = t.start_byte;
                }
            } else {
                // 交叉重叠（top 在 t 内部结束）
                if top.cursor < t.start_byte {
                    out.push(HighlightToken {
                        start_byte: top.cursor as i32,
                        end_byte: t.start_byte as i32,
                        kind: top.kind.clone(),
                    });
                }
                stack.pop();
            }
        }

        stack.push(Open {
            cursor: t.start_byte,
            end: t.end_byte,
            kind: t.kind,
        });
    }
    while !stack.is_empty() {
        close_top(&mut stack, &mut out);
    }

    out
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
        let mut results: Vec<RawCapture> = Vec::new();
        let mut matches = qc.matches(query, tree.root_node(), source.as_bytes());
        while let Some(match_) = matches.next() {
            let specificity = grammar
                .pattern_specificity
                .get(match_.pattern_index)
                .copied()
                .unwrap_or(0);
            for capture in match_.captures {
                let node = capture.node;
                let kind_str = query.capture_names()[capture.index as usize];
                results.push(RawCapture {
                    start_byte: node.start_byte() as u64,
                    end_byte: node.end_byte() as u64,
                    kind: kind_str.to_string(),
                    specificity,
                });
            }
        }
        let results = resolve_capture_conflicts(results);
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