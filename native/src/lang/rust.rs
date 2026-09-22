use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_rust::LANGUAGE, tree_sitter_rust::HIGHLIGHTS_QUERY);