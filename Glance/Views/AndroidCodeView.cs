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

    private ScrollView? _vScroll;
    private HorizontalScrollView? _hScroll;
    private TextView? _text;
    private double _codeFontSize = FontSizeSp;
    private (string? Text, HighlightIndex? Highlights, bool IsDark)? _pending;

    public double CodeFontSize
    {
        get => _codeFontSize;
        set
        {
            _codeFontSize = value;
            if (_text is not null && value > 0)
            {
                _text.SetTextSize(ComplexUnitType.Sp, (float)value);
                MeasureMinWidth(_text, _text.Text ?? string.Empty);
            }
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        Android.Util.Log.Info("GlanceNative", "CreateNativeControlCore");
        var ctx = (parent as AndroidViewControlHandle)?.View.Context
                  ?? Android.App.Application.Context;

        var text = new TextView(ctx)
        {
            Typeface = Android.Graphics.Typeface.Create(FontFamilyName, Android.Graphics.TypefaceStyle.Normal),
            Gravity = GravityFlags.Top
        };
        text.SetTextIsSelectable(true);
        text.SetTextSize(ComplexUnitType.Sp, (float)_codeFontSize);
        text.SetPadding(8, 8, 8, 8);
        text.ViewAttachedToWindow += (_, _) => Android.Util.Log.Info("GlanceNative", "TextView.ViewAttachedToWindow");
        text.LayoutChange += (_, e) => Android.Util.Log.Info("GlanceNative",
            $"TextView.LayoutChange {e.Left},{e.Top},{e.Right},{e.Bottom}");

        // 双轴都走原生touch
        var hScroll = new HorizontalScrollView(ctx) { HorizontalScrollBarEnabled = true };
        hScroll.AddView(text, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        var vScroll = new ScrollView(ctx) { VerticalScrollBarEnabled = true };
        vScroll.AddView(hScroll, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _text = text;
        _hScroll = hScroll;
        _vScroll = vScroll;

        // 原生控件创建晚于首次SetContent，补投递缓存的内容
        if (_pending is { } p)
        {
            _pending = null;
            ApplyContent(text, p.Text, p.Highlights, p.IsDark);
        }

        return new AndroidViewControlHandle(vScroll);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _text = null;
        _hScroll = null;
        _vScroll = null;
        base.DestroyNativeControlCore(control);
    }

    public void SetContent(string? text, HighlightIndex? highlights, bool isDark)
    {
        var tv = _text;
        Android.Util.Log.Info("GlanceNative", $"SetContent len={text?.Length ?? -1} hasTv={tv is not null}");
        if (tv is null)
        {
            _pending = (text, highlights, isDark);
            return;
        }
        ApplyContent(tv, text, highlights, isDark);
    }

    private void ApplyContent(TextView tv, string? text, HighlightIndex? highlights, bool isDark)
    {
        text ??= string.Empty;

        tv.SetTextColor(new NativeColor(ToArgb(HighlightColor.ColorFor(string.Empty, isDark))));

        if (highlights is null || text.Length == 0)
        {
            tv.Text = text;
            MeasureMinWidth(tv, text);
            return;
        }

        var runs = BuildDocumentRuns(text, highlights, isDark);
        var spannable = new SpannableString(text);
        foreach (var (start, end, argb) in runs)
            spannable.SetSpan(new ForegroundColorSpan(new NativeColor(argb)), start, end, SpanTypes.ExclusiveExclusive);
        tv.TextFormatted = spannable;
        tv.SetTextIsSelectable(true);
        MeasureMinWidth(tv, text);
        Android.Util.Log.Info("GlanceNative", $"Applied len={text.Length} runs={runs.Count} attached={tv.IsAttachedToWindow}");
    }

    // 按最长行宽度定位TextView最小宽度
    private static void MeasureMinWidth(TextView tv, string text)
    {
        if (text.Length == 0) { tv.SetMinWidth(0); return; }
        var paint = tv.Paint;
        float max = 0f;
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            var end = nl < 0 ? text.Length : nl;
            var w = paint.MeasureText(text, start, end);
            if (w > max) max = w;
            if (nl < 0) break;
            start = nl + 1;
        }
        tv.SetMinWidth((int)Math.Ceiling(max) + tv.PaddingLeft + tv.PaddingRight);
    }

    public void ScrollToEnd()
    {
        var v = _vScroll;
        var h = _hScroll;
        if (v is null) return;
        v.Post(() =>
        {
            v.FullScroll(FocusSearchDirection.Down);
            h?.FullScroll(FocusSearchDirection.Right);
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
