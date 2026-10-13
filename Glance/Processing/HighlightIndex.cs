using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Glance.Processing;

public sealed class HighlightIndex
{
    private readonly byte[] _data;
    private readonly byte[] _lineIndex;
    private readonly IReadOnlyList<string> _kinds;

    public HighlightIndex(byte[] data, byte[] lineIndex, IReadOnlyList<string> kinds)
    {
        _data = data;
        _lineIndex = lineIndex;
        _kinds = kinds;
    }

    public int LineCount => Math.Max(0, _lineIndex.Length / IntBytes - 1);

    public int TotalTokenCount => _data.Length / TokenBytes;

    public int TokenCountOf(int line)
    {
        if (line < 0 || line >= LineCount) return 0;
        return BinaryPrimitives.ReadInt32LittleEndian(_lineIndex.AsSpan((line + 1) * IntBytes))
             - BinaryPrimitives.ReadInt32LittleEndian(_lineIndex.AsSpan(line * IntBytes));
    }

    public IReadOnlyList<HighlightToken> TokensOf(int line)
    {
        var count = TokenCountOf(line);
        if (count == 0) return Array.Empty<HighlightToken>();
        var off = BinaryPrimitives.ReadInt32LittleEndian(_lineIndex.AsSpan(line * IntBytes)) * TokenBytes;
        var result = new List<HighlightToken>(count);
        for (var i = 0; i < count; i++)
        {
            var start = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(off));
            var end = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(off + IntBytes));
            var kindIndex = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(off + 2 * IntBytes));
            result.Add(new HighlightToken(
                start,
                end,
                kindIndex >= 0 && kindIndex < _kinds.Count ? _kinds[kindIndex] : string.Empty
            ));
            off += TokenBytes;
        }
        return result;
    }

    public IReadOnlyList<HighlightToken> AllTokens()
    {
        var result = new List<HighlightToken>();
        for (var i = 0; i < LineCount; i++)
            result.AddRange(TokensOf(i));
        return result;
    }

    public static HighlightIndex Empty() => new(Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<string>());

    private const int TokenBytes = 12;
    private const int IntBytes = 4;
}
