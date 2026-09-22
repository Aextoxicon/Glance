use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_json::LANGUAGE, tree_sitter_json::HIGHLIGHTS_QUERY);
