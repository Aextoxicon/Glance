using System;
using Avalonia;
using Glance.Native;

namespace Glance.ViewModels;

public sealed class OutlineRow
{
    public OutlineNode Node { get; }
    public int Depth { get; }
    public string KindLabel { get; }
    public Thickness Indent { get; }
    public string DisplayName => string.IsNullOrEmpty(Node.Name) ? "（无名）" : Node.Name;

    public OutlineRow(OutlineNode node, int depth)
    {
        Node = node;
        Depth = depth;
        KindLabel = KindLabelFor(node.Kind);
        Indent = new Thickness(Math.Min(depth, 3) * 14, 0, 0, 0);
    }

    private static readonly (string Prefix, string Label)[] KindPrefixes =
    {
        ("generator_function", "函数"),
        ("namespace", "命名空间"),
        ("class", "类"),
        ("interface", "接口"),
        ("struct", "结构体"),
        ("enum", "枚举"),
        ("method", "方法"),
        ("constructor", "构造"),
        ("function", "函数"),
        ("type_alias", "类型"),
        ("type_item", "类型"),
        ("type_declaration", "类型"),
        ("const", "常量"),
        ("field", "字段"),
        ("property", "属性"),
        ("variable", "变量"),
        ("module", "模块"),
        ("mod", "模块"),
        ("macro", "宏"),
        ("record", "记录"),
        ("trait", "特征"),
        ("impl", "实现"),
        ("union", "联合"),
        ("package_clause", "包"),
    };

    public static string KindLabelFor(string kind)
    {
        foreach (var (prefix, label) in KindPrefixes)
        {
            if (kind.StartsWith(prefix, StringComparison.Ordinal))
                return label;
        }
        return kind;
    }
}
