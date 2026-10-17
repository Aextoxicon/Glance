using Avalonia;
using Glance.Native;

namespace Glance.ViewModels;

public sealed class OutlineRow
{
    public OutlineNode Node { get; }
    public int Depth { get; }
    public string KindLabel { get; }
    public Thickness Indent { get; }

    public OutlineRow(OutlineNode node, int depth)
    {
        Node = node;
        Depth = depth;
        KindLabel = KindLabelFor(node.Kind);
        Indent = new Thickness(depth * 14, 0, 0, 0);
    }

    public static string KindLabelFor(string kind)
    {
        return kind switch
        {
            "class_declaration" or "class" => "类",
            "interface_declaration" or "interface" => "接口",
            "struct" or "struct_declaration" => "结构体",
            "enum" or "enum_declaration" => "枚举",
            "method" or "method_declaration" => "方法",
            "function" or "function_declaration" => "函数",
            "namespace" or "namespace_definition" => "命名空间",
            "type_alias" or "type_alias_declaration" => "类型",
            "const" or "const_declaration" => "常量",
            "package_clause" => "包",
            "module" or "module_declaration" => "模块",
            "trait" => "特征",
            "impl" => "实现",
            "constructor" or "constructor_declaration" => "构造",
            "field" or "field_declaration" => "字段",
            "property" or "property_declaration" => "属性",
            "variable" or "variable_declaration" => "变量",
            "macro" or "macro_declaration" => "宏",
            "union" => "联合",
            _ => kind
        };
    }
}
