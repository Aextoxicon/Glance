use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_css::LANGUAGE, tree_sitter_css::HIGHLIGHTS_QUERY);
