using System.Collections.Generic;
using System.IO;
using Glance.Native;

namespace Glance.Processing;

public sealed class UniffiCodeParser : ICodeParser
{
    public ParsedCode ParseCode(string source, string filename)
    {
        var native = UniffiCodeParserMethods.ParseCode(source, filename);
        return new ParsedCode(
            Path.GetExtension(filename),
            source,
            new HighlightIndex(native.HighlightData, native.LineIndex, native.Kinds),
            native.Outline
        );
    }
}
