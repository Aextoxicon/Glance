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

// UniFFI类型

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

/// 按行打包高亮。两趟：先数每行几条，再写入 —— 不为每行建Vec，也不产生String
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

// 辅助函数

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
        "rule" => {
            // 静态模式规则(%.o:%.c)的target
            if let Some(target) = node.child_by_field_name("target") {
                return text_of(target, source);
            }
            // 普通规则的target
            if let Some(targets) = find_child_of_kind(node, &["targets"]) {
                return text_of(targets, source);
            }
        }
        "from_instruction" => {
            if let Some(alias) = node.child_by_field_name("as") {
                return text_of(alias, source);
            }
            if let Some(name) = find_child_of_kind(node, &["image_spec"])
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

    // Ruby
    "class",
    "module",
    "method",
    "singleton_method",
    // Makefile
    "rule",
    "define_directive",
    // Dockerfile
    "from_instruction",
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

// 导出接口
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

use std::io::Cursor;
use std::collections::HashMap;
use acadrust::entities::{
    AttachmentPoint, EntityType, HorizontalAlignment, TextHorizontalAlignment, TextVerticalAlignment,
    VerticalAlignment,
};
use acadrust::Entity;
use acadrust::types::{BoundingBox3D, Color, Matrix3, Vector3};
use acadrust::tables::Layer;
use acadrust::document::CadDocument;
use acadrust::io::dxf::{DxfReader, DxfReaderConfiguration};
use acadrust::io::dwg::{DwgReader, DwgReadOptions};

// 几何层,每类型一条连续buffer
#[derive(uniffi::Record, Default, Debug)]
pub struct CadGeometry {
    // sx,sy,sz,ex,ey,ez,layer_idx
    pub line_data: Vec<f64>,
    // cx,cy,cz,r,nx,ny,nz,layer_idx(WCS中心/半径/法向)
    pub circle_data: Vec<f64>,
    // sx,sy,sz,ex,ey,ez,cx,cy,cz,r,nx,ny,nz,layer_idx(起止点/圆心/半径/法向)
    pub arc_data: Vec<f64>,
    // cx,cy,cz,majx,majy,majz,ratio,start_param,end_param,nx,ny,nz,layer_idx
    pub ellipse_data: Vec<f64>,
    // 每顶点x,y,z,bulge展平(bulge=tan(包含角/4),正为逆时针)
    pub polyline_data: Vec<f64>,
    // 每条多段线顶点数,与polyline_layers平行
    pub polyline_counts: Vec<u32>,
    // 每条多段线一个layer_idx
    pub polyline_layers: Vec<u32>,
    // 每条多段线是否闭合,与polyline_counts平行
    pub polyline_closed: Vec<bool>,
    // 每条图元一个0xRRGGBB,0表示未定(按主题前景色)
    pub line_colors: Vec<u32>,
    pub circle_colors: Vec<u32>,
    pub arc_colors: Vec<u32>,
    pub ellipse_colors: Vec<u32>,
    pub polyline_colors: Vec<u32>,
    // 每条文字:x,y,z,height,rotation,halign(0左1中2右),valign(0基线1下2中3上),layer_idx
    pub text_data: Vec<f64>,
    // 与text_data平行,每条一个内容
    pub text_values: Vec<String>,
    pub text_colors: Vec<u32>,
    pub layer_names: Vec<String>,
    // 整图包围盒
    pub bounds_min: Vec<f64>,
    pub bounds_max: Vec<f64>,
}

struct Bounds {
    min_x: f64, min_y: f64, min_z: f64,
    max_x: f64, max_y: f64, max_z: f64,
    valid: bool,
}
impl Bounds {
    fn new() -> Self {
        Bounds { min_x:0.0,min_y:0.0,min_z:0.0,max_x:0.0,max_y:0.0,max_z:0.0,valid:false }
    }
    fn expand(&mut self, x: f64, y: f64, z: f64) {
        if !self.valid {
            self.min_x=x;self.min_y=y;self.min_z=z;
            self.max_x=x;self.max_y=y;self.max_z=z;
            self.valid=true;
        } else {
            self.min_x=self.min_x.min(x);self.min_y=self.min_y.min(y);self.min_z=self.min_z.min(z);
            self.max_x=self.max_x.max(x);self.max_y=self.max_y.max(y);self.max_z=self.max_z.max(z);
        }
    }
}

fn get_layer(map: &mut HashMap<String, u32>, names: &mut Vec<String>, layer: &str) -> u32 {
    if let Some(&i) = map.get(layer) {
        i
    } else {
        let i = names.len() as u32;
        names.push(layer.to_string());
        map.insert(layer.to_string(), i);
        i
    }
}

// ByLayer/ByBlock/None落到图层色,explode已把ByBlock继承成INSERT的颜色
fn resolve_rgb(color: &Color, layer: Option<&Layer>) -> u32 {
    fn pack((r, g, b): (u8, u8, u8)) -> u32 {
        ((r as u32) << 16) | ((g as u32) << 8) | b as u32
    }
    // ACI7是白/黑自适应色,返回0让UI按主题取前景色,否则浅色主题下画成白色看不见
    if color.index() == Some(7) {
        return 0;
    }
    if let Some(rgb) = color.rgb() {
        return pack(rgb);
    }
    if let Some(l) = layer {
        if l.color.index() == Some(7) {
            return 0;
        }
        if let Some(rgb) = l.color.rgb() {
            return pack(rgb);
        }
    }
    0
}

fn text_halign(h: TextHorizontalAlignment) -> f64 {
    match h {
        TextHorizontalAlignment::Center | TextHorizontalAlignment::Middle => 1.0,
        TextHorizontalAlignment::Right => 2.0,
        _ => 0.0,
    }
}

fn text_valign(v: TextVerticalAlignment) -> f64 {
    match v {
        TextVerticalAlignment::Baseline => 0.0,
        TextVerticalAlignment::Bottom => 1.0,
        TextVerticalAlignment::Middle => 2.0,
        TextVerticalAlignment::Top => 3.0,
    }
}

fn attr_halign(h: HorizontalAlignment) -> f64 {
    match h {
        HorizontalAlignment::Center | HorizontalAlignment::Middle => 1.0,
        HorizontalAlignment::Right => 2.0,
        _ => 0.0,
    }
}

fn attr_valign(v: VerticalAlignment) -> f64 {
    match v {
        VerticalAlignment::Baseline => 0.0,
        VerticalAlignment::Bottom => 1.0,
        VerticalAlignment::Middle => 2.0,
        VerticalAlignment::Top => 3.0,
    }
}

// MText的9宫格附着点归一成水平/垂直码
fn mtext_align(a: AttachmentPoint) -> (f64, f64) {
    use AttachmentPoint::*;
    let h = match a {
        TopLeft | MiddleLeft | BottomLeft => 0.0,
        TopCenter | MiddleCenter | BottomCenter => 1.0,
        _ => 2.0,
    };
    let v = match a {
        TopLeft | TopCenter | TopRight => 3.0,
        MiddleLeft | MiddleCenter | MiddleRight => 2.0,
        _ => 1.0,
    };
    (h, v)
}

// 非左对齐时用对齐点当基准,零向量说明没设
fn pick_anchor(alignment: Option<Vector3>, insertion: Vector3) -> Vector3 {
    match alignment {
        Some(p) if p.x != 0.0 || p.y != 0.0 => p,
        _ => insertion,
    }
}

fn pick_anchor_v(alignment: Vector3, insertion: Vector3) -> Vector3 {
    if alignment.x != 0.0 || alignment.y != 0.0 {
        alignment
    } else {
        insertion
    }
}

// MText的\P是硬回车,其余格式码暂不处理
fn clean_mtext(value: &str) -> String {
    value.replace("\\P", "\n").replace("\\p", "\n")
}

#[allow(clippy::too_many_arguments)]
fn push_text(
    g: &mut CadGeometry,
    b: &mut Bounds,
    anchor: Vector3,
    height: f64,
    rotation: f64,
    halign: f64,
    valign: f64,
    layer_idx: u32,
    rgb: u32,
    value: &str,
    bb: BoundingBox3D,
) {
    if value.trim().is_empty() || height <= 0.0 {
        return;
    }
    g.text_data.extend_from_slice(&[
        anchor.x, anchor.y, anchor.z, height, rotation, halign, valign, layer_idx as f64,
    ]);
    g.text_values.push(value.to_string());
    g.text_colors.push(rgb);
    b.expand(bb.min.x, bb.min.y, bb.min.z);
    b.expand(bb.max.x, bb.max.y, bb.max.z);
}

// 隐藏实体与关闭/冻结图层上的实体不该出现在预览里
fn entity_visible(e: &EntityType, doc: &CadDocument) -> bool {
    let common = e.as_entity().common();
    if common.invisible {
        return false;
    }
    doc.layers.get(&common.layer).map(|l| l.is_visible()).unwrap_or(true)
}

#[allow(clippy::too_many_arguments)]
fn process_entity(
    e: &EntityType,
    doc: &CadDocument,
    map: &mut HashMap<String, u32>,
    g: &mut CadGeometry,
    b: &mut Bounds,
) {
    if !entity_visible(e, doc) {
        return;
    }
    if let EntityType::Insert(insert) = e {
        // explode()按array_transforms生成每个阵列单元,explode_from_document()按CAD的EXPLODE规则直接拒绝MINSERT
        let Some(parts) = insert.block_entities_from_document(doc) else {
            return;
        };
        for child in insert.explode(&parts) {
            process_entity(&child, doc, map, g, b);
        }
        return;
    }
    let layer = e.as_entity().common().layer.clone();
    let li = get_layer(map, &mut g.layer_names, &layer);
    let rgb = resolve_rgb(&e.as_entity().common().color, doc.layers.get(&layer));

    match e {
        EntityType::Line(l) => {
            let (sx, sy, sz) = (l.start.x, l.start.y, l.start.z);
            let (ex, ey, ez) = (l.end.x, l.end.y, l.end.z);
            g.line_data.extend_from_slice(&[sx, sy, sz, ex, ey, ez, li as f64]);
            g.line_colors.push(rgb);
            b.expand(sx, sy, sz);
            b.expand(ex, ey, ez);
        }
        EntityType::Circle(c) => {
            let center = c.center_wcs();
            g.circle_data.extend_from_slice(&[center.x, center.y, center.z, c.radius, c.normal.x, c.normal.y, c.normal.z, li as f64]);
            g.circle_colors.push(rgb);
            let bb = c.bounding_box();
            b.expand(bb.min.x, bb.min.y, bb.min.z);
            b.expand(bb.max.x, bb.max.y, bb.max.z);
        }
        EntityType::Arc(a) => {
            let s = a.start_point_wcs();
            let e = a.end_point_wcs();
            let c = a.center_wcs();
            g.arc_data.extend_from_slice(&[s.x, s.y, s.z, e.x, e.y, e.z, c.x, c.y, c.z, a.radius, a.normal.x, a.normal.y, a.normal.z, li as f64]);
            g.arc_colors.push(rgb);
            let bb = a.bounding_box();
            b.expand(bb.min.x, bb.min.y, bb.min.z);
            b.expand(bb.max.x, bb.max.y, bb.max.z);
        }
        EntityType::Ellipse(el) => {
            // acadrust的Ellipse存的就是WCS,再乘basis会二次变换
            g.ellipse_data.extend_from_slice(&[
                el.center.x, el.center.y, el.center.z,
                el.major_axis.x, el.major_axis.y, el.major_axis.z,
                el.minor_axis_ratio, el.start_parameter, el.end_parameter,
                el.normal.x, el.normal.y, el.normal.z, li as f64,
            ]);
            g.ellipse_colors.push(rgb);
            let bb = el.bounding_box();
            b.expand(bb.min.x, bb.min.y, bb.min.z);
            b.expand(bb.max.x, bb.max.y, bb.max.z);
        }
        EntityType::LwPolyline(p) => {
            let basis = Matrix3::arbitrary_axis(p.normal);
            let count = p.vertices.len() as u32;
            for v in &p.vertices {
                let loc = basis * Vector3::new(v.location.x, v.location.y, p.elevation);
                g.polyline_data.extend_from_slice(&[loc.x, loc.y, loc.z, v.bulge]);
                b.expand(loc.x, loc.y, loc.z);
            }
            g.polyline_counts.push(count);
            g.polyline_layers.push(li);
            g.polyline_closed.push(p.is_closed);
            g.polyline_colors.push(rgb);
        }
        EntityType::Text(t) => {
            push_text(g, b, pick_anchor(t.alignment_point, t.insertion_point), t.height, t.rotation,
                text_halign(t.horizontal_alignment), text_valign(t.vertical_alignment),
                li, rgb, &t.value, t.bounding_box());
        }
        EntityType::MText(m) => {
            let (halign, valign) = mtext_align(m.attachment_point);
            let value = clean_mtext(&m.value);
            push_text(g, b, m.insertion_point, m.height, m.rotation, halign, valign,
                li, rgb, &value, m.bounding_box());
        }
        // 块属性定义:没填默认值时用tag当可见文字
        EntityType::AttributeDefinition(d) => {
            let value = if d.default_value.trim().is_empty() { d.tag.as_str() } else { d.default_value.as_str() };
            push_text(g, b, pick_anchor_v(d.alignment_point, d.insertion_point), d.height, d.rotation,
                attr_halign(d.horizontal_alignment), attr_valign(d.vertical_alignment),
                li, rgb, value, d.bounding_box());
        }
        EntityType::AttributeEntity(a) => {
            push_text(g, b, pick_anchor_v(a.alignment_point, a.insertion_point), a.height, a.rotation,
                attr_halign(a.horizontal_alignment), attr_valign(a.vertical_alignment),
                li, rgb, &a.value, a.bounding_box());
        }
        _ => {}
    }
}

#[uniffi::export]
pub fn parse_cad_geometry(file_bytes: Vec<u8>, filename: String) -> CadGeometry {
    let lower = filename.to_ascii_lowercase();
    let is_dwg = lower.ends_with(".dwg");
    let is_dxf = lower.ends_with(".dxf");
    if !is_dwg && !is_dxf {
        return CadGeometry::default();
    }
    let doc = if is_dwg {
        let mut reader = DwgReader::from_stream_with_options(Cursor::new(file_bytes), DwgReadOptions::failsafe());
        match reader.read_with_stats() {
            Ok(o) => o.document,
            Err(_) => return CadGeometry::default(),
        }
    } else {
        let reader = match DxfReader::from_reader(Cursor::new(file_bytes)) {
            Ok(r) => r,
            Err(_) => return CadGeometry::default(),
        };
        let reader = reader.with_configuration(DxfReaderConfiguration {
            failsafe: true,
            default_encoding: None,
        });
        match reader.read_with_stats() {
            Ok(o) => o.document,
            Err(_) => return CadGeometry::default(),
        }
    };
    let mut g = CadGeometry::default();
    let mut map: HashMap<String, u32> = HashMap::new();
    let mut b = Bounds::new();
    // 块定义内的实体不是图纸内容,一起画会和INSERT展开出的副本重复错位
    for e in doc.model_space_entities() {
        process_entity(e, &doc, &mut map, &mut g, &mut b);
    }
    g.bounds_min = vec![b.min_x, b.min_y, b.min_z];
    g.bounds_max = vec![b.max_x, b.max_y, b.max_z];
    g
}

#[derive(uniffi::Record, Debug, Default)]
pub struct CadParseResult {
    pub format: String,
    pub type_names: Vec<String>,
    pub entity_counts: Vec<u32>,
    pub layer_names: Vec<String>,
    pub diagnostics: Vec<String>,
    pub version: String,
}

// Debug会序列化整个实体,大文件极慢,用match取变体名
fn entity_type_name(e: &EntityType) -> &'static str {
    match e {
        EntityType::Point(_) => "Point",
        EntityType::Line(_) => "Line",
        EntityType::Circle(_) => "Circle",
        EntityType::Arc(_) => "Arc",
        EntityType::Ellipse(_) => "Ellipse",
        EntityType::Polyline(_) => "Polyline",
        EntityType::Polyline2D(_) => "Polyline2D",
        EntityType::Polyline3D(_) => "Polyline3D",
        EntityType::LwPolyline(_) => "LwPolyline",
        EntityType::Text(_) => "Text",
        EntityType::MText(_) => "MText",
        EntityType::Spline(_) => "Spline",
        EntityType::Helix(_) => "Helix",
        EntityType::Dimension(_) => "Dimension",
        EntityType::Hatch(_) => "Hatch",
        EntityType::Solid(_) => "Solid",
        EntityType::Face3D(_) => "Face3D",
        EntityType::Insert(_) => "Insert",
        EntityType::Block(_) => "Block",
        EntityType::BlockEnd(_) => "BlockEnd",
        EntityType::Ray(_) => "Ray",
        EntityType::XLine(_) => "XLine",
        EntityType::Viewport(_) => "Viewport",
        EntityType::AttributeDefinition(_) => "AttributeDefinition",
        EntityType::AttributeEntity(_) => "AttributeEntity",
        EntityType::Leader(_) => "Leader",
        EntityType::MultiLeader(_) => "MultiLeader",
        EntityType::MLine(_) => "MLine",
        EntityType::Mesh(_) => "Mesh",
        EntityType::RasterImage(_) => "RasterImage",
        EntityType::Solid3D(_) => "Solid3D",
        EntityType::Region(_) => "Region",
        EntityType::Body(_) => "Body",
        EntityType::Surface(_) => "Surface",
        EntityType::Table(_) => "Table",
        EntityType::Tolerance(_) => "Tolerance",
        EntityType::PolyfaceMesh(_) => "PolyfaceMesh",
        EntityType::Wipeout(_) => "Wipeout",
        EntityType::Shape(_) => "Shape",
        EntityType::Underlay(_) => "Underlay",
        EntityType::Seqend(_) => "Seqend",
        EntityType::Ole2Frame(_) => "Ole2Frame",
        EntityType::PolygonMesh(_) => "PolygonMesh",
        EntityType::Light(_) => "Light",
        EntityType::SectionSymbol(_) => "SectionSymbol",
        EntityType::ViewBorder(_) => "ViewBorder",
        EntityType::Extended(_) => "Extended",
        EntityType::Unknown(_) => "Unknown",
    }
}

