using System;
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

    public static readonly Color DKeyword = Color.FromRgb(0xFF, 0x7B, 0x72);
    public static readonly Color DString = Color.FromRgb(0xA5, 0xD6, 0xFF);
    public static readonly Color DComment = Color.FromRgb(0x8B, 0x94, 0x9E);
    public static readonly Color DFunction = Color.FromRgb(0xD2, 0xA8, 0xFF);
    public static readonly Color DFunctionBuiltin = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DFunctionMethod = Color.FromRgb(0xD2, 0xA8, 0xFF);
    public static readonly Color DType = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DNumber = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DOperator = Color.FromRgb(0xFF, 0x7B, 0x72);
    public static readonly Color DIdentifier = Color.FromRgb(0xC9, 0xD1, 0xD9);
    public static readonly Color DVariable = Color.FromRgb(0xFF, 0xA6, 0x57);
    public static readonly Color DProperty = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DPunctuation = Color.FromRgb(0xC9, 0xD1, 0xD9);
    public static readonly Color DEscape = Color.FromRgb(0xFF, 0xA6, 0x57);
    public static readonly Color DConstantBuiltin = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DLabel = Color.FromRgb(0xFF, 0xA6, 0x57);
    public static readonly Color DNamespace = Color.FromRgb(0xD2, 0xA8, 0xFF);
    public static readonly Color DBuiltin = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DTag = Color.FromRgb(0x7E, 0xE7, 0x87);
    public static readonly Color DConstructor = Color.FromRgb(0x79, 0xC0, 0xFF);
    public static readonly Color DModule = Color.FromRgb(0xD2, 0xA8, 0xFF);
    public static readonly Color DError = Color.FromRgb(0xF8, 0x51, 0x49);
    public static readonly Color DPlainText = Color.FromRgb(0xC9, 0xD1, 0xD9);

    private static readonly (string Kind, Color Light, Color Dark)[] Palette =
    {
        ("keyword", Keyword, DKeyword),
        ("string", String, DString),
        ("comment", Comment, DComment),
        ("function", Function, DFunction),
        ("type", Type, DType),
        ("number", Number, DNumber),
        ("operator", Operator, DOperator),
        ("builtin", Builtin, DBuiltin),
        ("identifier", Identifier, DIdentifier),
        ("variable", Variable, DVariable),
        ("parameter", Variable, DVariable),
        ("property", Property, DProperty),
        ("punctuation", Punctuation, DPunctuation),
        ("delimiter", Punctuation, DPunctuation),
        ("embedded", Punctuation, DPunctuation),
        ("escape", Escape, DEscape),
        ("constant", ConstantBuiltin, DConstantBuiltin),
        ("label", Label, DLabel),
        ("namespace", Namespace, DNamespace),
        ("tag", Tag, DTag),
        ("constructor", Constructor, DConstructor),
        ("module", Module, DModule),
        ("error", Error, DError),
        ("text", String, DString),
        ("boolean", Keyword, DKeyword),
        ("attribute", Keyword, DKeyword),
        ("import", Keyword, DKeyword),
        ("conditional", Keyword, DKeyword),
        ("repeat", Keyword, DKeyword),
        ("include", Keyword, DKeyword),
        ("exception", Keyword, DKeyword),
        ("function.builtin", FunctionBuiltin, DFunctionBuiltin),
        ("function.method", FunctionMethod, DFunctionMethod),
        ("function_declaration", Function, DFunction),
        ("function_definition", Function, DFunction),
        ("class_declaration", Constructor, DConstructor),
        ("class_definition", Constructor, DConstructor),
        ("class_specifier", Constructor, DConstructor),
        ("method_declaration", FunctionMethod, DFunctionMethod),
        ("string.escape", Escape, DEscape),
        ("string.special.key", Property, DProperty),
        ("constant.builtin", ConstantBuiltin, DConstantBuiltin),
        ("constant.macro", ConstantBuiltin, DConstantBuiltin),
        ("keyword.function", FunctionBuiltin, DFunctionBuiltin),
    };

    private static readonly IReadOnlyDictionary<string, Color> LightMap;
    private static readonly IReadOnlyDictionary<string, Color> DarkMap;

    static HighlightColor()
    {
        var light = new Dictionary<string, Color>(Palette.Length, StringComparer.Ordinal);
        var dark = new Dictionary<string, Color>(Palette.Length, StringComparer.Ordinal);
        foreach (var (kind, l, d) in Palette)
        {
            light[kind] = l;
            dark[kind] = d;
        }
        LightMap = light;
        DarkMap = dark;
    }

    public static Color ColorFor(string kind, bool dark = false)
    {
        return Lookup(dark ? DarkMap : LightMap, kind, dark ? DPlainText : PlainText);
    }

    private static Color Lookup(IReadOnlyDictionary<string, Color> map, string kind, Color fallback)
    {
        if (map.TryGetValue(kind, out var color))
            return color;
        var prefix = kind;
        while (true)
        {
            var idx = prefix.LastIndexOf('.');
            if (idx < 0) break;
            prefix = prefix.Substring(0, idx);
            if (map.TryGetValue(prefix, out var parent))
                return parent;
        }
        return fallback;
    }
}
