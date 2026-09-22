use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_c_sharp::LANGUAGE, tree_sitter_c_sharp::HIGHLIGHTS_QUERY);
