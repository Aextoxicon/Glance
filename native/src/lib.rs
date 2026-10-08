use streaming_iterator::StreamingIterator;
use tree_sitter::{Language, Parser, QueryCursor};
pub mod lang;
uniffi::setup_scaffolding!();

// 调试日志宏,未开trace时cfg!为常量false,分支被优化掉,release零输出
macro_rules! debug_log {
    ($($arg:tt)*) => {
        if cfg!(feature = "trace") {
            eprintln!($($arg)*);
        }
    };
}

// UniFFI types

/// kind是capture下标，出口才转成字符串
#[derive(Clone, Copy)]
struct Token {
    start_byte: i32,
    end_byte: i32,
    kind: u32,
}

const PACKED_TOKEN_BYTES: usize = 12;

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
    /// 每12字节一条：[start:u32][end:u32][kind:u32]
    pub highlight_data: Vec<u8>,
    pub line_index: Vec<u8>,
    /// 下标即token的kind
    pub kinds: Vec<String>,
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

fn convert_highlights(map: Option<&[u32]>, highlights: &mut [Token]) {
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

/// None表示它落在任何行之外
fn line_span_of(token: &Token, line_starts: &[u64], line_count: usize) -> Option<(usize, usize)> {
    let start = token.start_byte as u64;
    let end = token.end_byte as u64;
    let start_line = match line_starts.binary_search(&start) {
        Ok(idx) => idx,
        Err(idx) => {
            if idx == 0 {
                return None;
            }
            idx - 1
        }
    };
    let end_line = {
        let idx = line_starts
            .binary_search(&end)
            .unwrap_or_else(|insertion_point| insertion_point);
        if idx == 0 {
            return None;
        }
        idx - 1
    };
    Some((start_line, end_line.min(line_count - 1)))
}

/// 按行打包高亮。两趟：先数每行几条，再写入 —— 不为每行建Vec，也不产生String。
fn pack_highlights_by_line(
    line_boundaries: &[(u64, u64)],
    highlights: &[Token],
) -> (Vec<u8>, Vec<u8>) {
    let line_count = line_boundaries.len();
    let mut line_index = vec![0u8; (line_count + 1) * 4];
    if line_count == 0 || highlights.is_empty() {
        return (Vec::new(), line_index);
    }
    let line_starts: Vec<u64> = line_boundaries.iter().map(|(s, _)| *s).collect();

    let mut counts = vec![0u32; line_count];
    let mut total = 0usize;
    for h in highlights {
        let (start, end) = (h.start_byte as u64, h.end_byte as u64);
        let Some((start_line, end_line)) = line_span_of(h, &line_starts, line_count) else {
            continue;
        };
        for line_idx in start_line..=end_line {
            let (line_start, line_end) = line_boundaries[line_idx];
            if start.max(line_start) < end.min(line_end) {
                counts[line_idx] += 1;
                total += 1;
            }
        }
    }

    let mut starts: Vec<u32> = Vec::with_capacity(line_count + 1);
    let mut acc = 0u32;
    for c in &counts {
        starts.push(acc);
        acc += *c;
    }
    starts.push(acc);

    // 写入扁平缓冲
    let mut data = vec![0u8; total * PACKED_TOKEN_BYTES];
    let mut cursors = starts[..line_count].to_vec();
    for h in highlights {
        let (start, end) = (h.start_byte as u64, h.end_byte as u64);
        let Some((start_line, end_line)) = line_span_of(h, &line_starts, line_count) else {
            continue;
        };
        for line_idx in start_line..=end_line {
            let (line_start, line_end) = line_boundaries[line_idx];
            let overlap_start = start.max(line_start);
            let overlap_end = end.min(line_end);
            if overlap_start >= overlap_end {
                continue;
            }
            let line_len = line_end.saturating_sub(line_start);
            let rel_start = overlap_start.saturating_sub(line_start).min(line_len) as u32;
            let rel_end = overlap_end.saturating_sub(line_start).min(line_len) as u32;
            let off = cursors[line_idx] as usize * PACKED_TOKEN_BYTES;
            data[off..off + 4].copy_from_slice(&rel_start.to_le_bytes());
            data[off + 4..off + 8].copy_from_slice(&rel_end.to_le_bytes());
            data[off + 8..off + PACKED_TOKEN_BYTES].copy_from_slice(&h.kind.to_le_bytes());
            cursors[line_idx] += 1;
        }
    }

    debug_assert!(
        cursors
            .iter()
            .enumerate()
            .all(|(i, c)| *c == starts[i + 1]),
        "游标未走到下一行起点，两趟统计不一致"
    );

    for (i, s) in starts.iter().enumerate() {
        let off = i * 4;
        line_index[off..off + 4].copy_from_slice(&s.to_le_bytes());
    }

    (data, line_index)
}

//helpers

/// 标识符类节点，穷举常见的几种
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

/// 沿declarator链下钻找真正的标识符
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
pub const OUTLINE_STRUCTURAL_KINDS: &[&str] = &[

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

const MAX_OUTLINE_DEPTH: usize = 16;
/// 超出直接截断
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
    kind: u32,
    score: u16,
}

// 结构具体度高256倍于capture名点分段数，两级同时生效
fn capture_score(specificity: u8, dotted: u8) -> u16 {
    (u16::from(specificity) << 8) | u16::from(dotted)
}

// 解决高亮token的冲突
fn resolve_capture_conflicts(mut tokens: Vec<RawCapture>) -> Vec<Token> {
    tokens.sort_by(|a, b| {
        a.start_byte
            .cmp(&b.start_byte)
            .then_with(|| b.end_byte.cmp(&a.end_byte))
            .then_with(|| b.score.cmp(&a.score))
    });
    tokens.dedup_by(|a, b| {
        a.start_byte == b.start_byte && a.end_byte == b.end_byte && a.kind == b.kind
    });

    struct Open {
        cursor: u64,
        end: u64,
        kind: u32,
    }
    let mut out: Vec<Token> = Vec::with_capacity(tokens.len());
    let mut stack: Vec<Open> = Vec::new();

    fn close_top(stack: &mut Vec<Open>, out: &mut Vec<Token>) {
        if let Some(top) = stack.pop() {
            if top.cursor < top.end {
                out.push(Token {
                    start_byte: top.cursor as i32,
                    end_byte: top.end as i32,
                    kind: top.kind,
                });
            }
            if let Some(next) = stack.last_mut() {
                if next.cursor < top.end {
                    next.cursor = top.end;
                }
            }
        }
    }

    for t in tokens {
        // 关掉所有在t起点之前（含）就结束的外层
        while stack.last().map_or(false, |top| top.end <= t.start_byte) {
            close_top(&mut stack, &mut out);
        }

        if let Some(top) = stack.last() {
            // 区间完全相同：已按具体度降序，栈顶胜出，直接丢弃t
            if top.cursor == t.start_byte && top.end == t.end_byte {
                continue;
            }
        }

        if let Some(top) = stack.last_mut() {
            if top.end > t.end_byte {
                // 先吐出top在t之前的那一段
                if top.cursor < t.start_byte {
                    out.push(Token {
                        start_byte: top.cursor as i32,
                        end_byte: t.start_byte as i32,
                        kind: top.kind,
                    });
                    top.cursor = t.start_byte;
                }
            } else {
                // 交叉重叠（top在t内部结束）
                if top.cursor < t.start_byte {
                    out.push(Token {
                        start_byte: top.cursor as i32,
                        end_byte: t.start_byte as i32,
                        kind: top.kind,
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
pub fn parse_code(source: String, filename: String) -> CodeParseResult {
    let source_bytes = source.as_bytes();
    // 先按文件名，再按扩展名路由
    let grammar = match lang::resolve_grammar(&filename) {
        Some(g) => g,
        None => {
            debug_log!("[RUST] unsupported file: {}", filename);
            return CodeParseResult {
                highlight_data: Vec::new(),
                line_index: Vec::new(),
                kinds: Vec::new(),
                outline: Vec::new(),
            };
        }
    };
    let language: Language = grammar.language.clone();

    let mut parser = Parser::new();
    if parser.set_language(&language).is_err() {
        debug_log!("[RUST] failed to set language for file: {}", filename);
        return CodeParseResult {
            highlight_data: Vec::new(),
            line_index: Vec::new(),
            kinds: Vec::new(),
            outline: Vec::new(),
        };
    }

    let tree = match parser.parse(source_bytes, None) {
        Some(t) => t,
        None => {
            debug_log!("[RUST] failed to parse source for file: {}", filename);
            return CodeParseResult {
                highlight_data: Vec::new(),
                line_index: Vec::new(),
                kinds: Vec::new(),
                outline: Vec::new(),
            };
        }
    };

    let query = &grammar.compiled_query;
    let names: &[&str] = query.capture_names();

    debug_log!(
        "[RUST] parse_code called, source length={}, filename={}",
        source.len(),
        filename
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
                let dotted = grammar
                    .capture_dotted
                    .get(capture.index as usize)
                    .copied()
                    .unwrap_or(0);
                results.push(RawCapture {
                    start_byte: node.start_byte() as u64,
                    end_byte: node.end_byte() as u64,
                    kind: capture.index,
                    score: capture_score(specificity, dotted),
                });
            }
        }
        let results = resolve_capture_conflicts(results);
        debug_log!("[RUST] highlights count: {} (deduplicated)", results.len());
        for h in &results {
            debug_log!(
                "[RUST]   highlight: start={} end={} kind={:?}",
                h.start_byte,
                h.end_byte,
                names[h.kind as usize]
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
        highlight_data: Vec::new(),
        line_index: Vec::new(),
        kinds: names.iter().map(|n| (*n).to_string()).collect(),
        outline,
    };

    // 获取UTF-16映射表和行边界
    let scan = scan_source(&source);
    convert_highlights(scan.byte_to_utf16_map.as_deref(), &mut highlights);
    convert_outline(scan.byte_to_utf16_map.as_deref(), &mut result.outline);

    let (data, line_index) = pack_highlights_by_line(&scan.line_boundaries, &highlights);
    result.highlight_data = data;
    result.line_index = line_index;

    debug_log!(
        "[RUST] returning {} packed tokens over {} lines (kinds={})",
        result.highlight_data.len() / PACKED_TOKEN_BYTES,
        result.line_index.len() / 4 - 1,
        result.kinds.len()
    );
    result
}