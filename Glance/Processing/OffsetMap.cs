using System.Text;

namespace Glance.Processing;

public sealed class OffsetMap
{
    private readonly int[] _byteToChar;
    public int CharLength { get; }

    public OffsetMap(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        _byteToChar = new int[utf8.Length + 1];
        var charIdx = 0;
        var i = 0;
        while (i < utf8.Length)
        {
            var lb = utf8[i];
            int cpLen, codepoint;
            if (lb < 0x80) { cpLen = 1; codepoint = lb; }
            else if ((lb & 0xE0) == 0xC0) { cpLen = 2; codepoint = ((lb & 0x1F) << 6) | (utf8[i + 1] & 0x3F); }
            else if ((lb & 0xF0) == 0xE0) { cpLen = 3; codepoint = ((lb & 0x0F) << 12) | ((utf8[i + 1] & 0x3F) << 6) | (utf8[i + 2] & 0x3F); }
            else { cpLen = 4; codepoint = ((lb & 0x07) << 18) | ((utf8[i + 1] & 0x3F) << 12) | ((utf8[i + 2] & 0x3F) << 6) | (utf8[i + 3] & 0x3F); }
            for (var k = 0; k < cpLen; k++) _byteToChar[i + k] = charIdx;
            charIdx += codepoint > 0xFFFF ? 2 : 1;
            i += cpLen;
        }
        _byteToChar[utf8.Length] = charIdx;
        CharLength = charIdx;
    }

    public int ToCharIndex(int byteOffset)
    {
        if (byteOffset <= 0) return 0;
        if (byteOffset >= _byteToChar.Length) return CharLength;
        return _byteToChar[byteOffset];
    }
}
