use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_python::LANGUAGE, tree_sitter_python::HIGHLIGHTS_QUERY);