using System.Collections.Generic;
using Glance.Native;

namespace Glance.Processing;

public sealed class UniffiCodeParser : ICodeParser
{
    public ParsedCode ParseCode(string source, string extension)
    {
        var native = UniffiCodeParserMethods.ParseCode(source, extension);
        return new ParsedCode(
            extension,
            source,
            new HighlightIndex(native.HighlightData, native.LineIndex, native.Kinds),
            native.Outline
        );
    }
}
