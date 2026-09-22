use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_containerfile::LANGUAGE, tree_sitter_containerfile::HIGHLIGHTS_QUERY);
