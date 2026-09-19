use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR_TS: LazyLock<GrammarDef> = grammar!(tree_sitter_typescript::LANGUAGE_TYPESCRIPT.into(), HIGHLIGHT_QUERY_BASE);
pub static GRAMMAR_TSX: LazyLock<GrammarDef> = LazyLock::new(|| {
    let query_source = format!("{}{}", HIGHLIGHT_QUERY_BASE, JSX_RULES);
    let query = tree_sitter::Query::new(
        &tree_sitter_typescript::LANGUAGE_TSX.into(),
        &query_source,
    )
    .expect("invalid TSX highlight query");
    GrammarDef {
        language: tree_sitter_typescript::LANGUAGE_TSX.into(),
        compiled_query: query,
    }
});

// 不含JSX
const HIGHLIGHT_QUERY_BASE: &str = r##"
;COMMENTS

(comment) @comment

;STRINGS & LITERALS

[
  (string)
  (template_string)
] @string

(escape_sequence) @escape

;TEMPLATE LITERALS

(template_substitution
  "${" @punctuation
  "}" @punctuation)

;REGEX

(regex) @string

;NUMBERS

[
  (number)
] @number

;CONSTANTS

[
  (true)
  (false)
  (null)
  (undefined)
] @constant.builtin

;KEYWORDS

[
  "abstract"
  "as"
  "asserts"
  "async"
  "await"
  "break"
  "case"
  "catch"
  "class"
  "const"
  "continue"
  "debugger"
  "declare"
  "default"
  "delete"
  "do"
  "else"
  "enum"
  "export"
  "extends"
  "finally"
  "for"
  "from"
  "function"
  "get"
  "if"
  "implements"
  "import"
  "in"
  "infer"
  "instanceof"
  "interface"
  "is"
  "keyof"
  "let"
  "module"
  "namespace"
  "new"
  "of"
  "override"
  "private"
  "protected"
  "public"
  "readonly"
  "return"
  "satisfies"
  "set"
  "static"
  "switch"
  "target"
  "throw"
  "try"
  "type"
  "typeof"
  "var"
  "void"
  "while"
  "with"
  "yield"
] @keyword

;OPERATORS

[
  "!"
  "!="
  "!=="
  "%"
  "%="
  "&"
  "&&"
  "&="
  "*"
  "**"
  "**="
  "*="
  "+"
  "++"
  "+="
  "-"
  "--"
  "-="
  "."
  "..."
  "/"
  "/="
  ":"
  ";"
  "<"
  "<<"
  "<<="
  "<="
  "="
  "=="
  "==="
  "=>"
  ">"
  ">="
  ">>"
  ">>="
  ">>>"
  ">>>="
  "?"
  "?."
  "??"
  "??="
  "^"
  "^="
  "|"
  "|="
  "||"
  "||="
  "~"
] @operator

;FUNCTION CALLS

(call_expression
  function: (identifier) @function)

(call_expression
  function: (member_expression
    property: (property_identifier) @function.method))

;FUNCTION DEFINITIONS

(function_declaration
  name: (identifier) @function)

(function_expression
  name: (identifier) @function)

(arrow_function
  parameter: (identifier) @variable)

;CLASS DEFINITIONS

(class_declaration
  name: (type_identifier) @type)

;INTERFACE / TYPE DEFINITIONS

(interface_declaration
  name: (type_identifier) @type)

(type_alias_declaration
  name: (type_identifier) @type)

(enum_declaration
  name: (identifier) @type)

;METHOD DEFINITIONS

(method_definition
  name: (property_identifier) @function.method)

;IMPORTS & EXPORTS

(import_statement
  (import_clause
    (identifier) @function))

(import_statement
  (import_clause
    (named_imports
      (import_specifier
        name: (identifier) @function))))

(import_statement
  source: (string) @string)

(export_statement
  (function_declaration
    name: (identifier) @function))

(export_statement
  (class_declaration
    name: (type_identifier) @type))

;TYPE ANNOTATIONS

(type_annotation
  (type_identifier) @type)

(type_annotation
  (predefined_type) @type)

;GENERICS

(type_arguments
  (type_identifier) @type)

(type_parameters
  (type_parameter
    name: (type_identifier) @type))

;VARIABLE DECLARATIONS

(variable_declarator
  name: (identifier) @variable)

;PROPERTIES

(property_identifier) @property

;LABELED STATEMENTS

(labeled_statement
  (statement_identifier) @label)

;IDENTIFIERS (fallback)

(identifier) @identifier
(type_identifier) @type
"##;

// 仅TSX独有：JSX元素规则（LANGUAGE_TYPESCRIPT节点集中无jsx_*，不可混入公共段）
const JSX_RULES: &str = r##"
;JSX

(jsx_opening_element
  name: (identifier) @function)

(jsx_closing_element
  name: (identifier) @function)

(jsx_self_closing_element
  name: (identifier) @function)

(jsx_attribute
  (property_identifier) @property)
"##;
