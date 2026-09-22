use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_javascript::LANGUAGE, HIGHLIGHT_QUERY);

const HIGHLIGHT_QUERY: &str = r##"
;COMMENTS

(comment) @comment

;STRINGS & LITERALS

[
  (string)
  (template_string)
  (string_fragment)
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

(this) @variable.builtin
(super) @variable.builtin

;KEYWORDS

[
  "async"
  "await"
  "break"
  "case"
  "catch"
  "class"
  "const"
  "continue"
  "debugger"
  "default"
  "delete"
  "do"
  "else"
  "export"
  "extends"
  "finally"
  "for"
  "from"
  "function"
  "get"
  "if"
  "import"
  "in"
  "instanceof"
  "let"
  "new"
  "of"
  "return"
  "set"
  "static"
  "switch"
  "target"
  "throw"
  "try"
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
    name: (identifier) @type))

;VARIABLE DECLARATIONS

(variable_declarator
  name: (identifier) @variable)

;PROPERTIES

(property_identifier) @property

;LABELED STATEMENTS

(labeled_statement
  (statement_identifier) @label)

;JSX

(jsx_opening_element
  name: (identifier) @function)

(jsx_closing_element
  name: (identifier) @function)

(jsx_self_closing_element
  name: (identifier) @function)

(jsx_attribute
  (property_identifier) @property)

;IDENTIFIERS (fallback)

(identifier) @identifier
"##;