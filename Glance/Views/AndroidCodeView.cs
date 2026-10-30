#if ANDROID
using System;
using System.Collections.Generic;
using Android.Text;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Android.Widget;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Glance.Processing;
using Glance.Utils;
using NativeColor = Android.Graphics.Color;

namespace Glance.Views;

// Android原生TextView岛，带系统级长按选择/放大镜/复制菜单
// 复用HighlightTransformer.BuildRuns，原生层只染色不解析
public class AndroidCodeView : NativeControlHost
{
    private const double FontSizeSp = 14;
    private const string FontFamilyName = "monospace";

    private HorizontalScrollView? _scroll;
    private TextView? _text;
    private double _codeFontSize = FontSizeSp;

    public double CodeFontSize
    {
        get => _codeFontSize;
        set
        {
            _codeFontSize = value;
            if (_text is not null && value > 0)
                _text.SetTextSize(ComplexUnitType.Sp, (float)value);
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var ctx = (parent as AndroidViewControlHandle)?.View.Context
                  ?? Android.App.Application.Context;

        var text = new TextView(ctx)
        {
            Typeface = Android.Graphics.Typeface.Create(FontFamilyName, Android.Graphics.TypefaceStyle.Normal),
            Gravity = GravityFlags.Top
        };
        text.SetTextIsSelectable(true);
        text.SetTextSize(ComplexUnitType.Sp, (float)_codeFontSize);
        text.SetPadding(0, 0, 0, 0);

        // 横向也用原生滚动
        _scroll = new HorizontalScrollView(ctx)
        {
            HorizontalScrollBarEnabled = true,
            VerticalScrollBarEnabled = true
        };
        _scroll.AddView(text, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        return new AndroidViewControlHandle(_scroll);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _text = null;
        _scroll = null;
        base.DestroyNativeControlCore(control);
    }

    public void SetContent(string? text, HighlightIndex? highlights, bool isDark)
    {
        var tv = _text;
        if (tv is null) return;
        text ??= string.Empty;

        tv.SetTextColor(new NativeColor(ToArgb(HighlightColor.ColorFor(string.Empty, isDark))));

        if (highlights is null || text.Length == 0)
        {
            tv.Text = text;
            return;
        }

        var runs = BuildDocumentRuns(text, highlights, isDark);
        var spannable = new SpannableString(text);
        foreach (var (start, end, argb) in runs)
            spannable.SetSpan(new ForegroundColorSpan(new NativeColor(argb)), start, end, SpanTypes.ExclusiveExclusive);
        tv.TextFormatted = spannable;
    }

    public void ScrollToEnd()
    {
        var sv = _scroll;
        if (sv is null) return;
        sv.Post(() =>
        {
            sv.FullScroll(FocusSearchDirection.Down);
            sv.FullScroll(FocusSearchDirection.Right);
        });
    }

    // 逐行复用BuildRuns
    internal static List<(int Start, int End, int Argb)> BuildDocumentRuns(
        string text, HighlightIndex highlights, bool isDark)
    {
        var result = new List<(int, int, int)>();
        var lineStart = 0;
        var line = 0;
        while (lineStart < text.Length)
        {
            var nl = text.IndexOf('\n', lineStart);
            var lineEnd = nl < 0 ? text.Length : nl;
            var tokens = highlights.TokensOf(line);
            if (tokens.Count > 0)
            {
                foreach (var run in HighlightTransformer.BuildRuns(tokens, lineStart, lineEnd, isDark))
                    result.Add((run.Start, run.End, ToArgb(run.Brush.Color)));
            }
            if (nl < 0) break;
            lineStart = nl + 1;
            line++;
        }
        return result;
    }

    internal static int ToArgb(Avalonia.Media.Color c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
}
#endif
