using System.Collections.Generic;

namespace Glance.Processing;

public record ParsedCad(
    string Format,
    byte[] Content,
    CadSummary Summary,
    IReadOnlyList<string> Diagnostics
);
