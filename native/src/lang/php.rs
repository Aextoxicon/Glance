use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(
    tree_sitter_php::LANGUAGE_PHP,
    tree_sitter_php::HIGHLIGHTS_QUERY,
);
