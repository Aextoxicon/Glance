use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_c::LANGUAGE, tree_sitter_c::HIGHLIGHT_QUERY);
