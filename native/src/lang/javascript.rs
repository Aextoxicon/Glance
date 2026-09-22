use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(
    tree_sitter_javascript::LANGUAGE,
    tree_sitter_javascript::HIGHLIGHT_QUERY,
);
