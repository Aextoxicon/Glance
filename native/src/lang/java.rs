use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_java::LANGUAGE, tree_sitter_java::HIGHLIGHTS_QUERY);
