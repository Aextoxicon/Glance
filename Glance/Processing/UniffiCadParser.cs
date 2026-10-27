using System.Collections.Generic;
using Glance.Native;

namespace Glance.Processing;

public sealed class UniffiCadParser : ICadParser
{
    public ParsedCad ParseCad(byte[] fileBytes, string filename)
    {
        var native = UniffiCodeParserMethods.ParseCad(fileBytes, filename);
        return new ParsedCad(
            native.Format,
            fileBytes,
            new CadSummary(
                native.Format,
                native.TypeNames,
                native.EntityCounts,
                native.LayerNames,
                native.Diagnostics,
                native.Version),
            native.Diagnostics);
    }

    public CadGeometry ParseGeometry(byte[] fileBytes, string filename) =>
        UniffiCodeParserMethods.ParseCadGeometry(fileBytes, filename);
}
