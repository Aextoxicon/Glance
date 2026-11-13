use std::sync::LazyLock;
use crate::lang::GrammarDef;

// CPP的query是C的增量补集：补上注释、字符串、数字与预处理的高亮模式
pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(
    tree_sitter_cpp::LANGUAGE,
    tree_sitter_c::HIGHLIGHT_QUERY,
    tree_sitter_cpp::HIGHLIGHT_QUERY,
);
