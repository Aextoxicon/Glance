using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Glance.Processing;
using Xunit;

namespace Glance.Tests;

public class HighlightIndexTests
{
    private static byte[] Ints(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    private static byte[] Tokens(params (int Start, int End, int Kind)[] tokens)
    {
        var bytes = new byte[tokens.Length * 12];
        for (var i = 0; i < tokens.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 12), tokens[i].Start);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 12 + 4), tokens[i].End);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 12 + 8), tokens[i].Kind);
        }
        return bytes;
    }

    [Fact]
    public void 空索引行数为0()
    {
        var idx = HighlightIndex.Empty();
        Assert.Equal(0, idx.LineCount);
        Assert.Equal(0, idx.TotalTokenCount);
        Assert.Equal(0, idx.TokenCountOf(0));
        Assert.Empty(idx.TokensOf(0));
    }

    [Fact]
    public void 单行三枚记号偏移与种类正确()
    {
        var idx = new HighlightIndex(
            Tokens((0, 5, 0), (6, 10, 1), (11, 15, 0)),
            Ints(0, 3),
            new List<string> { "keyword", "string" });

        Assert.Equal(1, idx.LineCount);
        Assert.Equal(3, idx.TotalTokenCount);
        Assert.Equal(3, idx.TokenCountOf(0));

        var tokens = idx.TokensOf(0);
        Assert.Collection(tokens,
            t => { Assert.Equal(0, t.StartByte); Assert.Equal(5, t.EndByte); Assert.Equal("keyword", t.Kind); },
            t => { Assert.Equal(6, t.StartByte); Assert.Equal(10, t.EndByte); Assert.Equal("string", t.Kind); },
            t => { Assert.Equal(11, t.StartByte); Assert.Equal(15, t.EndByte); Assert.Equal("keyword", t.Kind); });
    }

    [Fact]
    public void 多行按行索引分段()
    {
        var idx = new HighlightIndex(
            Tokens((0, 2, 0), (3, 5, 0), (10, 12, 0)),
            Ints(0, 2, 3),
            new List<string> { "keyword" });

        Assert.Equal(2, idx.LineCount);
        Assert.Equal(2, idx.TokenCountOf(0));
        Assert.Equal(1, idx.TokenCountOf(1));
        Assert.Equal(2, idx.TokensOf(0).Count);
        Assert.Equal(10, idx.TokensOf(1)[0].StartByte);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(99)]
    public void 越界行返回0而不是抛异常(int line)
    {
        var idx = new HighlightIndex(
            Tokens((0, 2, 0)),
            Ints(0, 1),
            new List<string> { "keyword" });

        Assert.Equal(0, idx.TokenCountOf(line));
        Assert.Empty(idx.TokensOf(line));
    }

    [Fact]
    public void 种类索引越界返回空字符串()
    {
        var idx = new HighlightIndex(
            Tokens((0, 2, 42)),
            Ints(0, 1),
            new List<string> { "keyword" });

        Assert.Equal(string.Empty, idx.TokensOf(0)[0].Kind);
    }

    [Fact]
    public void 负种类索引不抛异常()
    {
        var idx = new HighlightIndex(
            Tokens((0, 2, -1)),
            Ints(0, 1),
            new List<string> { "keyword" });

        Assert.Equal(string.Empty, idx.TokensOf(0)[0].Kind);
    }
}
