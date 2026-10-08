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

    public int LineCount => System.Math.Max(0, _lineIndex.Length / IntBytes - 1);

    public int TotalTokenCount => _data.Length / TokenBytes;

    public int TokenCountOf(int line)
    {
        if (line < 0 || line >= LineCount) return 0;
        return ReadInt32LittleEndian(_lineIndex, (line + 1) * IntBytes) - ReadInt32LittleEndian(_lineIndex, line * IntBytes);
    }

    public IReadOnlyList<HighlightToken> TokensOf(int line)
    {
        var count = TokenCountOf(line);
        if (count == 0) return System.Array.Empty<HighlightToken>();
        var off = ReadInt32LittleEndian(_lineIndex, line * IntBytes) * TokenBytes;
        var result = new List<HighlightToken>(count);
        for (var i = 0; i < count; i++)
        {
            var start = ReadInt32LittleEndian(_data, off);
            var end = ReadInt32LittleEndian(_data, off + IntBytes);
            var kindIndex = ReadInt32LittleEndian(_data, off + 2 * IntBytes);
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

    public static HighlightIndex Empty() => new(System.Array.Empty<byte>(), System.Array.Empty<byte>(), System.Array.Empty<string>());

    private const int TokenBytes = 12;
    private const int IntBytes = 4;

    private static int ReadInt32LittleEndian(byte[] src, int offset)
    {
        return (src[offset] & 0xFF)
             | ((src[offset + 1] & 0xFF) << 8)
             | ((src[offset + 2] & 0xFF) << 16)
             | ((src[offset + 3] & 0xFF) << 24);
    }
}
