use std::sync::LazyLock;
use crate::lang::GrammarDef;

pub static GRAMMAR: LazyLock<GrammarDef> = grammar!(tree_sitter_go::LANGUAGE.into(), HIGHLIGHT_QUERY);

// 高亮优先级规则：
// 更具体的capture写在前面，匹配优先级更高
// (identifier)作为兜底，放在最后
const HIGHLIGHT_QUERY: &str = r##"
;COMMENTS

(comment) @comment

;STRINGS & LITERALS

[
  (interpreted_string_literal)
  (raw_string_literal)
  (rune_literal)
] @string

(escape_sequence) @escape

;NUMBERS

[
  (int_literal)
  (float_literal)
  (imaginary_literal)
] @number

;CONSTANTS

[
  (true)
  (false)
  (nil)
  (iota)
] @constant.builtin

;KEYWORDS

[
  "break"
  "case"
  "chan"
  "const"
  "continue"
  "default"
  "defer"
  "else"
  "fallthrough"
  "for"
  "func"
  "go"
  "goto"
  "if"
  "import"
  "interface"
  "map"
  "package"
  "range"
  "return"
  "select"
  "struct"
  "switch"
  "type"
  "var"
] @keyword

;OPERATORS

[
  "--"
  "-"
  "-="
  ":="
  "!"
  "!="
  "..."
  "*"
  "*="
  "/"
  "/="
  "&"
  "&&"
  "&="
  "%"
  "%="
  "^"
  "^="
  "+"
  "++"
  "+="
  "<-"
  "<"
  "<<"
  "<<="
  "<="
  "="
  "=="
  ">"
  ">="
  ">>"
  ">>="
  "|"
  "|="
  "||"
  "~"
] @operator

;FUNCTION CALLS

; Builtin function calls
(call_expression
  function: (identifier) @function.builtin
  (#match? @function.builtin "^(append|cap|close|complex|copy|delete|imag|len|make|new|panic|print|println|real|recover)$"))

; Regular function calls (unqualified)
(call_expression
  function: (identifier) @function)

; Method calls (qualified: receiver.Method)
(call_expression
  function: (selector_expression
    field: (field_identifier) @function.method))

; Type conversion calls like int64(x), float64(x), string(x)
(type_conversion_expression
  type: (type_identifier) @type)

;FUNCTION/METHOD DEFINITIONS

; Function declaration: func Foo(...)
(function_declaration
  name: (identifier) @function)

; Method declaration: func (r *Receiver) Foo(...)
(method_declaration
  name: (field_identifier) @function.method)

; Method receiver: the (r *Receiver) part - it's the first parameter
(method_declaration
  parameters: (parameter_list
    (parameter_declaration
      name: (identifier) @variable)))

; Function parameters and return types
(parameter_list
  (parameter_declaration
    name: (identifier) @variable))

;TYPE DEFINITIONS

; Type declaration: type Foo Bar
(type_declaration
  (type_spec
    name: (type_identifier) @type
    type: (type_identifier) @type))

; Type alias: type Foo = Bar
(type_declaration
  (type_spec
    name: (type_identifier) @type
    type: (type_identifier) @type))

; Struct type: type Foo struct { ... }
(type_declaration
  (type_spec
    name: (type_identifier) @type
    type: (struct_type) @keyword))

; Interface type: type Foo interface { ... }
(type_declaration
  (type_spec
    name: (type_identifier) @type
    type: (interface_type) @keyword))

; Struct field names
(field_declaration
  name: (field_identifier) @property)

; Struct tags
(field_declaration
  tag: (interpreted_string_literal) @string)

;IMPORT STATEMENTS

; import "fmt" — import path string
(import_spec
  path: (interpreted_string_literal) @string)

; import alias: import alias "fmt"
(import_spec
  name: (package_identifier) @namespace
  path: (interpreted_string_literal) @string)

; Import group: import ( "fmt" "os" )
(import_declaration
  "import" @keyword)

;PACKAGE

; package main
(package_clause
  "package" @keyword
  (package_identifier) @namespace)

;SHORT VARIABLE DECLARATIONS

; x := expr — left side is variable
(short_var_declaration
  left: (expression_list
    (identifier) @variable))

;ASSIGNMENTS

; x = expr — left side is variable
(assignment_statement
  left: (expression_list
    (identifier) @variable))

;CONTROL FLOW

; if/else/for/switch/select expressions checked with conditions
; if x > 0 { ... }
(if_statement
  condition: (binary_expression
    left: (identifier) @variable))

;LABELED STATEMENTS

; labelName:
(labeled_statement
  (label_name) @label)

;TYPE ASSERTIONS

; x.(Type)
(type_assertion_expression
  type: (type_identifier) @type)

;GENERICS (Go 1.18+)

; func Foo[T any](x T) - capture the type parameter name
(type_parameter_list
  (type_parameter_declaration
    name: (identifier) @type))

;QUALIFIED REFERENCES

; Qualified function call: fmt.Printf(...) - package name as namespace
(selector_expression
  operand: (identifier) @namespace)

;IDENTIFIERS (fallback)

; Type identifiers not caught above
(type_identifier) @type

; Field identifiers (struct field access)
(field_identifier) @property

; Generic identifier fallback — very last, so more specific matches win
(identifier) @identifier
"##;
