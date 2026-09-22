use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> =
    grammar!(tree_sitter_toml_ng::LANGUAGE, tree_sitter_toml_ng::HIGHLIGHTS_QUERY);
