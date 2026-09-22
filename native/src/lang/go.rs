use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_go::LANGUAGE, tree_sitter_go::HIGHLIGHTS_QUERY);
