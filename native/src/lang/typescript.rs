use std::sync::LazyLock;
use crate::lang::GrammarDef;

// TS = JS 基础段 + TS 增量段；TS 的 query 只含 TS 独有节点，无法独立使用
pub static GRAMMAR_TS: LazyLock<GrammarDef> = grammar!(
    tree_sitter_typescript::LANGUAGE_TYPESCRIPT,
    tree_sitter_javascript::HIGHLIGHT_QUERY,
    tree_sitter_typescript::HIGHLIGHTS_QUERY,
);

pub static GRAMMAR_TSX: LazyLock<GrammarDef> = grammar!(
    tree_sitter_typescript::LANGUAGE_TSX,
    tree_sitter_javascript::HIGHLIGHT_QUERY,
    tree_sitter_typescript::HIGHLIGHTS_QUERY,
    tree_sitter_javascript::JSX_HIGHLIGHT_QUERY,
);
