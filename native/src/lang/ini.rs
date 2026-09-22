use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_ini::LANGUAGE, tree_sitter_ini::HIGHLIGHTS_QUERY);
