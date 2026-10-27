using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Glance.Native;

namespace Glance.Views;

// 各字段是扁平double[]，stride见native/src/lib.rs
public sealed class CadCanvas : Control
{
    private const double Padding = 12.0;
    private const double TwoPi = Math.PI * 2;

    public static readonly StyledProperty<CadGeometry?> DrawingProperty =
        AvaloniaProperty.Register<CadCanvas, CadGeometry?>(nameof(Drawing));

    public CadGeometry? Drawing
    {
        get => GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    static CadCanvas()
    {
        AffectsRender<CadCanvas>(DrawingProperty);
    }

    public override void Render(DrawingContext context)
    {
        var g = Drawing;
        if (g is null) return;

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var minX = At(g.BoundsMin, 0);
        var minY = At(g.BoundsMin, 1);
        var maxX = At(g.BoundsMax, 0);
        var maxY = At(g.BoundsMax, 1);
        var dx = maxX - minX;
        var dy = maxY - minY;

        // 无图元时bounds退化，给个恒定比例避免除零
        var scaleX = dx > 1e-9 ? (width - Padding * 2) / dx : double.PositiveInfinity;
        var scaleY = dy > 1e-9 ? (height - Padding * 2) / dy : double.PositiveInfinity;
        var scale = Math.Min(scaleX, scaleY);
        if (double.IsInfinity(scale) || scale <= 0) scale = 1;

        var offsetX = (width - dx * scale) / 2 - minX * scale;
        var offsetY = (height + dy * scale) / 2 + minY * scale;

        Point ToScreen(double x, double y) => new(offsetX + x * scale, offsetY - y * scale);

        var themeColor = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Colors.White : Colors.Black;
        var themeBrush = new SolidColorBrush(themeColor);
        var brushCache = new Dictionary<uint, IBrush>();

        IBrush BrushFor(uint rgb)
        {
            if (rgb == 0) return themeBrush;
            if (brushCache.TryGetValue(rgb, out var cached)) return cached;
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brushCache[rgb] = brush;
            return brush;
        }

        var penCache = new Dictionary<uint, IPen>();

        IPen PenFor(uint rgb)
        {
            if (penCache.TryGetValue(rgb, out var cached)) return cached;
            var pen = new Pen(BrushFor(rgb), 1);
            penCache[rgb] = pen;
            return pen;
        }

        DrawLines(context, g, PenFor, ToScreen);
        DrawCircles(context, g, PenFor, ToScreen, scale);
        DrawArcs(context, g, PenFor, ToScreen, scale);
        DrawEllipses(context, g, PenFor, ToScreen, scale);
        DrawPolylines(context, g, PenFor, ToScreen);
        DrawTexts(context, g, BrushFor, ToScreen, scale);
    }

    private static void DrawLines(DrawingContext context, CadGeometry g, Func<uint, IPen> penFor,
        Func<double, double, Point> toScreen)
    {
        var data = g.LineData;
        for (var i = 0; i + 7 <= data.Length; i += 7)
            context.DrawLine(penFor(ColorAt(g.LineColors, i / 7)),
                toScreen(data[i], data[i + 1]), toScreen(data[i + 3], data[i + 4]));
    }

    private static void DrawCircles(DrawingContext context, CadGeometry g, Func<uint, IPen> penFor,
        Func<double, double, Point> toScreen, double scale)
    {
        var data = g.CircleData;
        for (var i = 0; i + 8 <= data.Length; i += 8)
        {
            var radius = data[i + 3] * scale;
            if (radius <= 0) continue;
            context.DrawEllipse(null, penFor(ColorAt(g.CircleColors, i / 8)),
                toScreen(data[i], data[i + 1]), radius, radius);
        }
    }

    // 已知起止点+圆心+法向：叉积定符号算扫掠角，避免优弧画成劣弧
    private static void DrawArcs(DrawingContext context, CadGeometry g, Func<uint, IPen> penFor,
        Func<double, double, Point> toScreen, double scale)
    {
        var data = g.ArcData;
        for (var i = 0; i + 14 <= data.Length; i += 14)
        {
            var start = new Vec(data[i], data[i + 1], data[i + 2]);
            var end = new Vec(data[i + 3], data[i + 4], data[i + 5]);
            var center = new Vec(data[i + 6], data[i + 7], data[i + 8]);
            var radius = data[i + 9] * scale;
            var normal = new Vec(data[i + 10], data[i + 11], data[i + 12]);
            if (radius <= 0) continue;

            var u = start - center;
            var v = end - center;
            var sweep = Math.Atan2(u.Cross(v).Dot(normal), u.Dot(v));
            if (sweep <= 0) sweep += TwoPi;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(toScreen(start.X, start.Y), false);
                ctx.ArcTo(toScreen(end.X, end.Y), new Size(radius, radius), 0,
                    sweep > Math.PI,
                    normal.Z >= 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, penFor(ColorAt(g.ArcColors, i / 14)), geometry);
        }
    }

    // 整椭圆走DrawEllipse+旋转变换；带参数区间的按参数取样画折线
    private static void DrawEllipses(DrawingContext context, CadGeometry g, Func<uint, IPen> penFor,
        Func<double, double, Point> toScreen, double scale)
    {
        var data = g.EllipseData;
        for (var i = 0; i + 13 <= data.Length; i += 13)
        {
            var center = new Vec(data[i], data[i + 1], data[i + 2]);
            var major = new Vec(data[i + 3], data[i + 4], data[i + 5]);
            var ratio = data[i + 6];
            var startParameter = data[i + 7];
            var endParameter = data[i + 8];
            var normal = new Vec(data[i + 9], data[i + 10], data[i + 11]);
            var majorLength = Math.Sqrt(major.Dot(major));
            if (majorLength <= 0 || ratio <= 0) continue;

            var pen = penFor(ColorAt(g.EllipseColors, i / 13));
            var span = (endParameter - startParameter) % TwoPi;
            if (span < 0) span += TwoPi;

            if (span < 1e-6)
            {
                var centerPoint = toScreen(center.X, center.Y);
                var rotation = Math.Atan2(-major.Y, major.X);
                using (context.PushTransform(
                    Matrix.CreateTranslation(-centerPoint.X, -centerPoint.Y) *
                    Matrix.CreateRotation(rotation) *
                    Matrix.CreateTranslation(centerPoint.X, centerPoint.Y)))
                {
                    context.DrawEllipse(null, pen, centerPoint, majorLength * scale, majorLength * ratio * scale);
                }
                continue;
            }

            var minor = normal.Cross(major) * ratio;
            var steps = Math.Clamp((int)(span / TwoPi * 96), 8, 192);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (var s = 0; s <= steps; s++)
                {
                    var t = startParameter + span * s / steps;
                    var point = center + major * Math.Cos(t) + minor * Math.Sin(t);
                    var screen = toScreen(point.X, point.Y);
                    if (s == 0) ctx.BeginFigure(screen, false);
                    else ctx.LineTo(screen);
                }
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }

    // 每顶点4个值(x,y,z,bulge)，bulge=tan(包含角/4)，正为逆时针
    private static void DrawPolylines(DrawingContext context, CadGeometry g, Func<uint, IPen> penFor,
        Func<double, double, Point> toScreen)
    {
        var data = g.PolylineData;
        var counts = g.PolylineCounts;
        var closedFlags = g.PolylineClosed;
        var cursor = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var count = (int)counts[i];
            if (count <= 0) continue;
            if (cursor + count * 4 > data.Length) break;

            var isClosed = i < closedFlags.Length && closedFlags[i];
            var pen = penFor(ColorAt(g.PolylineColors, i));

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var first = toScreen(data[cursor], data[cursor + 1]);
                ctx.BeginFigure(first, false);
                var previous = first;
                for (var v = 1; v < count; v++)
                {
                    var offset = cursor + v * 4;
                    var next = toScreen(data[offset], data[offset + 1]);
                    AddSegment(ctx, previous, next, data[offset - 1]);
                    previous = next;
                }
                if (isClosed)
                    AddSegment(ctx, previous, first, data[cursor + (count - 1) * 4 + 3]);
                ctx.EndFigure(isClosed);
            }
            context.DrawGeometry(null, pen, geometry);
            cursor += count * 4;
        }
    }

