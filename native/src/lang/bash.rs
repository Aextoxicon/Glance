use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_bash::LANGUAGE, tree_sitter_bash::HIGHLIGHT_QUERY);
