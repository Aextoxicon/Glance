using Glance.Utils;
using Xunit;

namespace Glance.Tests;

// 错了也不报错的典型
public class HighlightColorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 未知种类回退到纯文本色(bool dark)
    {
        var plain = HighlightColor.ColorFor("definitely-not-a-kind", dark);
        Assert.Equal(HighlightColor.ColorFor(string.Empty, dark), plain);
    }

    [Theory]
    [InlineData("string.special", "string")]
    [InlineData("string.special.symbol", "string")]
    [InlineData("punctuation.bracket", "punctuation")]
    [InlineData("comment.documentation", "comment")]
    [InlineData("keyword.control", "keyword")]
    public void 点分前缀回退到父级颜色(string kind, string parent)
    {
        Assert.Equal(HighlightColor.ColorFor(parent), HighlightColor.ColorFor(kind));
    }

    [Theory]
    [InlineData("keyword", "Keyword")]
    [InlineData("string", "String")]
    [InlineData("comment", "Comment")]
    [InlineData("function", "Function")]
    [InlineData("type", "Type")]
    [InlineData("number", "Number")]
    [InlineData("operator", "Operator")]
    [InlineData("variable", "Variable")]
    [InlineData("property", "Property")]
    [InlineData("namespace", "Namespace")]
    [InlineData("tag", "Tag")]
    [InlineData("constructor", "Constructor")]
    [InlineData("module", "Module")]
    [InlineData("error", "Error")]
    public void 常见种类映射到指定颜色(string kind, string colorField)
    {
        var expected = (Avalonia.Media.Color)typeof(HighlightColor)
            .GetField(colorField)!.GetValue(null)!;
        Assert.Equal(expected, HighlightColor.ColorFor(kind));
    }

    [Theory]
    [InlineData("keyword")]
    [InlineData("string")]
    [InlineData("comment")]
    [InlineData("type")]
    [InlineData("number")]
    [InlineData("variable")]
    public void 深浅两套表对同一种类给出不同颜色(string kind)
    {
        // 深色表漏写时会退化成同一个浅色值，这里能立刻抓到
        Assert.NotEqual(HighlightColor.ColorFor(kind, dark: false), HighlightColor.ColorFor(kind, dark: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 声明类与内置类种类都有独立映射(bool dark)
    {
        var fallback = HighlightColor.ColorFor("definitely-not-a-kind", dark);
        string[] kinds =
        {
            "function.builtin", "function.method", "constant.builtin", "keyword.function",
            "function_declaration", "function_definition", "class_declaration",
            "class_definition", "class_specifier", "method_declaration", "string.escape",
            "string.special.key", "constant.macro",
        };

        foreach (var kind in kinds)
            Assert.NotEqual(fallback, HighlightColor.ColorFor(kind, dark));
    }
}
