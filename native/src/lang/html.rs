use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_html::LANGUAGE, tree_sitter_html::HIGHLIGHTS_QUERY);