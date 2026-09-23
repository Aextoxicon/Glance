use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(
    tree_sitter_xml::LANGUAGE_XML,
    tree_sitter_xml::XML_HIGHLIGHT_QUERY,
);
