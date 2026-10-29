using System.Collections.Generic;
using System.Threading.Tasks;
using Glance.Processing;
using Glance.Views;
using Xunit;

namespace Glance.Tests;

public class HighlightTransformerTests
{
    private static HighlightToken Tok(int start, int end, string kind) => new(start, end, kind);

    [Fact]
    public void 空token返回空runs()
    {
        Assert.Empty(HighlightTransformer.BuildRuns(new List<HighlightToken>(), 0, 100, false));
    }

    [Fact]
    public void 相邻同色token合并为一个run()
    {
        // 亮色下keyword与operator同为红
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword"), Tok(6, 10, "operator") }, 0, 20, false);

        var run = Assert.Single(runs);
        Assert.Equal(0, run.Start);
        Assert.Equal(10, run.End);
    }

    [Fact]
    public void 不同色token不合并()
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword"), Tok(6, 10, "string") }, 0, 20, false);

        Assert.Equal(2, runs.Count);
        Assert.Equal(0, runs[0].Start);
        Assert.Equal(5, runs[0].End);
        Assert.Equal(6, runs[1].Start);
        Assert.Equal(10, runs[1].End);
        Assert.NotEqual(runs[0].Brush, runs[1].Brush);
    }

    [Fact]
    public void 乱序token按行内位置排序()
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(6, 10, "string"), Tok(0, 5, "keyword") }, 0, 20, false);

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Start < runs[1].Start);
    }

    [Fact]
    public void 重叠token不合并()
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 10, "keyword"), Tok(5, 15, "keyword") }, 0, 20, false);

        Assert.Equal(2, runs.Count);
        Assert.Equal((0, 10), (runs[0].Start, runs[0].End));
        Assert.Equal((5, 15), (runs[1].Start, runs[1].End));
    }

    [Fact]
    public void 越界偏移被clamp到行范围内()
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(-5, 3, "keyword"), Tok(18, 25, "keyword") }, 0, 20, false);

        var run = Assert.Single(runs);
        Assert.Equal(0, run.Start);
        Assert.Equal(20, run.End);
    }

    [Fact]
    public void 完全越行token被丢弃()
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(20, 30, "keyword") }, 0, 20, false);

        Assert.Empty(runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 同色异类token共享同一brush实例(bool dark)
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword"), Tok(6, 10, "operator") }, 0, 20, dark);

        var run = Assert.Single(runs);
        Assert.Same(runs[0].Brush, run.Brush);
    }

    [Fact]
    public Task 明暗主题产生不同brush()
    {
        // SolidColorBrush.Color是UI线程属性，断言须经无头会话调度
        return AvaloniaTest.Run(() =>
        {
            var light = HighlightTransformer.BuildRuns(
                new[] { Tok(0, 5, "keyword") }, 0, 20, false);
            var dark = HighlightTransformer.BuildRuns(
                new[] { Tok(0, 5, "keyword") }, 0, 20, true);

            Assert.NotEqual(light[0].Brush.Color, dark[0].Brush.Color);
        });
    }
}
