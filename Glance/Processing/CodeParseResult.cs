using System.Collections.Generic;
using Glance.Native;

namespace Glance.Processing;

public record ParsedCode(
    string Language,
    string Content,
    HighlightIndex Highlights,
    IReadOnlyList<OutlineNode> Outline
);
