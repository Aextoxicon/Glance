using System.Collections.Generic;
using Avalonia.Media;

namespace Glance.Utils;

public static class HighlightColor
{
    public static readonly Color Keyword = Color.FromRgb(0xD7, 0x3A, 0x49);
    public static readonly Color String = Color.FromRgb(0x09, 0x62, 0x2A);
    public static readonly Color Comment = Color.FromRgb(0x6A, 0x73, 0x7D);
    public static readonly Color Function = Color.FromRgb(0x82, 0x50, 0xDF);
    public static readonly Color FunctionBuiltin = Color.FromRgb(0x00, 0x5C, 0xC5);
    public static readonly Color FunctionMethod = Color.FromRgb(0x6F, 0x42, 0xC1);
    public static readonly Color Type = Color.FromRgb(0x6F, 0x42, 0xC1);
    public static readonly Color Number = Color.FromRgb(0x05, 0x50, 0xAE);
    public static readonly Color Operator = Color.FromRgb(0xD7, 0x3A, 0x49);
    public static readonly Color Identifier = Color.FromRgb(0x24, 0x29, 0x2E);
    public static readonly Color Variable = Color.FromRgb(0xE3, 0x62, 0x09);
    public static readonly Color Property = Color.FromRgb(0xEC, 0x09, 0xBE);
    public static readonly Color Punctuation = Color.FromRgb(0x8C, 0x95, 0x9F);
    public static readonly Color Escape = Color.FromRgb(0xE3, 0x62, 0x09);
    public static readonly Color ConstantBuiltin = Color.FromRgb(0x95, 0x38, 0x00);
    public static readonly Color Label = Color.FromRgb(0xE3, 0x62, 0x09);
    public static readonly Color Namespace = Color.FromRgb(0x28, 0xA7, 0x45);
    public static readonly Color Builtin = Color.FromRgb(0x6F, 0x42, 0xC1);
    public static readonly Color Tag = Color.FromRgb(0x22, 0x86, 0x3A);
    public static readonly Color Constructor = Color.FromRgb(0x6F, 0x42, 0xC1);
    public static readonly Color Module = Color.FromRgb(0x28, 0xA7, 0x45);
    public static readonly Color Error = Color.FromRgb(0xCF, 0x22, 0x2E);
    public static readonly Color PlainText = Color.FromRgb(0x24, 0x29, 0x2E);

    private static readonly IReadOnlyDictionary<string, Color> ColorMap = new Dictionary<string, Color>
    {
        ["keyword"] = Keyword,
        ["string"] = String,
        ["comment"] = Comment,
        ["function"] = Function,
        ["type"] = Type,
        ["number"] = Number,
        ["operator"] = Operator,
        ["builtin"] = Builtin,
        ["identifier"] = Identifier,
        ["variable"] = Variable,
        ["parameter"] = Variable,
        ["property"] = Property,
        ["punctuation"] = Punctuation,
        ["delimiter"] = Punctuation,
        ["embedded"] = Punctuation,
        ["escape"] = Escape,
        ["constant"] = ConstantBuiltin,
        ["label"] = Label,
        ["namespace"] = Namespace,
        ["tag"] = Tag,
        ["constructor"] = Constructor,
        ["module"] = Module,
        ["error"] = Error,
        ["text"] = String,
        ["boolean"] = Keyword,
        ["attribute"] = Keyword,
        ["import"] = Keyword,
        ["conditional"] = Keyword,
        ["repeat"] = Keyword,
        ["include"] = Keyword,
        ["exception"] = Keyword,
        ["function.builtin"] = FunctionBuiltin,
        ["function.method"] = FunctionMethod,
        ["function_declaration"] = Function,
        ["function_definition"] = Function,
        ["class_declaration"] = Constructor,
        ["class_definition"] = Constructor,
        ["class_specifier"] = Constructor,
        ["method_declaration"] = FunctionMethod,
        ["string.escape"] = Escape,
        ["string.special.key"] = Property,
        ["constant.builtin"] = ConstantBuiltin,
        ["constant.macro"] = ConstantBuiltin,
        ["keyword.function"] = FunctionBuiltin,
    };

    public static Color ColorFor(string kind)
    {
        if (ColorMap.TryGetValue(kind, out var color))
            return color;
        var prefix = kind;
        while (true)
        {
            var idx = prefix.LastIndexOf('.');
            if (idx < 0) break;
            prefix = prefix.Substring(0, idx);
            if (ColorMap.TryGetValue(prefix, out var parent))
                return parent;
        }
        return PlainText;
    }

    // TODO: buildLineAnnotatedString 依赖 Avalonia 文本渲染（AnnotatedString），属 UI 重写，移至 Views 层。
}