#[uniffi::export]
pub fn parse_cad(file_bytes: Vec<u8>, filename: String) -> CadParseResult {
    let lower = filename.to_ascii_lowercase();
    let is_dwg = lower.ends_with(".dwg");
    let is_dxf = lower.ends_with(".dxf");
    if !is_dwg && !is_dxf {
        debug_log!("[RUST] unsupported cad file: {}", filename);
        return CadParseResult::default();
    }

    debug_log!(
        "[RUST] parse_cad start: format={} bytes={}",
        if is_dwg { "DWG" } else { "DXF" },
        file_bytes.len()
    );

    let outcome = if is_dwg {
        let mut reader =
            DwgReader::from_stream_with_options(Cursor::new(file_bytes), DwgReadOptions::failsafe());
        match reader.read_with_stats() {
            Ok(o) => o,
            Err(e) => {
                debug_log!("[RUST] dwg parse failed: {}", e);
                return CadParseResult::default();
            }
        }
    } else {
        let reader = match DxfReader::from_reader(Cursor::new(file_bytes)) {
            Ok(r) => r,
            Err(e) => {
                debug_log!("[RUST] dxf parse failed: {}", e);
                return CadParseResult::default();
            }
        };
        let reader = reader.with_configuration(DxfReaderConfiguration {
            failsafe: true,
            default_encoding: None,
        });
        match reader.read_with_stats() {
            Ok(o) => o,
            Err(e) => {
                debug_log!("[RUST] dxf parse failed: {}", e);
                return CadParseResult::default();
            }
        }
    };

    let doc = outcome.document;
    let stats = outcome.stats;

    let mut names: Vec<String> = Vec::new();
    let mut counts: Vec<u32> = Vec::new();
    // 与几何层保持同一口径:只统计图纸内容,不含块定义
    for e in doc.model_space_entities() {
        let name = entity_type_name(e);
        if let Some(i) = names.iter().position(|n| n == name) {
            counts[i] += 1;
        } else {
            names.push(name.to_string());
            counts.push(1);
        }
    }

    let layer_names: Vec<String> = doc.layers.iter().map(|l| l.name.clone()).collect();
    let diagnostics: Vec<String> = stats
        .diagnostics
        .iter()
        .map(|d| format!("{}: {}", d.code, d.message))
        .collect();

    debug_log!(
        "[RUST] parse_cad ok: entities={} layers={} version={}",
        counts.iter().sum::<u32>(),
        layer_names.len(),
        stats.source_version
    );

    CadParseResult {
        format: if is_dwg { "DWG" } else { "DXF" }.to_string(),
        type_names: names,
        entity_counts: counts,
        layer_names,
        diagnostics,
        version: stats.source_version,
    }
}