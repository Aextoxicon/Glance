use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_make::LANGUAGE, tree_sitter_make::HIGHLIGHTS_QUERY);
