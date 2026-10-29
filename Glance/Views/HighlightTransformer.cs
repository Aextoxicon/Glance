using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Glance.Processing;
using Glance.Utils;

namespace Glance.Views;

// token偏移是相对行首的UTF-16字符索引
public sealed class HighlightTransformer : DocumentColorizingTransformer
{
    private readonly HighlightIndex? _index;
    private static readonly ConcurrentDictionary<(Color Color, bool Dark), SolidColorBrush> BrushCache = new();

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
        foreach (var run in BuildRuns(tokens, line.Offset, line.Offset + line.Length, IsDark))
        {
            var brush = run.Brush;
            ChangeLinePart(run.Start, run.End, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    // 同色token合并成一个run，重叠token不合并
    internal static List<HighlightRun> BuildRuns(
        IReadOnlyList<HighlightToken> tokens, int baseOffset, int lineEnd, bool isDark)
    {
        var sorted = new List<HighlightToken>(tokens);
        sorted.Sort((a, b) => a.StartByte.CompareTo(b.StartByte));

        var runs = new List<HighlightRun>(sorted.Count);
        var curStart = 0;
        var curEnd = 0;
        SolidColorBrush? curBrush = null;
        foreach (var tok in sorted)
        {
            var start = Math.Max(baseOffset + tok.StartByte, baseOffset);
            var end = Math.Min(baseOffset + tok.EndByte, lineEnd);
            if (end <= start) continue;
            var brush = BrushFor(HighlightColor.ColorFor(tok.Kind, isDark), isDark);
            if (curBrush is not null && ReferenceEquals(brush, curBrush) && start >= curEnd)
            {
                curEnd = end;
                continue;
            }
            if (curBrush is not null) runs.Add(new HighlightRun(curStart, curEnd, curBrush));
            curStart = start;
            curEnd = end;
            curBrush = brush;
        }
        if (curBrush is not null) runs.Add(new HighlightRun(curStart, curEnd, curBrush));
        return runs;
    }

    private static SolidColorBrush BrushFor(Color color, bool dark) =>
        BrushCache.GetOrAdd((color, dark), static key => new SolidColorBrush(key.Color));
}

internal readonly record struct HighlightRun(int Start, int End, SolidColorBrush Brush);
