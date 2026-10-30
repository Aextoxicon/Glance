using System.Collections.Generic;
using System.Threading.Tasks;
using Glance.Processing;
using Glance.Views;
using Xunit;

namespace Glance.Tests;

// UI线程对象，全部经AvaloniaTest.Run调度
public class HighlightTransformerTests
{
    private static HighlightToken Tok(int start, int end, string kind) => new(start, end, kind);

    [Fact]
    public Task 空token返回空runs() => AvaloniaTest.Run(() =>
    {
        Assert.Empty(HighlightTransformer.BuildRuns(new List<HighlightToken>(), 0, 100, false));
    });

    [Fact]
    public Task 相邻同色token合并为一个run() => AvaloniaTest.Run(() =>
    {
        // 亮色下keyword与operator同为红
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword"), Tok(6, 10, "operator") }, 0, 20, false);

        var run = Assert.Single(runs);
        Assert.Equal(0, run.Start);
        Assert.Equal(10, run.End);
    });

    [Fact]
    public Task 不同色token不合并() => AvaloniaTest.Run(() =>
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword"), Tok(6, 10, "string") }, 0, 20, false);

        Assert.Equal(2, runs.Count);
        Assert.Equal(0, runs[0].Start);
        Assert.Equal(5, runs[0].End);
        Assert.Equal(6, runs[1].Start);
        Assert.Equal(10, runs[1].End);
        Assert.NotEqual(runs[0].Brush, runs[1].Brush);
    });

    [Fact]
    public Task 乱序token按行内位置排序() => AvaloniaTest.Run(() =>
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(6, 10, "string"), Tok(0, 5, "keyword") }, 0, 20, false);

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Start < runs[1].Start);
    });

    [Fact]
    public Task 重叠token不合并() => AvaloniaTest.Run(() =>
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 10, "keyword"), Tok(5, 15, "keyword") }, 0, 20, false);

        Assert.Equal(2, runs.Count);
        Assert.Equal((0, 10), (runs[0].Start, runs[0].End));
        Assert.Equal((5, 15), (runs[1].Start, runs[1].End));
    });

    [Fact]
    public Task 越界偏移被clamp到行范围内() => AvaloniaTest.Run(() =>
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(-5, 3, "keyword"), Tok(18, 25, "keyword") }, 0, 20, false);

        var run = Assert.Single(runs);
        Assert.Equal(0, run.Start);
        Assert.Equal(20, run.End);
    });

    [Fact]
    public Task 完全越行token被丢弃() => AvaloniaTest.Run(() =>
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(20, 30, "keyword") }, 0, 20, false);

        Assert.Empty(runs);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task 同色异类token共享同一brush实例(bool dark) => AvaloniaTest.Run(() =>
    {
        var runs = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword"), Tok(6, 10, "operator") }, 0, 20, dark);

        var run = Assert.Single(runs);
        Assert.Same(runs[0].Brush, run.Brush);
    });

    [Fact]
    public Task 明暗主题产生不同brush() => AvaloniaTest.Run(() =>
    {
        var light = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword") }, 0, 20, false);
        var dark = HighlightTransformer.BuildRuns(
            new[] { Tok(0, 5, "keyword") }, 0, 20, true);

        Assert.NotEqual(light[0].Brush.Color, dark[0].Brush.Color);
    });
}
