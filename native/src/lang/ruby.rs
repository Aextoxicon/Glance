use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(
    tree_sitter_ruby::LANGUAGE,
    tree_sitter_ruby::HIGHLIGHTS_QUERY,
);
