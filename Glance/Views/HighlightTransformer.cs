using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Glance.Processing;
using Glance.Utils;

namespace Glance.Views;

// token 偏移是相对行首的 UTF-16 字符索引
public sealed class HighlightTransformer : DocumentColorizingTransformer
{
    private readonly HighlightIndex? _index;

    public bool IsDark { get; set; }

    public HighlightTransformer(HighlightIndex? index, bool isDark = false)
    {
        _index = index;
        IsDark = isDark;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_index is null) return;
        var tokens = _index.TokensOf(line.LineNumber - 1);
        if (tokens.Count == 0) return;
        var baseOffset = line.Offset;
        foreach (var tok in tokens)
        {
            var start = baseOffset + tok.StartByte;
            var end = baseOffset + tok.EndByte;
            if (end <= start) continue;
            start = Math.Max(start, line.Offset);
            end = Math.Min(end, line.Offset + line.Length);
            if (end <= start) continue;
            var brush = new SolidColorBrush(HighlightColor.ColorFor(tok.Kind, IsDark));
            ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
