use uniffi_code_parser::lang::get_grammar;
use uniffi_code_parser::OUTLINE_STRUCTURAL_KINDS;

// 每种语言取一个代表扩展名；同一grammar的其它扩展名（.h/.hpp/.mjs等）覆盖结果相同
const EXTS: &[&str] = &[
    ".c", ".cpp", ".go", ".py", ".js", ".ts", ".tsx", ".sh", ".cs", ".java", ".json", ".css",
    ".rs", ".toml", ".yaml", ".ini", ".mk", ".kt", ".dockerfile", ".swift", ".html", ".xml",
    ".rb", ".php",
];

const KNOWN_RED: &[&str] = &[".mk", ".dockerfile", ".rb"];

// 各grammar crate自带的node-types.json；少数crate的常量名带前缀
fn node_types_of(ext: &str) -> &'static str {
    match ext {
        ".c" => tree_sitter_c::NODE_TYPES,
        ".cpp" => tree_sitter_cpp::NODE_TYPES,
        ".go" => tree_sitter_go::NODE_TYPES,
        ".py" => tree_sitter_python::NODE_TYPES,
        ".js" => tree_sitter_javascript::NODE_TYPES,
        ".ts" => tree_sitter_typescript::TYPESCRIPT_NODE_TYPES,
        ".tsx" => tree_sitter_typescript::TSX_NODE_TYPES,
        ".sh" => tree_sitter_bash::NODE_TYPES,
        ".cs" => tree_sitter_c_sharp::NODE_TYPES,
        ".java" => tree_sitter_java::NODE_TYPES,
        ".json" => tree_sitter_json::NODE_TYPES,
        ".css" => tree_sitter_css::NODE_TYPES,
        ".rs" => tree_sitter_rust::NODE_TYPES,
        ".toml" => tree_sitter_toml_ng::NODE_TYPES,
        ".yaml" => tree_sitter_yaml::NODE_TYPES,
        ".ini" => tree_sitter_ini::NODE_TYPES,
        ".mk" => tree_sitter_make::NODE_TYPES,
        ".kt" => tree_sitter_kotlin_sg::NODE_TYPES,
        ".dockerfile" => tree_sitter_containerfile::NODE_TYPES,
        ".swift" => tree_sitter_swift::NODE_TYPES,
        ".html" => tree_sitter_html::NODE_TYPES,
        ".xml" => tree_sitter_xml::XML_NODE_TYPES,
        ".rb" => tree_sitter_ruby::NODE_TYPES,
        ".php" => tree_sitter_php::PHP_NODE_TYPES,
        _ => panic!("{} 未登记node-types", ext),
    }
}

// 数出带name字段的节点类型：只需看fields块里有没有name这个键，不引json解析依赖
fn count_name_fields(node_types: &str) -> usize {
    let mut count = 0usize;
    let mut search = 0usize;
    while let Some(rel) = node_types[search..].find("\"fields\"") {
        let after = search + rel + "\"fields\"".len();
        let Some(brace) = node_types[after..].find('{') else {
            break;
        };
        let open = after + brace;
        let mut depth = 0i32;
        let mut end = open;
        for (step, byte) in node_types.as_bytes()[open..].iter().enumerate() {
            match byte {
                b'{' => depth += 1,
                b'}' => {
                    depth -= 1;
                    if depth == 0 {
                        end = open + step;
                        break;
                    }
                }
                _ => {}
            }
        }
        if node_types[open..=end].contains("\"name\":") {
            count += 1;
        }
        search = end + 1;
    }
    count
}

struct Row {
    ext: &'static str,
    hit: usize,
    named: usize,
}

fn measure(ext: &'static str) -> Row {
    let grammar = get_grammar(ext).unwrap_or_else(|| panic!("{} 没有登记grammar", ext));
    let lang = &grammar.language;
    let mut hit = 0usize;
    for id in 0..lang.node_kind_count() as u16 {
        if !lang.node_kind_is_named(id) {
            continue;
        }
        if let Some(kind) = lang.node_kind_for_id(id) {
            if OUTLINE_STRUCTURAL_KINDS.contains(&kind) {
                hit += 1;
            }
        }
    }
    Row {
        ext,
        hit,
        named: count_name_fields(node_types_of(ext)),
    }
}

/// 大纲覆盖率自检：白名单命中0且有命名节点 = 漏补；两者都0 = 该语言本就没有定义概念
#[test]
fn outline_kind_coverage_table() {
    let rows: Vec<Row> = EXTS.iter().map(|ext| measure(ext)).collect();

    let mut report = String::from("ext | 命中白名单 | 带name字段的节点\n----|---|----\n");
    for r in &rows {
        report.push_str(&format!("{} | {} | {}\n", r.ext, r.hit, r.named));
    }
    let shallow: Vec<String> = rows
        .iter()
        .filter(|r| r.hit <= 2 && r.named > 2)
        .map(|r| format!("{}(命中{}/命名节点{})", r.ext, r.hit, r.named))
        .collect();
    report.push_str(&format!("\n覆盖过浅（仅提示）: {:?}\n", shallow));
    eprintln!("{}", report);

    let red: Vec<&str> = rows
        .iter()
        .filter(|r| r.hit == 0 && r.named > 0)
        .map(|r| r.ext)
        .collect();
    assert_eq!(red, KNOWN_RED, "大纲恒空清单发生变化:\n{}", report);
}