    // 每条8个值(x,y,z,height,rotation,halign,valign,layer)，锚点是基线左端
    private static void DrawTexts(DrawingContext context, CadGeometry g, Func<uint, IBrush> brushFor,
        Func<double, double, Point> toScreen, double scale)
    {
        var data = g.TextData;
        var values = g.TextValues;
        for (var i = 0; i + 8 <= data.Length; i += 8)
        {
            var index = i / 8;
            if (index >= values.Length) break;
            var text = values[index];
            if (string.IsNullOrWhiteSpace(text)) continue;

            var emSize = data[i + 3] * scale;
            // 屏幕上太小的字会糊成一团
            if (emSize < 4) continue;

            var anchor = toScreen(data[i], data[i + 1]);
            var rotation = data[i + 4];
            var horizontal = (int)Math.Round(data[i + 5]);
            var vertical = (int)Math.Round(data[i + 6]);

            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, emSize, brushFor(ColorAt(g.TextColors, index)));

            var x = horizontal switch
            {
                1 => anchor.X - formatted.Width / 2,
                2 => anchor.X - formatted.Width,
                _ => anchor.X,
            };
            var y = vertical switch
            {
                1 => anchor.Y - formatted.Height,
                2 => anchor.Y - formatted.Height / 2,
                3 => anchor.Y,
                _ => anchor.Y - formatted.Baseline,
            };

            if (Math.Abs(rotation) < 1e-6)
            {
                context.DrawText(formatted, new Point(x, y));
                continue;
            }

            // 屏幕Y轴翻转，旋转角取反
            using (context.PushTransform(
                Matrix.CreateTranslation(-anchor.X, -anchor.Y) *
                Matrix.CreateRotation(-rotation) *
                Matrix.CreateTranslation(anchor.X, anchor.Y)))
            {
                context.DrawText(formatted, new Point(x, y));
            }
        }
    }

    private static void AddSegment(StreamGeometryContext ctx, Point from, Point to, double bulge)
    {
        if (Math.Abs(bulge) < 1e-9)
        {
            ctx.LineTo(to);
            return;
        }
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var chord = Math.Sqrt(dx * dx + dy * dy);
        if (chord < 1e-9)
        {
            ctx.LineTo(to);
            return;
        }
        var included = 4 * Math.Atan(bulge);
        var radius = Math.Abs(chord / (2 * Math.Sin(included / 2)));
        ctx.ArcTo(to, new Size(radius, radius), 0, Math.Abs(included) > Math.PI,
            bulge > 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
    }

    private static uint ColorAt(uint[] colors, int index) =>
        index < colors.Length ? colors[index] : 0;

    private static double At(double[] array, int index) =>
        index < array.Length ? array[index] : 0;

    private readonly record struct Vec(double X, double Y, double Z)
    {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        public double Dot(Vec o) => X * o.X + Y * o.Y + Z * o.Z;
        public Vec Cross(Vec o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    }
}
